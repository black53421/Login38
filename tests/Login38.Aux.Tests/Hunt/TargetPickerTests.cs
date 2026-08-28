using Login38.Aux.Hunt;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers choosing what to attack next.
/// </summary>
/// <remarks>
/// <para>
/// Only half the choice is here now. Whether the character can get to a monster is asked of
/// the client — the routines its own walk engine decides a step with, run inside it — so
/// there is nothing left in this file that models walking, and nothing left that a test on
/// a made-up map could get wrong in the same direction the code did. What is testable is
/// what the settings allow, and the order the answers come in.
/// </para>
/// <para>
/// The rule is that what the walk did not reach is not picked, and the exception is that a
/// caller with nothing left may ask again without it. Both halves earned their place off a
/// live client: without the rule the hunt locks onto whatever is behind the nearest wall and
/// wedges against it, melee and ranged alike; without the exception it stands still in front
/// of nine monsters for ten seconds, because a character that picks nothing never moves and
/// the probe answers from where the character is standing.
/// </para>
/// </remarks>
public sealed class TargetPickerTests
{
    private static readonly HashSet<uint> Nothing = [];

    private static readonly (int X, int Y) Standing = (0, 0);

    /// <summary>Out of everything's way, so walkability alone decides.</summary>
    private const int Unarmed = 0;

    /// <summary>A bow's reach, for the cases about shooting something unreachable.</summary>
    private const int Bow = 14;

    [Fact]
    public void TakesTheOneReachedInFewestSteps() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "far", 8, 0), Monster(2, "near", 3, 0)],
            [12, 4], Standing, Unarmed)).ShouldBe(2u);

    // The whole point of asking the client. A monster three squares away in a straight line
    // that its walk never arrives at is not a nearer monster, it is not a monster at all.
    [Fact]
    public void PassesOverOneTheWalkNeverReached() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "behind a wall", 3, 0), Monster(2, "round the corner", 9, 0)],
            [null, 20], Standing, Unarmed)).ShouldBe(2u);

    // No nearest-anyway fallback on the ordinary path. Shooting at a wall is not a fight, and
    // a hunt that locks onto what is behind one wedges the character against it — melee and
    // ranged alike, which is what the one build this rule was relaxed for did.
    [Fact]
    public void TakesNothingWhenTheWalkReachedNothing() =>
        TargetPicker.Nearest([Monster(1, "boxed off", 3, 0)], [null], Standing, Unarmed)
            .ShouldBeNull();

    [Fact]
    public void TakesNothingFromAnEmptyScreen() =>
        TargetPicker.Nearest([], [], Standing, Unarmed).ShouldBeNull();

    // One run answers about a bounded number of monsters. The ones past the end were never
    // asked about, which is not the same as unreachable, but is the same as unpickable.
    [Fact]
    public void LeavesAloneWhatItNeverAskedAbout() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "asked", 5, 0), Monster(2, "unasked", 1, 0)],
            [7], Standing, Unarmed)).ShouldBe(1u);

    [Fact]
    public void ZeroStepsIsAnAnswerNotAMissingOne() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "far", 9, 0), Monster(2, "on top of us", 2, 0)], [30, 0], Standing,
            Unarmed)).ShouldBe(2u);

    // With a bow the walk stops as soon as it is close enough to shoot, so every monster
    // on the screen comes back at nought steps and the step count has nothing left to say.
    // Without a second question the pick would be whichever record the scan walked first,
    // which changes as the client reallocates and is not a choice at all.
    [Fact]
    public void BreaksATieOnHowFarAwayTheyActuallyAre() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "across the field", 20, 6), Monster(2, "just there", 4, 1)],
            [0, 0],
            Standing,
            Unarmed)).ShouldBe(2u);

    // Distance only settles ties. A monster the walk gets to sooner is still the better
    // target, however the straight line between the two measures.
    [Fact]
    public void StillTakesFewerStepsOverBeingNearer() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "near, but round a corner", 4, 0), Monster(2, "far and straight", 16, 0)],
            [9, 2],
            Standing,
            Unarmed)).ShouldBe(2u);

    // The one that stops a character being sent at a wall. Whether the walk gets there is
    // asked when the target is chosen, not found out afterwards — afterwards means the
    // client has already wedged itself against stone, because its climb has no lookahead.
    [Fact]
    public void PrefersOneItCanWalkToOverOneItCanOnlyShoot() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "behind a wall, in range", 2, 0), Monster(2, "round the long way", 6, 2)],
            [null, 18],
            Standing,
            Bow)).ShouldBe(2u);

    // Not even one it could shoot from where it stands. That exception was allowed for one
    // build and is the whole of what "it keeps locking onto things behind walls" was: with a
    // bow every monster within reach qualifies, walls included, so the exception swallowed
    // the rule.
    [Fact]
    public void TakesNothingWhenTheOnlyShotIsThroughAWall() =>
        TargetPicker.Nearest(
            [Monster(1, "behind a wall, in range", 2, 0)], [null], Standing, Bow)
            .ShouldBeNull();

    // The way out of the deadlock the rule creates, and the only one there is: a character
    // that picks nothing does not move, and the probe answers from where the character is
    // standing, so its answer never changes on its own. Read off a stopped character — nine
    // monsters on screen, none of them reached, the same coordinates for over ten seconds.
    [Fact]
    public void TakesOneTheWalkNeverReachedWhenThereIsNoOtherWayToMove() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "boxed off", 3, 0)], [null], Standing, Unarmed, desperate: true))
            .ShouldBe(1u);

    // Desperate is an ordering, not a free-for-all. What the walk reached still wins.
    [Fact]
    public void StillPrefersWhatTheWalkReachedWhenDesperate() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "behind a wall, in range", 2, 0), Monster(2, "round the long way", 6, 2)],
            [null, 18],
            Standing,
            Bow,
            desperate: true)).ShouldBe(2u);

    // The same box the client uses: two columns to a tile across, one row down. Measuring in
    // squares would give half the reach sideways as up — so the wide one is a shot the
    // character already has and the tall one is a walk it has to make, and that is the order
    // they come in when the walk reached neither.
    [Fact]
    public void MeasuresTheShotTheWayTheClientMeasuresIt() =>
        Picked(TargetPicker.Nearest(
            [Monster(1, "tall, out of reach", 0, 15), Monster(2, "wide, in reach", 28, 0)],
            [null, null],
            Standing,
            Bow,
            desperate: true)).ShouldBe(2u);

    [Fact]
    public void LeavesTheBlacklistedAlone() =>
        TargetPicker.Wanted(
            [Monster(1, "蟹人", 2, 0), Monster(2, "食屍鬼", 4, 0)],
            new HuntSettings { Blacklist = ["蟹人"] },
            Nothing).ShouldHaveSingleItem().Id.ShouldBe(2u);

    [Fact]
    public void TakesOnlyTheWhitelistedWhenThereIsOne() =>
        TargetPicker.Wanted(
            [Monster(1, "蟹人", 2, 0), Monster(2, "食屍鬼", 4, 0)],
            new HuntSettings { Whitelist = ["食屍鬼"] },
            Nothing).ShouldHaveSingleItem().Id.ShouldBe(2u);

    [Fact]
    public void SkipsSomethingItHasGivenUpOn() =>
        TargetPicker.Wanted(
            [Monster(1, "stuck", 2, 0), Monster(2, "fine", 6, 0)],
            new HuntSettings(),
            new HashSet<uint> { 1 }).ShouldHaveSingleItem().Id.ShouldBe(2u);

    // Kept in the order it was found, because the step counts come back in that order and
    // nothing else lines the two up.
    [Fact]
    public void KeepsTheOrderItWasGiven() =>
        TargetPicker.Wanted(
            [Monster(3, "c", 1, 0), Monster(1, "a", 2, 0), Monster(2, "b", 3, 0)],
            new HuntSettings(),
            Nothing).Select(target => target.Id).ShouldBe([3u, 1u, 2u]);

    // A name on both lists is somebody who changed their mind and did not finish tidying up.
    // The reading that attacks less is the safe one.
    [Fact]
    public void LetsTheBlacklistWin()
    {
        var settings = new HuntSettings { Blacklist = ["蟹人"], Whitelist = ["蟹人"] };

        TargetPicker.Allowed("蟹人", settings).ShouldBeFalse();
    }

    [Fact]
    public void TakesAnythingWhenTheWhitelistIsEmpty() =>
        TargetPicker.Allowed("蟹人", new HuntSettings()).ShouldBeTrue();

    /// <summary>What was picked, failing the test when nothing was.</summary>
    /// <remarks>
    /// Not <c>Nearest(...)?.Id.ShouldBe(x)</c>, which is what this file used to say and is a
    /// trap worth naming: the null conditional short circuits the whole chain, so when
    /// nothing is picked the assertion is never reached at all and a test about what was
    /// chosen passes having chosen nothing. Three tests here were silently vacuous for
    /// exactly that reason, and none of them failed when the rule underneath them was
    /// inverted.
    /// </remarks>
    private static uint Picked(HuntTarget? chosen)
    {
        chosen.ShouldNotBeNull();

        return chosen.Value.Id;
    }

    // Untouched, which is what the client reads before anything has hurt something. What
    // is being picked has nothing to do with health, so every candidate carries the same.
    private static HuntTarget Monster(uint id, string name, int x, int y) =>
        new(new GameAddress(0x1000 + id), id, name, x, y, HuntAddresses.HealthUnknown);

    // Burrowed. The server refuses both attack paths for one and says nothing about it, so a
    // hunt that picks one stands there swinging until a watchdog gives up and a rotation
    // spends its whole cast list on nothing — which is what "it keeps throwing skills at
    // things it cannot hit" was.
    [Theory]
    [InlineData(HuntAddresses.ActionSunk)]
    [InlineData(HuntAddresses.ActionHidden)]
    [InlineData(HuntAddresses.ActionFlying)]
    [InlineData(HuntAddresses.ActionSunkDeep)]
    public void LeavesAHiddenMonsterOutOfTheChoosing(byte hidden)
    {
        var wanted = TargetPicker.Wanted(
            [Monster("狼", action: 0), Monster("蟻獅", action: hidden)],
            new HuntSettings(),
            new HashSet<uint>());

        wanted.Select(target => target.Name).ShouldBe(["狼"]);
    }

    // And nothing else on that byte, because it carries whatever the creature is doing this
    // instant as well as the state it is in. 2 is being hurt and 21 is one of the ways a
    // monster comes out of a barrier — both of them things worth attacking.
    [Fact]
    public void KeepsAMonsterThatIsMerelyDoingSomething()
    {
        var wanted = TargetPicker.Wanted(
            [Monster("狼", action: 2), Monster("熊", action: 21)],
            new HuntSettings(),
            new HashSet<uint>());

        wanted.Count.ShouldBe(2);
    }

    private static HuntTarget Monster(string name, byte action) =>
        new(new GameAddress(0x1000), (uint)name.GetHashCode(StringComparison.Ordinal), name,
            100, 200, 100, action);
}
