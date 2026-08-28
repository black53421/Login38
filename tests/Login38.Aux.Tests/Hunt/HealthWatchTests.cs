using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers telling a fight from a character swinging at something it cannot hit.
/// </summary>
/// <remarks>
/// <para>
/// The server drops an attack it will not allow and says nothing about it — no line of sight
/// round a corner, a monster still burrowed. From inside the client all of those look like a
/// fight going well, because the client starts the blow, plays the animation and moves its
/// cooldown on either way. The health bar is the server's own answer and the only one there
/// is.
/// </para>
/// <para>
/// The property that matters most here is the one about ordinary fights. This must not be a
/// timer on how long something takes to die: a tough monster is still losing health while it
/// takes its time, and every point of it has to start the clock again. Only a target that
/// cannot be hurt at all holds one number.
/// </para>
/// </remarks>
public sealed class HealthWatchTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private const uint Monster = 200_015_269;

    [Fact]
    public void SaysNothingHasHappenedOnTheFirstReading() =>
        new HealthWatch().Unchanged(Monster, 100, Second).ShouldBe(TimeSpan.Zero);

    [Fact]
    public void CountsFromTheReadingThatLastChanged()
    {
        var watch = new HealthWatch();

        watch.Unchanged(Monster, 100, Second);

        watch.Unchanged(Monster, 100, Second * 6).ShouldBe(Second * 5);
    }

    // The one that keeps this off an ordinary fight. A monster losing a point every few
    // seconds is a monster being killed, however slowly.
    [Fact]
    public void StartsAgainOnEveryPointLost()
    {
        var watch = new HealthWatch();

        watch.Unchanged(Monster, 100, Second);
        watch.Unchanged(Monster, 100, Second * 4).ShouldBe(Second * 3);

        watch.Unchanged(Monster, 99, Second * 5).ShouldBe(TimeSpan.Zero);
        watch.Unchanged(Monster, 99, Second * 8).ShouldBe(Second * 3);
    }

    // The value a monster carries before the server has ever sent a health bar for it, which
    // is to say before anything has ever hurt it. It is a value like any other here: what
    // matters is that it stops changing, not what it is.
    [Fact]
    public void TreatsNeverHavingBeenToldTheSameWay()
    {
        var watch = new HealthWatch();

        watch.Unchanged(Monster, HuntAddresses.HealthUnknown, Second);

        watch.Unchanged(Monster, HuntAddresses.HealthUnknown, Second * 6).ShouldBe(Second * 5);
    }

    // Leaving the unknown value is the first blow that landed, and it has to count as
    // progress or every fight would be given up five seconds after it started.
    [Fact]
    public void CountsTheFirstBlowThatLands()
    {
        var watch = new HealthWatch();

        watch.Unchanged(Monster, HuntAddresses.HealthUnknown, Second);
        watch.Unchanged(Monster, HuntAddresses.HealthUnknown, Second * 4);

        watch.Unchanged(Monster, 79, Second * 5).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void StartsAgainForADifferentMonster()
    {
        var watch = new HealthWatch();

        watch.Unchanged(Monster, 100, Second);

        watch.Unchanged(Monster + 1, 100, Second * 9).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void StartsAgainWhenReset()
    {
        var watch = new HealthWatch();

        watch.Unchanged(Monster, 100, Second);
        watch.Reset();

        watch.Unchanged(Monster, 100, Second * 30).ShouldBe(TimeSpan.Zero);
    }
}
