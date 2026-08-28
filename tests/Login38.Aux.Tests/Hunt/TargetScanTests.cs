using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers telling a monster from everything else standing in the world.
/// </summary>
/// <remarks>
/// The numbers in these cases are transcribed from a live client: a scan of forty-four
/// records around a character holding three 蟹人, three 龍龜, one 狼人, thirty-odd dropped
/// items, five corpses and a pile of the client's own effects. Every case here is one of
/// those records.
/// </remarks>
public sealed class TargetScanTests
{
    private const byte Actor = HuntAddresses.ActorType;
    private const byte Item = 0x00;
    private const byte Effect = 0x09;
    private const byte TownPerson = 0x0E;

    [Fact]
    public void TakesALivingMonster() =>
        TargetScan.IsMonster(Actor, action: 0x03, isPlayer: 0, isGone: 0).ShouldBeTrue();

    // The player carries the flag at +0x27; both monsters standing beside it did not.
    [Fact]
    public void LeavesThePlayerAlone() =>
        TargetScan.IsMonster(Actor, action: 0x30, isPlayer: 1, isGone: 0).ShouldBeFalse();

    // A corpse: +0x58 set and the action code at its dying value.
    [Fact]
    public void LeavesACorpseAlone() =>
        TargetScan.IsMonster(Actor, action: 0x08, isPlayer: 0, isGone: 1).ShouldBeFalse();

    // Dying but not yet flagged gone. Locking onto one wastes the swing that follows.
    [Fact]
    public void LeavesSomethingPlayingItsDeathAlone() =>
        TargetScan.IsMonster(Actor, action: HuntAddresses.DyingAction, isPlayer: 0, isGone: 0)
            .ShouldBeFalse();

    [Fact]
    public void LeavesADroppedItemAlone() =>
        TargetScan.IsMonster(Item, action: 0x00, isPlayer: 0, isGone: 0).ShouldBeFalse();

    [Fact]
    public void LeavesTheClientsOwnEffectsAlone() =>
        TargetScan.IsMonster(Effect, action: 0x00, isPlayer: 0, isGone: 0).ShouldBeFalse();

    // Action 0 on an actor is not death and not an item — it is a monster standing still.
    // The same value on the player record between two samples is why this byte cannot be
    // read as a state.
    [Fact]
    public void TakesAMonsterDoingNothing() =>
        TargetScan.IsMonster(Actor, action: 0x00, isPlayer: 0, isGone: 0).ShouldBeTrue();

    // Read off a town: twenty-six records, every shopkeeper and guard among them typed
    // 0x0E. A hunt standing in a market that offered them would attack the people it is
    // meant to be buying potions from.
    [Fact]
    public void LeavesTownPeopleAlone() =>
        TargetScan.IsMonster(TownPerson, action: 0x03, isPlayer: 0, isGone: 0).ShouldBeFalse();

    [Fact]
    public void ReadsTheNameOutOfALabel() =>
        TargetScan.NameFrom("蟹人#45164:1610").ShouldBe("蟹人");

    // Not every label carries the suffix. One 龍龜 held the bare name while two others of
    // its kind in the same scan held the full form.
    [Fact]
    public void ReadsALabelThatIsJustAName() =>
        TargetScan.NameFrom("龍龜").ShouldBe("龍龜");

    [Fact]
    public void HasNoNameForAnEmptyLabel() =>
        TargetScan.NameFrom(string.Empty).ShouldBeEmpty();
}
