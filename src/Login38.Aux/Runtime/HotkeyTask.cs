using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Runs a command when the player presses F1 to F4 in the game.
/// </summary>
/// <remarks>
/// <para>
/// The one helper feature the player drives rather than the other way round. Four rows,
/// each a command in the same syntax as everything else, fired by a key rather than by a
/// clock or by a number going down.
/// </para>
/// <para>
/// Two threads are involved and the split matters. The keyboard hook runs on an operating
/// system input thread with a deadline it must not miss, so all it does is note which key
/// it was. The command itself — reading the bag, sending a packet — happens here, on the
/// helper's own loop, a fraction of a second later.
/// </para>
/// </remarks>
public sealed class HotkeyTask : IAuxTask, IAuxTaskShutdown
{
    /// <summary>F1 through F4.</summary>
    internal static readonly int[] Keys = [0x70, 0x71, 0x72, 0x73];

    private readonly HelperDispatch _dispatch;
    private readonly AuxRuntimeOptions _options;
    private readonly AuxWakeSignal _wake;
    private readonly ILogger<HotkeyTask> _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan?[] _lastFired = new TimeSpan?[AuxSettings.FunctionKeyMacros];
    private readonly bool[] _wanted = new bool[AuxSettings.FunctionKeyMacros];

    private KeyboardHook? _hook;
    private bool _tried;

    public HotkeyTask(
        HelperDispatch dispatch,
        AuxRuntimeOptions options,
        AuxWakeSignal wake,
        ILogger<HotkeyTask> logger)
    {
        _dispatch = dispatch;
        _options = options;
        _wake = wake;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "hotkeys";

    /// <summary>
    /// Runs on every helper pass, including passes woken early by a function-key event.
    /// </summary>
    /// <remarks>
    /// The keyboard hook wakes the same helper thread immediately, so the first action does
    /// not wait for the normal 100 ms cadence. Windows key-repeat remains the source of held
    /// presses; the Encoder cooldown still decides which repeats are allowed to fire.
    /// </remarks>
    public TimeSpan Interval => TimeSpan.Zero;

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Wanted(context.Settings.Macros))
        {
            StopListening();
            return;
        }

        if (!Listening(context))
        {
            return;
        }

        Collect();

        if (!context.IsInWorld)
        {
            // Keys pressed on the character screen are the player using the client's own
            // interface, not asking for a macro.
            Array.Clear(_wanted);

            return;
        }

        Fire(context);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A low-level hook left behind would keep swallowing F1 to F4 for the whole machine
    /// after the game has gone, which is a good deal worse than the feature not working.
    /// </remarks>
    public void Stopping() => StopListening();


    /// <summary>Whether at least one function-key macro can actually run.</summary>
    internal static bool Wanted(FunctionKeyMacro[] macros)
    {
        ArgumentNullException.ThrowIfNull(macros);

        return macros.Any(static macro =>
            macro.Enabled && !string.IsNullOrWhiteSpace(macro.Command));
    }

    private void StopListening()
    {
        _hook?.Dispose();
        _hook = null;
        _tried = false;
        Array.Clear(_wanted);
    }

    /// <summary>Which macro a virtual key belongs to.</summary>
    internal static int? RowOf(int key)
    {
        var row = Array.IndexOf(Keys, key);

        return row < 0 ? null : row;
    }

    /// <summary>
    /// Whether a row may fire now.
    /// </summary>
    /// <remarks>
    /// A row that has never fired may fire at once — the cooldown is against a key being
    /// held, not against the first press after the game starts.
    /// </remarks>
    internal static bool Ready(TimeSpan? lastFired, TimeSpan now, TimeSpan cooldown) =>
        lastFired is not { } last || now - last >= cooldown;

    /// <summary>
    /// Installs the hook, once.
    /// </summary>
    /// <remarks>
    /// Not retried on failure. The one reason it fails is that this process may not install
    /// a hook at all, which will not have changed by the next pass, and a hundred attempts
    /// a second at something that cannot work is worse than the feature being off.
    /// </remarks>
    private bool Listening(AuxContext context)
    {
        if (_hook is not null)
        {
            return true;
        }

        if (_tried)
        {
            return false;
        }

        _tried = true;

        try
        {
            _hook = KeyboardHook.Install(Keys, context.Process.Id, _wake.Signal);

            _logger.LogInformation("F1 to F4 are being watched for in the game");

            return true;
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning(e, "The function keys could not be hooked; the macros are off");

            return false;
        }
    }

    /// <summary>Takes everything the hook has seen since the last pass.</summary>
    private void Collect()
    {
        while (_hook is not null && _hook.TryTake(out var key))
        {
            if (RowOf(key) is { } row)
            {
                _wanted[row] = true;
            }
        }
    }

    /// <summary>
    /// Runs at most one macro.
    /// </summary>
    /// <remarks>
    /// One per pass for the same reason the timers fire one per pass: the client casts one
    /// skill at a time. The rest stay wanted and go on the next pass, a tenth of a second
    /// later, which is faster than anybody can press two keys anyway.
    /// </remarks>
    private void Fire(AuxContext context)
    {
        var now = _clock.Elapsed;

        if (Take(context.Settings.Macros, _wanted, _lastFired, now, _options.FunctionKeyCooldown) is not { } row)
        {
            return;
        }

        var command = context.Settings.Macros[row].Command;

        _lastFired[row] = now;

        _logger.LogInformation("F{Key}: {Command}", row + 1, command);

        _dispatch.Send(context.Process, HelperEntrySyntax.Parse(command), context.Bag);
    }

    /// <summary>
    /// Takes the next press worth acting on, and forgets the ones that are not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A press on a key with nothing bound to it is forgotten rather than held, so binding
    /// something to that key later does not fire it the moment it is bound. A press inside
    /// the cooldown is forgotten for the same reason it exists — a key being held down
    /// should do one thing, not thirty.
    /// </para>
    /// <para>
    /// Presses past the one taken are left alone, so two keys pressed together both happen,
    /// one pass apart.
    /// </para>
    /// </remarks>
    internal static int? Take(
        FunctionKeyMacro[] macros,
        bool[] wanted,
        TimeSpan?[] lastFired,
        TimeSpan now,
        TimeSpan cooldown)
    {
        ArgumentNullException.ThrowIfNull(macros);
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(lastFired);

        for (var row = 0; row < wanted.Length; row++)
        {
            if (!wanted[row])
            {
                continue;
            }

            wanted[row] = false;

            var macro = macros[row];

            if (macro.Enabled && !string.IsNullOrWhiteSpace(macro.Command) &&
                Ready(lastFired[row], now, cooldown))
            {
                return row;
            }
        }

        return null;
    }
}
