using Login38.Interop;

namespace Login38.Aux.Notifications;

/// <summary>
/// Whether anything should be drawn over the game, and where.
/// </summary>
/// <remarks>
/// Pulled out of the drawing so it can be decided without a window to look at. Every one of
/// these conditions is a case somebody has to have watched happen once — a toast left
/// hanging over the desktop after the game was minimised, or floating above another
/// application the player alt-tabbed to.
/// </remarks>
public static class OverlayPlacement
{
    /// <summary>What the game's window is currently doing.</summary>
    /// <param name="Visible">Whether it is on screen at all.</param>
    /// <param name="Minimised">Whether it has been put away.</param>
    /// <param name="Foreground">Whether the player is looking at it.</param>
    /// <param name="Client">Where its picture is on the desktop.</param>
    public readonly record struct WindowState(
        bool Visible, bool Minimised, bool Foreground, ScreenArea? Client);

    /// <summary>
    /// Where to put the overlay, or null for "nowhere".
    /// </summary>
    /// <remarks>
    /// A minimised window still has a client rectangle, and it is somewhere off the bottom
    /// of the desktop — so "is it minimised" has to be asked separately rather than inferred
    /// from where the game says it is.
    /// </remarks>
    public static ScreenArea? Where(WindowState window) =>
        window is { Visible: true, Minimised: false, Foreground: true, Client: { IsEmpty: false } client }
            ? client
            : null;
}
