using System.ComponentModel;
using System.Runtime.InteropServices;
using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>What the player did to the icon in the notification area.</summary>
public enum TrayAction
{
    /// <summary>Something this does not act on.</summary>
    None,

    /// <summary>A left click, which brings the launcher back.</summary>
    Open,

    /// <summary>A right click, which asks for the menu.</summary>
    Menu,
}

/// <summary>
/// An icon in the notification area, beside the clock.
/// </summary>
/// <remarks>
/// <para>
/// The launcher goes there once a game is running. It cannot simply exit — the patch
/// pipeline and every helper feature run in this process, polling the client from outside,
/// so a process that ended would take the helper with it. But leaving a launcher window in
/// front of the game is not what anybody wants either.
/// </para>
/// <para>
/// So the window is hidden and this is what is left: something small enough to be out of
/// the way and visible enough that a player can find their way back. A launcher that
/// vanished completely would be one the player could only reach from inside the game,
/// which is fine until the game will not take the key.
/// </para>
/// <para>
/// The owning window handle comes from the caller. Anything that receives messages needs
/// one, and the presentation layer already has one it can hook — creating a second window
/// class here to avoid asking would be more code for the same result.
/// </para>
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    /// <summary>
    /// The message the shell sends when the icon is clicked.
    /// </summary>
    /// <remarks>
    /// In the range reserved for an application's own messages, so it cannot collide with
    /// anything the framework sends to the same window.
    /// </remarks>
    public const int CallbackMessage = 0x0400 + 0x21;

    private const int LeftButtonUp = 0x0202;
    private const int LeftButtonDoubleClick = 0x0203;
    private const int RightButtonUp = 0x0205;

    /// <summary>Distinguishes this icon from any other this process might add.</summary>
    private const uint IconId = 1;

    private readonly nint _window;
    private NotifyIconData _data;
    private bool _added;

    /// <summary>Adds the icon.</summary>
    /// <param name="window">A window of this process's, to be sent the clicks.</param>
    /// <param name="icon">The icon to draw, or zero for none.</param>
    /// <param name="tooltip">What hovering over it says.</param>
    /// <exception cref="GameProcessException">The shell refused it.</exception>
    public unsafe TrayIcon(nint window, nint icon, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        _window = window;
        _data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = window,
            Id = IconId,
            Flags = Shell32.FlagMessage | Shell32.FlagIcon | Shell32.FlagTip,
            CallbackMessage = CallbackMessage,
            Icon = icon,
        };

        Write(tooltip);

        if (!Shell32.NotifyIcon(Shell32.Add, ref _data))
        {
            throw new GameProcessException(
                "Shell_NotifyIcon(NIM_ADD) failed", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        _added = true;
    }

    /// <summary>Changes what hovering over the icon says.</summary>
    /// <remarks>
    /// Failure is ignored. The tooltip says which server is running; not being able to
    /// change it is not worth interrupting a running game over, and the icon itself is
    /// still there.
    /// </remarks>
    public void Update(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        if (!_added)
        {
            return;
        }

        Write(tooltip);
        Shell32.NotifyIcon(Shell32.Modify, ref _data);
    }

    /// <summary>
    /// Which of the clicks this cares about a message was, if any.
    /// </summary>
    /// <param name="message">The message the window received.</param>
    /// <param name="detail">Its <c>lParam</c>, which carries the mouse message.</param>
    /// <remarks>
    /// A double click opens as well as a single one. The icon has no second meaning for
    /// it, and a player who double-clicks everything should not be the one player for whom
    /// the launcher does not come back.
    /// </remarks>
    public static TrayAction Decode(int message, nint detail)
    {
        if (message != CallbackMessage)
        {
            return TrayAction.None;
        }

        return (int)detail switch
        {
            LeftButtonUp or LeftButtonDoubleClick => TrayAction.Open,
            RightButtonUp => TrayAction.Menu,
            _ => TrayAction.None,
        };
    }

    /// <summary>
    /// The first icon inside an executable, or zero if it has none.
    /// </summary>
    /// <remarks>
    /// The caller owns what comes back and should destroy it. In practice the one caller
    /// holds it for the life of the process, which is why nothing here wraps it in a
    /// handle type that would promise otherwise.
    /// </remarks>
    public static unsafe nint IconOf(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        nint small = 0;

        // Large and small are asked for separately and the small one is what the
        // notification area draws. Passing null for the large one is what tells the shell
        // not to load artwork nothing is going to use.
        return Shell32.ExtractIconEx(executablePath, 0, null, &small, 1) == 0 ? 0 : small;
    }

    /// <summary>Gives an icon back.</summary>
    /// <remarks>
    /// Separate from <see cref="Dispose"/> because the icon outlives the notification: the
    /// same artwork is reused if the launcher withdraws again, and destroying it with the
    /// notification would mean loading it from the executable each time.
    /// </remarks>
    public static void Destroy(nint icon)
    {
        if (icon != 0)
        {
            User32.DestroyIcon(icon);
        }
    }

    /// <summary>Removes the icon.</summary>
    /// <remarks>
    /// An icon left behind stays in the notification area until the shell notices the
    /// process has gone, which can be until the pointer next passes over it.
    /// </remarks>
    public void Dispose()
    {
        if (!_added)
        {
            return;
        }

        _added = false;
        Shell32.NotifyIcon(Shell32.Delete, ref _data);
    }

    /// <summary>Copies the tooltip into the structure's own buffer.</summary>
    /// <remarks>
    /// Cut to fit rather than refused, and always terminated. The buffer is a fixed 128
    /// characters and a string that filled it would leave the shell reading past the end.
    /// </remarks>
    private unsafe void Write(string tooltip)
    {
        fixed (char* tip = _data.Tip)
        {
            var room = 127;
            var length = Math.Min(tooltip.Length, room);

            tooltip.AsSpan(0, length).CopyTo(new Span<char>(tip, room));
            tip[length] = '\0';
        }
    }
}
