using Login38.Aux.Game;
using Shouldly;

namespace Login38.Aux.Tests.Game;

/// <summary>
/// Covers what an item is called once the client has finished decorating the name.
/// </summary>
/// <remarks>
/// Every rule an operator writes is matched through here, so the cost of getting it wrong
/// is a rule that silently never fires — or one that fires on the wrong item.
/// </remarks>
public sealed class ItemNamesTests
{
    [Theory]
    [InlineData("變形卷軸", "變形卷軸")]
    [InlineData("歐西斯弓", "歐西斯弓")]
    public void LeavesANameWithNoCountAlone(string name, string expected) =>
        ItemNames.StripQuantity(name).ShouldBe(expected);

    [Theory]
    [InlineData("肉 (191)", "肉")]
    [InlineData("象牙塔變身卷軸 (364)", "象牙塔變身卷軸")]
    public void RemovesAStackCount(string name, string expected) =>
        ItemNames.StripQuantity(name).ShouldBe(expected);

    // Past a thousand the client starts writing a separator, so a rule written when the
    // stack was small has to keep matching once it is large.
    [Theory]
    [InlineData("變形卷軸 (1,000)", "變形卷軸")]
    [InlineData("金幣 (17,099)", "金幣")]
    public void RemovesAStackCountWithSeparators(string name, string expected) =>
        ItemNames.StripQuantity(name).ShouldBe(expected);

    // The reason the count is only removed when the brackets hold digits: plenty of items
    // are told apart by what is inside theirs.
    [Theory]
    [InlineData("精靈水晶(水之元氣)")]
    [InlineData("魔法書(壞物術)")]
    [InlineData("魔法卷軸 (擬似魔法武器)")]
    public void KeepsBracketsThatArePartOfTheName(string name) =>
        ItemNames.StripQuantity(name).ShouldBe(name);

    // With and without the space, because the client writes both.
    [Theory]
    [InlineData("銀劍 (揮舞)", "銀劍")]
    [InlineData("銀劍(揮舞)", "銀劍")]
    [InlineData("胸甲 (使用中)", "胸甲")]
    [InlineData("胸甲(使用中)", "胸甲")]
    public void RemovesTheClientsOwnStateMark(string name, string expected) =>
        ItemNames.StripState(name).ShouldBe(expected);

    [Theory]
    [InlineData("魔法卷軸 (擬似魔法武器)")]
    [InlineData("金幣 (17,099)")]
    [InlineData("純粹的劍")]
    public void LeavesEverythingElseToTheOtherRule(string name) =>
        ItemNames.StripState(name).ShouldBe(name);

    [Theory]
    [InlineData("銀劍 (揮舞)", "銀劍")]
    [InlineData("胸甲 (使用中)", "胸甲")]
    [InlineData("金幣 (17,099)", "金幣")]
    [InlineData("魔法卷軸 (擬似魔法武器)", "魔法卷軸 (擬似魔法武器)")]
    public void CleaningIsBothRules(string name, string expected) =>
        ItemNames.Clean(name).ShouldBe(expected);

    // What the matching is for: the same sword before and after it is picked up.
    [Fact]
    public void MatchesAnItemWhateverStateItIsIn()
    {
        ItemNames.Match("銀劍 (揮舞)", "銀劍").ShouldBeTrue();
        ItemNames.Match("銀劍", "銀劍 (揮舞)").ShouldBeTrue();
        ItemNames.Match("肉 (191)", "肉").ShouldBeTrue();
    }

    // Two scrolls that differ only inside their brackets are different scrolls.
    [Fact]
    public void DoesNotMatchTwoItemsThatDifferInsideTheirBrackets() =>
        ItemNames.Match("魔法卷軸 (初級治癒術)", "魔法卷軸 (擬似魔法武器)").ShouldBeFalse();

    [Fact]
    public void TreatsAnEmptyNameAsEmpty() => ItemNames.Clean("   ").ShouldBeEmpty();

    // A bracket that never closes is not a count, and reading past it would be a slice
    // outside the string.
    [Fact]
    public void SurvivesAnUnclosedBracket() => ItemNames.Clean("肉 (191").ShouldBe("肉 (191");
}
