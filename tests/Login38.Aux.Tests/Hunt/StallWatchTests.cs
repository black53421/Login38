using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers noticing that a target has stopped being worth chasing.
/// </summary>
/// <remarks>
/// This is what stands in for the thing the collision grid cannot see. Bit 0 of a cell is
/// map geometry — nothing dynamic occupies a cell, which was watched over a hundred and
/// nine of them — so neither the hunt nor the client's own walk engine can tell that
/// another creature is standing in the way. What cannot be predicted has to be noticed.
/// </remarks>
public sealed class StallWatchTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(6);

    [Fact]
    public void SaysNothingOnTheFirstReading() =>
        new StallWatch().Stalled(10, 10, 100, TimeSpan.Zero, Limit).ShouldBeFalse();

    [Fact]
    public void GivesUpWhenNothingChangesForLongEnough()
    {
        var watch = new StallWatch();

        watch.Stalled(10, 10, 100, TimeSpan.Zero, Limit).ShouldBeFalse();
        watch.Stalled(10, 10, 100, TimeSpan.FromSeconds(5), Limit).ShouldBeFalse();
        watch.Stalled(10, 10, 100, TimeSpan.FromSeconds(6), Limit).ShouldBeTrue();
    }

    [Fact]
    public void CountsWalkingAsProgress()
    {
        var watch = new StallWatch();

        watch.Stalled(10, 10, 100, TimeSpan.Zero, Limit);
        watch.Stalled(11, 10, 100, TimeSpan.FromSeconds(5), Limit).ShouldBeFalse();
        watch.Stalled(11, 10, 100, TimeSpan.FromSeconds(9), Limit).ShouldBeFalse();
    }

    // The reason the cooldown is read at all. A character standing next to a monster
    // hitting it does not move, so a watch that only looked at position would give up on
    // exactly the fight that was going well.
    [Fact]
    public void CountsLandingABlowAsProgressWithoutMoving()
    {
        var watch = new StallWatch();

        watch.Stalled(10, 10, 100, TimeSpan.Zero, Limit);
        watch.Stalled(10, 10, 140, TimeSpan.FromSeconds(5), Limit).ShouldBeFalse();
        watch.Stalled(10, 10, 180, TimeSpan.FromSeconds(9), Limit).ShouldBeFalse();
        watch.Stalled(10, 10, 220, TimeSpan.FromSeconds(13), Limit).ShouldBeFalse();
    }

    // Neither moving nor hitting: walked into something the grid does not know about.
    [Fact]
    public void GivesUpWhenItIsNeitherWalkingNorHitting()
    {
        var watch = new StallWatch();

        watch.Stalled(10, 10, 100, TimeSpan.Zero, Limit);
        watch.Stalled(10, 10, 100, TimeSpan.FromSeconds(7), Limit).ShouldBeTrue();
    }

    // Two callers read one reading at two lengths: a second of quiet means the walk engine
    // has stopped and is worth restarting, six means the target is not worth more time.
    // Asking twice must not move the baseline, or the second question always answers zero.
    [Fact]
    public void AnswersTwoLengthsFromOneReading()
    {
        var watch = new StallWatch();

        watch.Idle(10, 10, 100, TimeSpan.Zero);

        watch.Idle(10, 10, 100, TimeSpan.FromSeconds(2)).ShouldBe(TimeSpan.FromSeconds(2));
        watch.Idle(10, 10, 100, TimeSpan.FromSeconds(2)).ShouldBe(TimeSpan.FromSeconds(2));
        watch.Stalled(10, 10, 100, TimeSpan.FromSeconds(2), Limit).ShouldBeFalse();
        watch.Idle(10, 10, 100, TimeSpan.FromSeconds(2)).ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void IsIdleForNoTimeAtAllWhenSomethingHappened()
    {
        var watch = new StallWatch();

        watch.Idle(10, 10, 100, TimeSpan.Zero);
        watch.Idle(10, 10, 100, TimeSpan.FromSeconds(3)).ShouldBe(TimeSpan.FromSeconds(3));
        watch.Idle(11, 10, 100, TimeSpan.FromSeconds(4)).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void StartsAgainForANewTarget()
    {
        var watch = new StallWatch();

        watch.Stalled(10, 10, 100, TimeSpan.Zero, Limit);
        watch.Reset();
        watch.Stalled(10, 10, 100, TimeSpan.FromSeconds(30), Limit).ShouldBeFalse();
    }
}
