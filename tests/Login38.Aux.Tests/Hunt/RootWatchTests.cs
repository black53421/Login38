using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers the watch that notices a character has stopped getting anywhere.
/// </summary>
/// <remarks>
/// This is the last thing between the hunt and a character that stands still for ever. The
/// server refuses an attack it will not allow by dropping the packet — no line of sight
/// round a corner, a monster still burrowed, out of range — and says nothing, so every other
/// signal the launcher has agrees the fight is going fine: the client keeps starting blows,
/// keeps pushing its own cooldown forward, keeps holding the target. Only the ground under
/// the character tells the truth.
/// </remarks>
public sealed class RootWatchTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [Fact]
    public void SaysNothingHasHappenedOnTheFirstReading() =>
        new RootWatch().Rooted(100, 100, 2, Second).ShouldBe(TimeSpan.Zero);

    [Fact]
    public void CountsFromTheFirstReadingWhileTheCharacterStaysPut()
    {
        var watch = new RootWatch();

        watch.Rooted(100, 100, 2, Second);

        watch.Rooted(100, 100, 2, Second * 21).ShouldBe(Second * 20);
    }

    // The whole reason for the slack. A character wedged against a wall shuffles, and an
    // exact test is beaten by one tile of it — which is precisely the case this exists for.
    [Fact]
    public void IsNotFooledByShuffling()
    {
        var watch = new RootWatch();

        watch.Rooted(100, 100, 2, Second);
        watch.Rooted(102, 101, 2, Second * 10);

        watch.Rooted(101, 100, 2, Second * 21).ShouldBe(Second * 20);
    }

    [Fact]
    public void StartsAgainWhenTheCharacterActuallyGetsSomewhere()
    {
        var watch = new RootWatch();

        watch.Rooted(100, 100, 2, Second);

        watch.Rooted(100, 108, 2, Second * 10).ShouldBe(TimeSpan.Zero);
        watch.Rooted(100, 108, 2, Second * 12).ShouldBe(Second * 2);
    }

    // Twice as wide as it is tall, because two columns make a tile across and one row makes
    // one down. Measuring in squares would give half the slack sideways as up.
    [Fact]
    public void MeasuresSlackTheWayTheClientMeasuresTheGrid()
    {
        var watch = new RootWatch();

        watch.Rooted(100, 100, 2, Second);
        watch.Rooted(104, 100, 2, Second * 5).ShouldBe(Second * 4);   // four columns across

        watch.Rooted(100, 104, 2, Second * 9).ShouldBe(TimeSpan.Zero); // four rows down
    }

    [Fact]
    public void StartsAgainWhenReset()
    {
        var watch = new RootWatch();

        watch.Rooted(100, 100, 2, Second);
        watch.Reset();

        watch.Rooted(100, 100, 2, Second * 30).ShouldBe(TimeSpan.Zero);
    }
}
