using Login38.Aux.Game;
using Shouldly;

namespace Login38.Aux.Tests.Game;

/// <summary>
/// Covers the brackets the client puts after a skill's name.
/// </summary>
/// <remarks>
/// The player writes <c>加速術</c> in their settings; the client stores
/// <c>加速術 (40/0)</c>. Everything that looks a skill up depends on the two meeting, and
/// the cases here are the reference's own — they came from a real client's spell table.
/// </remarks>
public sealed class SpellNamesTests
{
    [Theory]
    [InlineData("加速術 (40/0)", "加速術")]
    [InlineData("魔法相剋術 (40/0/2)", "魔法相剋術")]
    [InlineData("造痕術 (5/80/1)", "造痕術")]
    [InlineData("初級瘟疫術 (4/0)", "初級瘟疫術")]
    public void TakesOffTheClientsNumbers(string full, string name) =>
        SpellNames.StripSuffix(full).ShouldBe(name);

    // The elf's physical skills have no space before the bracket. Matching only the spaced
    // form would leave every one of them unfindable.
    [Theory]
    [InlineData("三重矢(15/0)", "三重矢")]
    [InlineData("集中射(10/0/1)", "集中射")]
    [InlineData("理解屬性(10/0)", "理解屬性")]
    public void TakesThemOffWithNoSpaceBefore(string full, string name) =>
        SpellNames.StripSuffix(full).ShouldBe(name);

    [Theory]
    [InlineData("加速術")]
    [InlineData("")]
    public void LeavesANameWithNoNumbersAlone(string name) =>
        SpellNames.StripSuffix(name).ShouldBe(name);

    // Brackets holding anything but numbers are part of the name, and taking them off would
    // make the skill unmatchable by what the player can actually see.
    [Fact]
    public void KeepsBracketsThatAreNotNumbers() =>
        SpellNames.StripSuffix("怪怪 (測試)").ShouldBe("怪怪 (測試)");
}
