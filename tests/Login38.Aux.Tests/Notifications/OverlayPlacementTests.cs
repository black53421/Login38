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
        OverlayPlacement.Where(State()).ShouldBe(Picture);

    // A toast left hanging over the desktop is worse than no toast.
    [Fact]
    public void DrawsNothingOverAGameThatHasBeenPutAway() =>
        OverlayPlacement.Where(State() with { Minimised = true }).ShouldBeNull();

    // Nor over whatever the player alt-tabbed to.
    [Fact]
    public void DrawsNothingWhileThePlayerIsUsingSomethingElse() =>
        OverlayPlacement.Where(State() with { Foreground = false }).ShouldBeNull();

    [Fact]
    public void DrawsNothingOverAWindowThatIsNotOnScreen() =>
        OverlayPlacement.Where(State() with { Visible = false }).ShouldBeNull();

    [Fact]
    public void DrawsNothingWhereTheGameWouldNotSayWhereItIs() =>
        OverlayPlacement.Where(State() with { Client = null }).ShouldBeNull();

    // A window part way through being created reports a client area of nothing, and
    // stretching a picture over it divides by zero somewhere further down.
    [Theory]
    [InlineData(0, 768)]
    [InlineData(1024, 0)]
    [InlineData(-1, -1)]
    public void DrawsNothingOverAPictureWithNoArea(int width, int height) =>
        OverlayPlacement.Where(State() with { Client = new ScreenArea(0, 0, width, height) })
            .ShouldBeNull();

    private static OverlayPlacement.WindowState State() =>
        new(Visible: true, Minimised: false, Foreground: true, Picture);
}
