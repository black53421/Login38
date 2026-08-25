using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>
/// Makes a window of this launcher's own something the player cannot interact with.
/// </summary>
/// <remarks>
/// For anything drawn over the game. A window sitting on top of a client that is being
/// played must not take a click, must not take the focus, and must not appear in the task
/// bar or the alt-tab list — a player who tabs into an invisible window and finds their
/// keys going nowhere has no way of working out what happened.
/// </remarks>
public static class OverlayWindow
{
    /// <summary>Where the extended style bits live.</summary>
    private const int ExtendedStyle = -20;

    /// <summary>Clicks fall through to whatever is behind.</summary>
    private const int Transparent = 0x0000_0020;

    /// <summary>Out of the task bar and the alt-tab list.</summary>
    private const int ToolWindow = 0x0000_0080;

    /// <summary>Never takes the focus, however it is clicked.</summary>
    private const int NoActivate = 0x0800_0000;

    /// <summary>Applies all three to a window this launcher owns.</summary>
    public static void MakeUntouchable(nint window)
    {
        if (window == 0)
        {
            return;
        }

        var style = User32.GetWindowLong(window, ExtendedStyle);

        User32.SetWindowLong(window, ExtendedStyle, style | Transparent | ToolWindow | NoActivate);
    }
}
