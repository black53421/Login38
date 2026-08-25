using Login38.Aux.Toggles;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the rule that turns a difference in levels into a colour.
/// </summary>
public sealed class MonsterColoursTests
{
    [Theory]
    [InlineData(50, 50, MonsterColour.White)]
    [InlineData(1, 50, MonsterColour.White)]
    [InlineData(51, 50, MonsterColour.Green)]
    [InlineData(60, 50, MonsterColour.Green)]
    [InlineData(61, 50, MonsterColour.Blue)]
    [InlineData(69, 50, MonsterColour.Blue)]
    [InlineData(70, 50, MonsterColour.LightRed)]
    [InlineData(79, 50, MonsterColour.LightRed)]
    [InlineData(80, 50, MonsterColour.DarkRed)]
    [InlineData(120, 1, MonsterColour.DarkRed)]
    public void PutsAMonsterInTheBandItBelongsTo(uint monster, uint player, MonsterColour expected) =>
        MonsterColours.For(monster, player).ShouldBe(expected);

    // Ten above is the last green and eleven the first blue, which is the one edge in this
    // rule that is not a round number.
    [Fact]
    public void ChangesColourBetweenTenAboveAndEleven()
    {
        MonsterColours.For(60, 50).ShouldBe(MonsterColour.Green);
        MonsterColours.For(61, 50).ShouldBe(MonsterColour.Blue);
    }

    // The difference is clamped rather than signed: a monster far below the player is no
    // more interesting than one at their level, and an unsigned subtraction that wrapped
    // would make it the most dangerous thing on screen.
    [Fact]
    public void DoesNotWrapForAMonsterBelowThePlayer() =>
        MonsterColours.For(1, 120).ShouldBe(MonsterColour.White);

    [Fact]
    public void HasNothingToWriteForAMonsterThePlayerHasOutlevelled() =>
        MonsterColours.PatchFor(50, 50).ShouldBeNull();

    [Fact]
    public void HasAColourToWriteForOneAbove() =>
        MonsterColours.PatchFor(51, 50).ShouldBe(MonsterColours.Green);

    [Theory]
    [InlineData(0x8800)]
    [InlineData(0xF800)]
    [InlineData(0x001F)]
    [InlineData(0x07E0)]
    public void RecognisesTheColoursItWrites(int colour) =>
        MonsterColours.IsFeature((ushort)colour).ShouldBeTrue();

    // White is the client's own, so it is never claimed: a name already white is one this
    // feature has not touched, and there is nothing to restore.
    [Fact]
    public void DoesNotClaimTheClientsOwnWhite() =>
        MonsterColours.IsFeature(MonsterColours.White).ShouldBeFalse();

    [Fact]
    public void HasFourColoursToRecognise() =>
        MonsterColours.Feature.Length.ShouldBe(4);

    [Fact]
    public void MapsEveryBandToADistinctValue() =>
        Enum.GetValues<MonsterColour>().Select(MonsterColours.Rgb565).Distinct().Count().ShouldBe(5);
}
