using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers where a character with a bow decides to stand.
/// </summary>
/// <remarks>
/// <para>
/// The client stops the instant it is in range at all. <c>ComputeStepHeading</c> at
/// <c>0x5A4D60</c> takes the weapon's entry out of <c>DAT_008D2CB8</c> — 2 tiles for melee,
/// 9 for a claw, 14 for a bow — and treats the first square inside it as arrival, so a bow
/// left alone fights from fourteen tiles. That is the worst distance there is: the monster
/// takes one step, the shot is out of range, the walk starts again, and the whole fight is
/// spent drifting in and out instead of shooting.
/// </para>
/// <para>
/// There is one knob and only one. The arrival test reads
/// <see cref="HuntAddresses.AttackReach"/> and prefers it over the table whenever it is not
/// zero, so standing closer means telling the client a smaller reach and letting its own
/// walk engine close the gap — which is what makes a bow behave like a melee weapon rather
/// than a stutter.
/// </para>
/// <para>
/// Reading and writing both need a running client, so what a test can hold is the rule
/// between the two numbers. That rule has to cap and never extend, or the character walks
/// off to a distance the weapon cannot cover; and it has to leave melee alone entirely.
/// </para>
/// </remarks>
public sealed class WeaponReachTests
{
    [Theory]
    [InlineData(14, 8, 8)]                                   // bow, told to close in
    [InlineData(9, 8, 8)]                                    // claw, likewise
    [InlineData(30, 8, 8)]                                   // classes 16-19
    public void StandsWhereItWasToldWhenTheWeaponReachesFurther(
        int table, int standoff, int expected) =>
        WeaponReach.Stand(table, standoff).ShouldBe(expected);

    // The cap that keeps melee out of this. Its entry is 2, already nearer than any sane
    // standoff, and walking further away to shoot with a sword is not a thing.
    [Theory]
    [InlineData(2, 8)]                                       // melee, classes 24-27
    [InlineData(9, 12)]                                      // claw under a wide standoff
    [InlineData(14, 14)]                                     // exactly the weapon's own reach
    public void NeverStandsFurtherOffThanTheWeaponReaches(int table, int standoff) =>
        WeaponReach.Stand(table, standoff).ShouldBe(table);

    // Nobody has chosen one. The weapon's own reach is the right answer to that; zero would
    // be a character walking into what it is shooting at.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FallsBackToTheWeaponWhenNoStandoffWasSet(int standoff) =>
        WeaponReach.Stand(14, standoff).ShouldBe(14);

    // Zero is not "no reach" to the client, it is "nobody has told me" — see
    // ComputeStepHeading. So a weapon the standoff does not reach into is left with no
    // override at all rather than one that agrees with the table, and nothing is written.
    [Theory]
    [InlineData(2, 8)]
    [InlineData(14, 14)]
    [InlineData(14, 0)]
    public void WritesNothingWhenTheClientAlreadyStandsThere(int table, int standoff) =>
        WeaponReach.Override(table, standoff).ShouldBe((byte)0);

    [Theory]
    [InlineData(14, 8, 8)]
    [InlineData(30, 3, 3)]
    [InlineData(9, 4, 4)]
    public void WritesTheStandoffWhenItActuallyMovesTheCharacter(
        int table, int standoff, int expected) =>
        WeaponReach.Override(table, standoff).ShouldBe((byte)expected);

    // What to do about a target being swung at and not hurt. Neither cause can be predicted
    // — the client's collision grid holds walkability and nothing else, and its map files
    // hold graphics — but both are answered by walking closer: a corner stops being a corner
    // once you are round it, and a burrowed monster surfaces when somebody comes within two
    // tiles of it.
    [Theory]
    [InlineData(14, 7)]                                      // a bow, left to itself
    [InlineData(8, 4)]                                       // the usual standoff
    [InlineData(4, 2)]
    [InlineData(3, 1)]
    [InlineData(2, 1)]                                       // melee, and the last step
    public void HalvesTheDistanceRatherThanStepping(int reach, int expected) =>
        WeaponReach.Closer(reach).ShouldBe(expected);

    // There is nothing nearer than the next square, so a target that cannot be hurt from
    // there cannot be hurt, and the hunt has to be told to stop rather than to try again.
    [Fact]
    public void RunsOutOfRoomAtTheNextSquare() =>
        WeaponReach.Closer(1).ShouldBeNull();

    // Three steps from a bow's own reach to arm's length. Stepping down one tile at a time
    // would take thirteen, at five seconds each.
    [Fact]
    public void GetsFromABowsReachToArmsLengthInThreeTries()
    {
        var steps = 0;

        for (int? reach = 14; WeaponReach.Closer(reach!.Value) is { } nearer; reach = nearer)
        {
            steps++;
        }

        steps.ShouldBe(3);
    }

    [Fact]
    public void HasTheSameDefaultTheClientHas() =>
        WeaponReach.Default.ShouldBe(1);
}
