using Login38.Aux.Notifications;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers where the toasts and the numbers sit, and how they fade.
/// </summary>
public sealed class NotificationLayoutTests
{
    private const int Wide = 1200;
    private const int High = 900;

    private static readonly TimeSpan Now = TimeSpan.FromMinutes(3);

    [Fact]
    public void StacksTheToastsUpwardsFromTheAnchor()
    {
        var first = NotificationLayout.Toast(Now, 0, Wide, High, Now);
        var second = NotificationLayout.Toast(Now, 1, Wide, High, Now);

        first.Y.ShouldBe((High * 71 / 100) - (NotificationLayout.ToastHeight + NotificationLayout.ToastGap));
        (first.Y - second.Y).ShouldBe(NotificationLayout.ToastHeight + NotificationLayout.ToastGap);
    }

    // Nothing sits on the anchor itself: the first row is a whole row above it, so the
    // stack grows away from the client's chat panel rather than into it.
    [Fact]
    public void LeavesTheAnchorLineClear() =>
        NotificationLayout.Toast(Now, 0, Wide, High, Now).Y
            .ShouldBeLessThan(High * 71 / 100);

    // Proportions rather than pixels, so the same numbers land in the same place whatever
    // the player runs the client at.
    [Theory]
    [InlineData(800, 600)]
    [InlineData(1024, 768)]
    [InlineData(1200, 900)]
    [InlineData(1920, 1080)]
    public void SitsInTheSamePlaceAtEverySizeTheGameIsPlayedAt(int width, int height)
    {
        var toast = NotificationLayout.Toast(Now, 0, width, height, Now);
        var anchor = toast.Y + NotificationLayout.ToastHeight + NotificationLayout.ToastGap;

        // Within a pixel of the proportion, which is all integer arithmetic can promise.
        (toast.X - (width * 12.0 / 1000)).ShouldBeInRange(-1, 1);
        (anchor - (height * 0.71)).ShouldBeInRange(-1, 1);
    }

    [Fact]
    public void StaysOnScreenWithAFullStackAtTheSmallestSizeTheClientRuns() =>
        NotificationLayout.Toast(Now, NotificationBoard.MostToasts - 1, 800, 600, Now)
            .Y.ShouldBeGreaterThan(0);

    [Fact]
    public void HasRoomForTheWholeRowAcrossTheNarrowestPicture() =>
        (NotificationLayout.ToastAnchorX(800) + NotificationLayout.ToastWidth).ShouldBeLessThan(800);

    // The name goes to the right of the item frame, which is as tall as the row.
    [Fact]
    public void LeavesTheItemFrameClearForTheName()
    {
        NotificationLayout.NameLeft.ShouldBeGreaterThan(NotificationLayout.ToastHeight);
        NotificationLayout.NameHeight.ShouldBeLessThan(NotificationLayout.ToastHeight);
    }

    // ---- the numbers -----------------------------------------------------------------

    [Fact]
    public void PutsTheNumbersRightOfCentreAndAboveIt()
    {
        var drift = NotificationLayout.Drift(Now, DriftKind.Experience, Wide, High, Now);

        drift.X.ShouldBe(Wide * 62 / 100);
        drift.X.ShouldBeGreaterThan(Wide / 2);
        drift.Y.ShouldBe((High * 41 / 100) + NotificationLayout.ExperienceOffset);
        drift.Y.ShouldBeLessThan(High / 2);
    }

    [Fact]
    public void PutsCoinUnderExperienceSoTheyDoNotOverlap()
    {
        var experience = NotificationLayout.Drift(Now, DriftKind.Experience, Wide, High, Now);
        var gold = NotificationLayout.Drift(Now, DriftKind.Gold, Wide, High, Now);

        gold.Y.ShouldBeGreaterThan(experience.Y);
        (gold.Y - experience.Y)
            .ShouldBe(NotificationLayout.GoldOffset - NotificationLayout.ExperienceOffset);
    }

    [Fact]
    public void RaisesANumberOverItsLife()
    {
        var start = NotificationLayout.Drift(Now, DriftKind.Gold, Wide, High, Now);
        var half = NotificationLayout.Drift(
            Now, DriftKind.Gold, Wide, High, Now + (NotificationBoard.DriftLife / 2));

        (start.Y - half.Y).ShouldBe(NotificationLayout.DriftRise / 2);
    }

    [Fact]
    public void HasRisenAllOfItByTheEnd() =>
        NotificationLayout.Drift(Now, DriftKind.Gold, Wide, High, Now + NotificationBoard.DriftLife)
            .Y.ShouldBe((High * 41 / 100) + NotificationLayout.GoldOffset - NotificationLayout.DriftRise);

    // A frame drawn late must not ask for a position above the top of the rise or an alpha
    // below zero.
    [Fact]
    public void StaysPutOnceItsTimeIsUp()
    {
        var late = NotificationLayout.Drift(
            Now, DriftKind.Gold, Wide, High, Now + NotificationBoard.DriftLife + TimeSpan.FromSeconds(10));

        late.Y.ShouldBe((High * 41 / 100) + NotificationLayout.GoldOffset - NotificationLayout.DriftRise);
        late.Alpha.ShouldBe((byte)0);
    }

    // ---- fading ----------------------------------------------------------------------

    [Fact]
    public void IsSolidUntilItStartsToGo()
    {
        NotificationLayout.Fade(TimeSpan.Zero, NotificationLayout.ToastSolid, NotificationBoard.ToastLife)
            .ShouldBe(byte.MaxValue);
        NotificationLayout.Fade(
                NotificationLayout.ToastSolid - TimeSpan.FromMilliseconds(1),
                NotificationLayout.ToastSolid, NotificationBoard.ToastLife)
            .ShouldBe(byte.MaxValue);
    }

    [Fact]
    public void IsHalfwayGoneHalfwayThroughTheFade()
    {
        var solid = NotificationLayout.ToastSolid;
        var life = NotificationBoard.ToastLife;

        NotificationLayout.Fade(solid + ((life - solid) / 2), solid, life)
            .ShouldBeInRange((byte)126, (byte)129);
    }

    [Fact]
    public void IsGoneWhenItsTimeIsUp() =>
        NotificationLayout.Fade(
                NotificationBoard.ToastLife, NotificationLayout.ToastSolid, NotificationBoard.ToastLife)
            .ShouldBe((byte)0);

    [Fact]
    public void NeverGoesBelowNothingHoweverLateTheFrameIs() =>
        NotificationLayout.Fade(
                TimeSpan.FromHours(1), NotificationLayout.ToastSolid, NotificationBoard.ToastLife)
            .ShouldBe((byte)0);

    // Half a second for a toast and three tenths for a number, both measured back from
    // when the board takes them down.
    [Fact]
    public void StartsFadingAFixedTimeBeforeItIsTakenDown()
    {
        (NotificationBoard.ToastLife - NotificationLayout.ToastSolid)
            .ShouldBe(TimeSpan.FromMilliseconds(500));
        (NotificationBoard.DriftLife - NotificationLayout.DriftSolid)
            .ShouldBe(TimeSpan.FromMilliseconds(300));
    }
}
