using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>
/// The game's main window, found once and then remembered.
/// </summary>
/// <remarks>
/// <para>
/// Found by process id, never by title. The client always names its window the same
/// thing, so <c>FindWindow</c> by title returns whichever copy Windows considers topmost
/// — and with two copies running, a message meant for one lands in the other. That was a
/// real defect in the reference, fixed there the same way.
/// </para>
/// <para>
/// The handle is captured when the window first becomes visible and used from then on, so
/// it keeps identifying the same window even after the title has been changed out from
/// under it.
/// </para>
/// </remarks>
public sealed class GameWindow
{
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(100);

    private GameWindow(nint handle, uint processId, string title)
    {
        Handle = handle;
        ProcessId = processId;
        InitialTitle = title;
    }

    /// <summary>The window handle.</summary>
    public nint Handle { get; }

    /// <summary>The process that owns it.</summary>
    public uint ProcessId { get; }

    /// <summary>What the window was called when it was found.</summary>
    public string InitialTitle { get; }

    /// <summary>Finds the process's first visible window, or null if it has none yet.</summary>
    public static GameWindow? Find(uint processId)
    {
        nint found = 0;
        var title = string.Empty;

        // The callback runs on this thread before EnumWindows returns, so capturing here
        // is safe and needs no pinning.
        User32.EnumWindows(
            (window, _) =>
            {
                if (!User32.IsWindowVisible(window))
                {
                    return true;
                }

                User32.GetWindowThreadProcessId(window, out var owner);

                if (owner != processId)
                {
                    return true;
                }

                var text = ReadTitle(window);

                // A window with no title is one of the client's own helper windows, not the
                // one it plays in.
                if (string.IsNullOrWhiteSpace(text))
                {
                    return true;
                }

                found = window;
                title = text;
                return false;
            },
            0);

        return found == 0 ? null : new GameWindow(found, processId, title);
    }

    /// <summary>
    /// Waits for the process to show a window.
    /// </summary>
    /// <remarks>
    /// The client unpacks itself, loads its data and initialises DirectDraw before it
    /// shows anything, so this is on the order of seconds even on a fast machine.
    /// </remarks>
    /// <returns>The window, or null if it never appeared.</returns>
    public static async Task<GameWindow?> WaitAsync(
        uint processId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (true)
        {
            if (Find(processId) is { } window)
            {
                return window;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return null;
            }

            await Task.Delay(DefaultPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the game painting over its own child windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client creates its main window without <c>WS_CLIPCHILDREN</c>, and presents
    /// each frame by blitting through a DirectDraw clipper bound to that window. Without
    /// the style, the clipper's visible region includes the area covered by child windows,
    /// so every frame paints over the text boxes — which shows up as a black input box
    /// that flickers while typing, and an IME candidate list that never appears.
    /// </para>
    /// <para>
    /// Only needed when the in-process present hook is not running. With it, the swapchain
    /// composes child windows itself and setting this as well causes flicker of its own.
    /// </para>
    /// </remarks>
    /// <returns>Whether the style had to be added.</returns>
    public bool EnableChildClipping()
    {
        const int Wanted = (int)(WindowStyles.ClipChildren | WindowStyles.ClipSiblings);

        var current = User32.GetWindowLong(Handle, User32.GwlStyle);

        if (current == 0)
        {
            throw new GameProcessException("The game window has no style; the handle is stale.");
        }

        if ((current & Wanted) == Wanted)
        {
            return false;
        }

        User32.SetWindowLong(Handle, User32.GwlStyle, current | Wanted);

        // The clipper only recomputes its region on a frame change, so without this the
        // style is set but nothing looks different until the window is resized.
        if (!User32.SetWindowPos(
                Handle, 0, 0, 0, 0, 0,
                SetWindowPosFlags.NoMove | SetWindowPosFlags.NoSize |
                SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate |
                SetWindowPosFlags.FrameChanged))
        {
            throw new GameProcessException(
                "SetWindowPos failed.", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return true;
    }

    /// <summary>Renames the window.</summary>
    public void SetTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (!User32.SetWindowText(Handle, title))
        {
            throw new GameProcessException(
                "SetWindowText failed.", new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static unsafe string ReadTitle(nint window)
    {
        var length = User32.GetWindowTextLength(window);

        if (length <= 0)
        {
            return string.Empty;
        }

        // Titles are short and this runs while polling for the window, so the buffer
        // stays on the stack rather than allocating once every hundred milliseconds.
        Span<char> buffer = length < 256 ? stackalloc char[length + 1] : new char[length + 1];

        fixed (char* text = buffer)
        {
            var copied = User32.GetWindowText(window, text, buffer.Length);
            return copied <= 0 ? string.Empty : new string(buffer[..copied]);
        }
    }

    /// <summary>
    /// Builds a random window title.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Third-party tools find this client with <c>FindWindow</c> against its fixed title.
    /// Giving each launch a different one is enough to stop that without touching anything
    /// the client itself depends on — it never reads its own title back.
    /// </para>
    /// <para>
    /// Deliberately a plain xorshift seeded by the caller rather than a cryptographic
    /// generator: the requirement is "different every time", and a deterministic function
    /// of a seed is testable.
    /// </para>
    /// </remarks>
    public static string RandomTitle(ulong seed)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        // A zero state makes xorshift produce zero forever.
        var state = seed | 1;

        ulong Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        var length = 8 + (int)(Next() % 9);
        var title = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            title.Append(Alphabet[(int)(Next() % (ulong)Alphabet.Length)]);
        }

        return title.ToString();
    }

    /// <summary>A seed that differs between launches and between copies of the game.</summary>
    public static ulong TitleSeed(uint processId) =>
        ((ulong)processId << 32) ^ (uint)Environment.TickCount ^ 0x9E37_79B9_7F4A_7C15;

    /// <summary>Whether the window is on screen at all.</summary>
    public bool IsVisible => User32.IsWindowVisible(Handle);

    /// <summary>Whether it has been minimised.</summary>
    public bool IsMinimised => User32.IsIconic(Handle);

    /// <summary>
    /// Whether the game is the window the player is currently using.
    /// </summary>
    /// <remarks>
    /// By process rather than by handle: the client has more than one top-level window and
    /// which of them has the focus is its own business. What matters is whether the player
    /// is looking at this game or at something else.
    /// </remarks>
    public bool IsForeground
    {
        get
        {
            var owner = ForegroundProcessId;

            return owner != 0 && owner == ProcessId;
        }
    }

    /// <summary>
    /// Which process owns the window the player is using, or zero if there is none.
    /// </summary>
    /// <remarks>
    /// Static, and cheap, because the question is asked by things that have no window to
    /// start from — a global key press, which belongs to whichever game is in front rather
    /// than to any particular one. Finding a <see cref="GameWindow"/> first would mean
    /// walking every top-level window on the desktop to answer it.
    /// </remarks>
    public static uint ForegroundProcessId
    {
        get
        {
            var foreground = User32.GetForegroundWindow();

            if (foreground == 0)
            {
                return 0;
            }

            User32.GetWindowThreadProcessId(foreground, out var owner);

            return owner;
        }
    }

    /// <summary>
    /// Where the game's picture is on the desktop, or null if it cannot be asked.
    /// </summary>
    /// <remarks>
    /// The client area rather than the whole window: anything drawn over the game has to
    /// line up with what the game drew, and the border and title bar are not part of that.
    /// </remarks>
    public ScreenArea? ClientArea()
    {
        if (!User32.GetClientRect(Handle, out var client))
        {
            return null;
        }

        var topLeft = new Win32.Point { X = client.Left, Y = client.Top };
        var bottomRight = new Win32.Point { X = client.Right, Y = client.Bottom };

        return User32.ClientToScreen(Handle, ref topLeft) && User32.ClientToScreen(Handle, ref bottomRight)
            ? new ScreenArea(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y)
            : null;
    }
}

/// <summary>A rectangle on the desktop, in pixels.</summary>
/// <param name="X">From the left of the desktop.</param>
/// <param name="Y">From the top.</param>
/// <param name="Width">How wide.</param>
/// <param name="Height">How tall.</param>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height)
{
    /// <summary>Whether it has any area at all.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
