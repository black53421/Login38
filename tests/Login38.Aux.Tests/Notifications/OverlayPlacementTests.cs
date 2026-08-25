using Login38.Aux.Notifications;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers whether anything should be drawn over the game, and where.
/// </summary>
public sealed class OverlayPlacementTests
{
    private static readonly ScreenArea Picture = new(100, 80, 1024, 768);

    [Fact]
    public void DrawsOverAGameThePlayerIsLookingAt() =>
        Where(State()).ShouldBe(Picture);

    // A toast left hanging over the desktop is worse than no toast.
    [Fact]
    public void DrawsNothingOverAGameThatHasBeenPutAway() =>
        Where(State() with { Minimised = true }).ShouldBeNull();

    // Nor over whatever the player alt-tabbed to.
    [Fact]
    public void DrawsNothingWhileThePlayerIsUsingSomethingElse() =>
        Where(State() with { Foreground = false }).ShouldBeNull();

    [Fact]
    public void DrawsNothingOverAWindowThatIsNotOnScreen() =>
        Where(State() with { Visible = false }).ShouldBeNull();

    [Fact]
    public void DrawsNothingWhereTheGameWouldNotSayWhereItIs() =>
        Where(State() with { Client = null }).ShouldBeNull();

    // A window part way through being created reports a client area of nothing, and
    // stretching a picture over it divides by zero somewhere further down.
    [Theory]
    [InlineData(0, 768)]
    [InlineData(1024, 0)]
    [InlineData(-1, -1)]
    public void DrawsNothingOverAPictureWithNoArea(int width, int height) =>
        Where(State() with { Client = new ScreenArea(0, 0, width, height) })
            .ShouldBeNull();

    // A toast is waiting in all of these; what they are about is the window.
    private static ScreenArea? Where(OverlayPlacement.WindowState window) =>
        OverlayPlacement.Where(window, anythingToShow: true);

    // The launcher had a layered, always-on-top window over the client's picture for the
    // whole session, redrawn ten times a second, showing nothing at all most of it -- over
    // the login screen, over character select, and over the client's own farewell screen
    // while it was giving its display back. A toast lasts a few seconds.
    [Fact]
    public void DrawsNothingWhenThereIsNothingToShow() =>
        OverlayPlacement.Where(State(), anythingToShow: false).ShouldBeNull();

    [Fact]
    public void DrawsWhenThereIsSomethingToShow() =>
        OverlayPlacement.Where(State(), anythingToShow: true).ShouldBe(Picture);

    private static OverlayPlacement.WindowState State() =>
        new(Visible: true, Minimised: false, Foreground: true, Picture);
}
