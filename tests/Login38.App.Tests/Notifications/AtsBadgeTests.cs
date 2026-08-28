using Login38.App.Notifications;
using Login38.TestSupport;
using Shouldly;

namespace Login38.App.Tests.Notifications;

/// <summary>
/// Covers the classic client's auto-hunt mark being there at all.
/// </summary>
/// <remarks>
/// One thing, and it is the one that breaks silently: the sprites are resources inside the
/// launcher's own assembly, named by a pack URI, and every way of getting that wrong ends in
/// a mark that is simply not drawn. Nothing else in the drawing path would notice — the
/// overlay would sit over the game with nothing in the middle of it, which reads as the hunt
/// being off.
/// </remarks>
public sealed class AtsBadgeTests
{
    [Fact]
    public void FindsBothOfTheSpritesItDrawsWith() =>
        WindowLayout.OnUi(() => new AtsBadge().IsReady).ShouldBeTrue();
}
