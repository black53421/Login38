using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>One watched keyboard edge.</summary>
/// <param name="Key">Virtual-key code.</param>
/// <param name="IsDown">True for key-down, false for key-up.</param>
public readonly record struct KeyboardHookEvent(int Key, bool IsDown);

/// <summary>
/// Watches for a few keys being pressed while a particular process is in front.
/// </summary>
/// <remarks>
/// <para>
/// A low-level keyboard hook, which is how a key pressed in the game reaches the launcher
/// at all: the game has focus, so nothing else is being told about it.
/// </para>
/// <para>
/// Windows calls the hook on its own input thread and gives it a deadline. Miss it — by
/// reading another process's memory, taking a lock, or waiting on anything — and the hook
/// is quietly removed and every key after that goes unseen. So the callback here does one
/// thing: put a number on a queue. Whoever wants to act on it does so on their own thread.
/// </para>
/// <para>
/// The keys watched for are swallowed rather than passed on, and only while the watched
/// process is in front. Both matter: a key that reaches the game as well would do two
/// things at once, and one that fired while the player was typing somewhere else would
/// fire at the worst possible moment.
/// </para>
/// </remarks>
public sealed class KeyboardHook : IDisposable
{
    /// <summary>How long to wait for the hook thread to install the hook.</summary>
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>And to wait for it to leave once asked.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentQueue<KeyboardHookEvent> _events = new();
    private readonly TaskCompletionSource<Exception?> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly HashSet<int> _keys;
    private readonly Action? _notify;
    private readonly uint _processId;
    private readonly Thread _thread;

    // Held so the garbage collector cannot take the delegate the operating system is
    // calling through. A local would be collected while the hook was still installed.
    private readonly User32.HookProc _callback;

    private uint _threadId;
    private nint _hook;

    private KeyboardHook(IEnumerable<int> keys, uint processId, Action? notify)
    {
        _keys = [.. keys];
        _notify = notify;
        _processId = processId;
        _callback = OnKey;
        _thread = new Thread(Pump)
        {
            Name = "keyboard hook",
            IsBackground = true,
        };
    }

    /// <summary>
    /// Starts watching. The hook is installed before this returns.
    /// </summary>
    /// <param name="keys">Virtual key codes to watch for.</param>
    /// <param name="processId">Whose window has to be in front for a key to count.</param>
    /// <param name="notify">A non-blocking callback used to wake the consumer.</param>
    /// <exception cref="GameProcessException">The hook could not be installed.</exception>
    public static KeyboardHook Install(IEnumerable<int> keys, uint processId, Action? notify = null)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var hook = new KeyboardHook(keys, processId, notify);

        hook._thread.Start();

        if (!hook._ready.Task.Wait(InstallTimeout))
        {
            hook.Dispose();

            throw new GameProcessException("The keyboard hook thread did not start");
        }

        if (hook._ready.Task.Result is { } failure)
        {
            hook.Dispose();

            throw new GameProcessException("SetWindowsHookEx(WH_KEYBOARD_LL) failed", failure);
        }

        return hook;
    }

    /// <summary>Takes the next keyboard edge since the last call, if there was one.</summary>
    public bool TryTakeEvent(out KeyboardHookEvent keyEvent) => _events.TryDequeue(out keyEvent);

    /// <summary>
    /// Takes the next key-down, for the legacy polling path. Key-up edges are consumed.
    /// </summary>
    public bool TryTake(out int key)
    {
        while (_events.TryDequeue(out var keyEvent))
        {
            if (keyEvent.IsDown)
            {
                key = keyEvent.Key;
                return true;
            }
        }

        key = 0;
        return false;
    }

    /// <summary>How many keyboard edges are waiting.</summary>
    public int Waiting => _events.Count;

    /// <summary>Whether the watched game process currently owns the foreground window.</summary>
    public bool WatchedProcessIsInFront => InFront();

    /// <summary>
    /// Removes the hook and waits for its thread.
    /// </summary>
    /// <remarks>
    /// The thread is sitting in <c>GetMessage</c>, which returns when something is posted
    /// to it. Waiting on the thread without posting first would wait for ever.
    /// </remarks>
    public void Dispose()
    {
        var id = _threadId;

        if (id != 0)
        {
            User32.PostThreadMessage(id, User32.WmQuit, 0, 0);
        }

        if (_thread.IsAlive)
        {
            _thread.Join(StopTimeout);
        }
    }

    /// <summary>
    /// Installs the hook and pumps messages until told to stop.
    /// </summary>
    /// <remarks>
    /// The hook has to be installed from a thread with a message queue, and every message
    /// has to be taken off promptly, because that queue is what the operating system uses
    /// to call the hook back. A thread that sleeps here delays every keystroke on the
    /// machine, which is why stopping is done by posting rather than by polling a flag.
    /// </remarks>
    private void Pump()
    {
        // Forces the queue into existence before anything can be posted to it, so a stop
        // arriving immediately is not thrown away.
        User32.PeekMessage(out _, 0, 0, 0, User32.PeekNoRemove);

        _threadId = Kernel32.GetCurrentThreadId();
        _hook = User32.SetWindowsHookEx(User32.WhKeyboardLowLevel, _callback, 0, 0);

        if (_hook == 0)
        {
            _ready.TrySetResult(new Win32Exception(Marshal.GetLastWin32Error()));

            return;
        }

        _ready.TrySetResult(null);

        try
        {
            while (true)
            {
                var more = User32.GetMessage(out var message, 0, 0, 0);

                if (more <= 0)
                {
                    // Zero is WM_QUIT; below zero is an error, and staying in the loop
                    // after one would spin.
                    break;
                }

                User32.TranslateMessage(message);
                User32.DispatchMessage(message);
            }
        }
        finally
        {
            User32.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    /// <summary>
    /// Called by the operating system for every keystroke on the machine.
    /// </summary>
    /// <remarks>
    /// Everything on this path is chosen to be quick and to not block: a set lookup, a
    /// window query, and an enqueue. Nothing here reads the game.
    /// </remarks>
    private nint OnKey(int code, nint wparam, nint lparam)
    {
        if (code >= 0)
        {
            var message = (uint)wparam;
            var isDown = message is User32.WmKeyDown or User32.WmSysKeyDown;
            var isUp = message is User32.WmKeyUp or User32.WmSysKeyUp;

            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<KeyboardHookData>(lparam);
                var key = (int)data.VirtualKey;

                if (_keys.Contains(key) && InFront())
                {
                    _events.Enqueue(new KeyboardHookEvent(key, isDown));
                    _notify?.Invoke();

                    // Swallow both edges: letting key-up through after taking key-down lets
                    // the client's own F-key state machine see half of a press.
                    return 1;
                }
            }
        }

        return User32.CallNextHookEx(0, code, wparam, lparam);
    }

    /// <summary>Whether the window with focus belongs to the process being watched.</summary>
    private bool InFront()
    {
        var window = User32.GetForegroundWindow();

        if (window == 0)
        {
            return false;
        }

        User32.GetWindowThreadProcessId(window, out var owner);

        return owner == _processId;
    }
}
