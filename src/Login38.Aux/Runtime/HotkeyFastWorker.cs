using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Low-latency F1-F4 worker used when the operator enables high-speed mode.
/// </summary>
/// <remarks>
/// The keyboard hook only records edges and signals this thread. The worker owns held-key
/// state and repeat deadlines, so Windows keyboard-repeat settings do not affect macro rate.
/// It never catches up missed deadlines: after a late action the next one is scheduled from
/// the actual fire time, preventing a stalled client from receiving a burst of queued uses.
/// </remarks>
public sealed class HotkeyFastWorker : IDisposable
{
    private static readonly TimeSpan PostPotionDelay = AuxHost.Cadence;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(6);

    private readonly HelperDispatch _dispatch;
    private readonly AuxSettingsSource _settings;
    private readonly AuxRuntimeOptions _options;
    private readonly PotionTask _potions;
    private readonly ActionArbiter _arbiter;
    private readonly ILegacyTextCodec _codec;
    private readonly HelperSwitch _helperSwitch;
    private readonly ILogger<HotkeyFastWorker> _logger;
    private readonly AutoResetEvent _signal = new(false);
    private readonly object _lifecycle = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool[] _held = new bool[AuxSettings.FunctionKeyMacros];
    private readonly bool[] _pending = new bool[AuxSettings.FunctionKeyMacros];
    private readonly TimeSpan?[] _lastFired = new TimeSpan?[AuxSettings.FunctionKeyMacros];
    private readonly TimeSpan?[] _nextDue = new TimeSpan?[AuxSettings.FunctionKeyMacros];

    private KeyboardHook? _hook;
    private Thread? _thread;
    private RemoteProcess? _process;
    private int _stopping;
    private bool _disposed;
    private TimeSpan _blockedUntil;

    public HotkeyFastWorker(
        HelperDispatch dispatch,
        AuxSettingsSource settings,
        AuxRuntimeOptions options,
        PotionTask potions,
        ActionArbiter arbiter,
        ILegacyTextCodec codec,
        HelperSwitch helperSwitch,
        ILogger<HotkeyFastWorker> logger)
    {
        _dispatch = dispatch;
        _settings = settings;
        _options = options;
        _potions = potions;
        _arbiter = arbiter;
        _codec = codec;
        _helperSwitch = helperSwitch;
        _logger = logger;
    }

    /// <summary>Whether the dedicated worker currently owns the F-key hook.</summary>
    public bool IsRunning => Volatile.Read(ref _thread) is { IsAlive: true };

    /// <summary>Starts the worker for one game process, once.</summary>
    public void Start(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_thread is { IsAlive: true })
            {
                return;
            }

            Array.Clear(_held);
            Array.Clear(_pending);
            Array.Clear(_nextDue);
            _blockedUntil = TimeSpan.Zero;
            _process = process;
            Volatile.Write(ref _stopping, 0);

            _hook = KeyboardHook.Install(HotkeyTask.Keys, process.Id, Signal);
            _thread = new Thread(Run)
            {
                Name = "F1-F4 fast worker",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();

            _logger.LogInformation("F1 to F4 high-speed worker started");
        }
    }

    /// <summary>Stops swallowing keys and cancels held-key repeats.</summary>
    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_thread is null && _hook is null)
            {
                return;
            }

            Volatile.Write(ref _stopping, 1);
            _signal.Set();

            var thread = _thread;

            if (thread is not null && thread != Thread.CurrentThread && thread.IsAlive)
            {
                thread.Join(StopTimeout);
            }

            _hook?.Dispose();
            _hook = null;
            _thread = null;
            _process = null;
            Array.Clear(_held);
            Array.Clear(_pending);
            Array.Clear(_nextDue);

            _logger.LogInformation("F1 to F4 high-speed worker stopped");
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        _signal.Dispose();
    }

    private void Signal() => _signal.Set();

    private void Run()
    {
        try
        {
            while (Volatile.Read(ref _stopping) == 0)
            {
                DrainEdges();

                if (Volatile.Read(ref _stopping) != 0)
                {
                    return;
                }

                var now = _clock.Elapsed;

                if (!_helperSwitch.IsOn || _process is not { } process || !process.IsRunning)
                {
                    _signal.WaitOne();
                    continue;
                }

                if (_hook is null || !_hook.WatchedProcessIsInFront)
                {
                    ClearRequests();
                    _signal.WaitOne();
                    continue;
                }

                var settings = _settings.Current;

                if (!HotkeyTask.Wanted(settings.Macros))
                {
                    ClearRequests();
                    _signal.WaitOne();
                    continue;
                }

                if (now < _blockedUntil)
                {
                    WaitUntil(_blockedUntil);
                    continue;
                }

                if (ReadyRow(settings.Macros, now) is { } row)
                {
                    var context = new AuxContext(process, settings, _codec);

                    if (!context.IsInWorld)
                    {
                        ClearRequests();
                        _signal.WaitOne();
                        continue;
                    }

                    // Recovery is checked on the same fresh snapshot immediately before a
                    // hotkey action. This preserves Potion > Hotkey even though this thread
                    // no longer waits for the helper's 100 ms polling pass.
                    if (_potions.TryAct(context))
                    {
                        _blockedUntil = _clock.Elapsed + PostPotionDelay;
                        continue;
                    }

                    Fire(context, row);
                    continue;
                }

                if (NextDeadline(settings.Macros) is { } deadline)
                {
                    WaitUntil(deadline);
                }
                else
                {
                    _signal.WaitOne();
                }
            }
        }
        catch (Exception e) when (e is GameProcessException or InvalidOperationException or IOException or ObjectDisposedException)
        {
            _logger.LogWarning(e, "The F1-F4 high-speed worker stopped after a client error");
        }
        finally
        {
            Interlocked.Exchange(ref _hook, null)?.Dispose();
        }
    }

    private void DrainEdges()
    {
        while (_hook is not null && _hook.TryTakeEvent(out var keyEvent))
        {
            if (HotkeyTask.RowOf(keyEvent.Key) is not { } row)
            {
                continue;
            }

            if (keyEvent.IsDown)
            {
                // Windows emits repeated key-down events while a key is held. The worker
                // owns repeat timing, so only the physical down edge starts a request.
                if (!_held[row])
                {
                    _held[row] = true;
                    _pending[row] = true;
                }
            }
            else
            {
                _held[row] = false;
                _nextDue[row] = null;
            }
        }
    }

    private int? ReadyRow(FunctionKeyMacro[] macros, TimeSpan now)
    {
        for (var row = 0; row < _pending.Length; row++)
        {
            var macro = macros[row];

            if (!macro.Enabled || string.IsNullOrWhiteSpace(macro.Command))
            {
                _pending[row] = false;
                _nextDue[row] = null;
                continue;
            }

            if (_pending[row])
            {
                if (HotkeyTask.Ready(_lastFired[row], now, _options.FunctionKeyCooldown))
                {
                    return row;
                }

                // A tap during cooldown is discarded like the legacy path. A held key is
                // kept and becomes due exactly when that row's cooldown expires.
                if (!_held[row])
                {
                    _pending[row] = false;
                }
                else if (_lastFired[row] is { } last)
                {
                    _nextDue[row] = last + _options.FunctionKeyCooldown;
                }
            }

            if (_held[row] && _nextDue[row] is { } due && now >= due)
            {
                return row;
            }
        }

        return null;
    }

    private TimeSpan? NextDeadline(FunctionKeyMacro[] macros)
    {
        TimeSpan? earliest = null;

        for (var row = 0; row < _held.Length; row++)
        {
            if (!_held[row] || !macros[row].Enabled || string.IsNullOrWhiteSpace(macros[row].Command))
            {
                continue;
            }

            if (_nextDue[row] is { } due && (earliest is null || due < earliest.Value))
            {
                earliest = due;
            }
        }

        return earliest;
    }

    private void Fire(AuxContext context, int row)
    {
        var now = _clock.Elapsed;
        var command = context.Settings.Macros[row].Command;

        _pending[row] = false;
        _lastFired[row] = now;
        _nextDue[row] = _held[row] ? now + _options.FunctionKeyCooldown : null;

        _logger.LogInformation("F{Key} fast: {Command}", row + 1, command);

        using var priority = _arbiter.WithPriority(PlayerActionPriority.Hotkey);
        _dispatch.Send(context.Process, HelperEntrySyntax.Parse(command), context.Bag);
    }

    private void WaitUntil(TimeSpan deadline)
    {
        while (Volatile.Read(ref _stopping) == 0)
        {
            var remaining = deadline - _clock.Elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            // Short bounded waits make key-up cancellation responsive without polling the
            // rest of the helper or burning a core for the whole repeat interval.
            var slice = remaining > TimeSpan.FromMilliseconds(2)
                ? TimeSpan.FromMilliseconds(2)
                : remaining;

            if (_signal.WaitOne(slice))
            {
                return;
            }
        }
    }

    private void ClearRequests()
    {
        Array.Clear(_held);
        Array.Clear(_pending);
        Array.Clear(_nextDue);
    }
}
