using Login38.Aux.Notifications;
using Login38.Core.Text;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers what is on screen and for how long.
/// </summary>
public sealed class NotificationBoardTests
{
    private static readonly TimeSpan Now = TimeSpan.FromMinutes(3);

    [Fact]
    public void ShowsWhatWasPickedUp()
    {
        var board = Board();

        board.Push(new Notification.Toast(7, "劍"u8.ToArray()), Now);

        board.Toasts.Count.ShouldBe(1);
        board.Toasts[0].Sprite.ShouldBe((ushort)7);
        board.Toasts[0].SpawnedAt.ShouldBe(Now);
    }

    // Decoded once, on the way in. The reference decodes it on every frame of the five
    // seconds a toast is up, through the code-page heuristic, for a string that cannot
    // change.
    [Fact]
    public void ReadsTheNameOutOfTheClientsCodePageOnTheWayIn()
    {
        var board = Board();

        board.Push(new Notification.Toast(1, LegacyTextCodec.Auto.Encode("寶劍", LegacyEncoding.Big5)), Now);

        board.Toasts[0].Name.ShouldBe("寶劍");
    }

    // A player who has just emptied a chest wants to see what came out last.
    [Fact]
    public void DropsTheOldestOnceTheStackIsFull()
    {
        var board = Board();

        for (var i = 0; i < NotificationBoard.MostToasts + 5; i++)
        {
            board.Push(new Notification.Toast((ushort)i, []), Now);
        }

        board.Toasts.Count.ShouldBe(NotificationBoard.MostToasts);
        board.Toasts[0].Sprite.ShouldBe((ushort)5);
        board.Toasts[^1].Sprite.ShouldBe((ushort)(NotificationBoard.MostToasts + 4));
    }

    [Fact]
    public void ShowsWhatTheKillWasWorth()
    {
        var board = Board();

        board.Push(new Notification.Drift(DriftKind.Experience, 100), Now);

        board.Drifts.Count.ShouldBe(1);
        board.Drifts[0].Amount.ShouldBe(100u);
    }

    // Killing quickly would otherwise stack a column of numbers up the screen. What the
    // player wants to know is what the run was worth.
    [Fact]
    public void AddsUpRatherThanStackingUp()
    {
        var board = Board();

        for (var i = 0; i < 200; i++)
        {
            board.Push(new Notification.Drift(DriftKind.Experience, 5), Now);
        }

        board.Drifts.Count.ShouldBe(1);
        board.Drifts[0].Amount.ShouldBe(1000u);
    }

    [Fact]
    public void KeepsExperienceAndCoinApart()
    {
        var board = Board();

        board.Push(new Notification.Drift(DriftKind.Experience, 100), Now);
        board.Push(new Notification.Drift(DriftKind.Gold, 50), Now);

        board.Drifts.Count.ShouldBe(2);
    }

    // A total that is still growing must not fade halfway through.
    [Fact]
    public void PutsTheClockBackOnATotalThatIsStillGrowing()
    {
        var board = Board();
        var later = Now + TimeSpan.FromMilliseconds(800);

        board.Push(new Notification.Drift(DriftKind.Experience, 10), Now);
        board.Push(new Notification.Drift(DriftKind.Experience, 20), later);

        board.Drifts[0].Amount.ShouldBe(30u);
        board.Drifts[0].SpawnedAt.ShouldBe(later);
    }

    // A long session at a high level can pass four billion experience in one sitting; the
    // reference saturates here too, and a number that wrapped to nearly nothing would read
    // as a loss.
    [Fact]
    public void SaturatesRatherThanWrappingOnATotalThatCannotFit()
    {
        var board = Board();

        board.Push(new Notification.Drift(DriftKind.Gold, uint.MaxValue - 1), Now);
        board.Push(new Notification.Drift(DriftKind.Gold, 100), Now);

        board.Drifts[0].Amount.ShouldBe(uint.MaxValue);
    }

    [Fact]
    public void TakesAToastDownWhenItsTimeIsUp()
    {
        var board = Board();

        board.Push(new Notification.Toast(1, []), Now);
        board.Tick(Now + NotificationBoard.ToastLife);

        board.Toasts.ShouldBeEmpty();
    }

    [Fact]
    public void LeavesOneThatStillHasTimeLeft()
    {
        var board = Board();

        board.Push(new Notification.Toast(1, []), Now);
        board.Tick(Now + NotificationBoard.ToastLife - TimeSpan.FromMilliseconds(1));

        board.Toasts.Count.ShouldBe(1);
    }

    [Fact]
    public void TakesANumberDownSoonerThanAToast()
    {
        var board = Board();

        board.Push(new Notification.Toast(1, []), Now);
        board.Push(new Notification.Drift(DriftKind.Gold, 1), Now);
        board.Tick(Now + NotificationBoard.DriftLife);

        board.Drifts.ShouldBeEmpty();
        board.Toasts.Count.ShouldBe(1);
    }

    [Fact]
    public void EmptiesForACharacterLeavingTheWorld()
    {
        var board = Board();

        board.Push(new Notification.Toast(1, []), Now);
        board.Push(new Notification.Drift(DriftKind.Gold, 1), Now);
        board.Clear();

        board.Toasts.ShouldBeEmpty();
        board.Drifts.ShouldBeEmpty();
    }

    private static NotificationBoard Board() => new(LegacyTextCodec.Auto);
}
