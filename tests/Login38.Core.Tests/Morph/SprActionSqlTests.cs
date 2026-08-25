using Login38.Core.Morph;
using Shouldly;

namespace Login38.Core.Tests.Morph;

/// <summary>
/// Covers building the server's <c>spr_action</c> table out of the client's sprite table.
/// </summary>
/// <remarks>
/// The client's <c>SPR.txt</c> is decades of hand edits: names in several languages,
/// comments, labels made of digits, and lines that are metadata rather than animation. The
/// interesting cases are all of those, not the well-formed line.
/// </remarks>
public sealed class SprActionSqlTests
{
    [Fact]
    public void RefusesATableWithNoSpriteInIt() =>
        Should.Throw<FormatException>(() => SprActionSql.Generate("0.walk(1 1,8.0:2)"))
            .Message.ShouldContain("no sprite");

    [Fact]
    public void RefusesAHeaderThatDoesNotNameASprite() =>
        Should.Throw<FormatException>(() => SprActionSql.Generate("#abc 80 name\n0.walk(1 1,8.0:2)"));

    [Fact]
    public void WritesOneRowForOneAction() =>
        SprActionSql.Generate("#100 80 name\n0.walk(1 2,8.0:3 8.1:4)").Sql
            .ShouldBe("INSERT INTO `spr_action` VALUES ('100', '0', '7', '24');\n");

    [Fact]
    public void CountsTheRowsItWrote() =>
        SprActionSql.Generate("#100 80 name\n0.walk(1 2,8.0:3 8.1:4)").Rows.ShouldBe(1);

    // 110 is not an action. It sets the rate for everything after it.
    [Fact]
    public void TakesTheFrameRateFromTheLineThatSetsIt() =>
        SprActionSql.Generate("#100 80 name\n110.framerate(30)\n4.attack(1 1,9.0:5)").Sql
            .ShouldBe("INSERT INTO `spr_action` VALUES ('100', '4', '5', '30');\n");

    [Fact]
    public void RunsAtTwentyFourUntilToldOtherwise() =>
        SprActionSql.Read("#100 80 name\n4.attack(1 1,9.0:5)")[0].FrameRate
            .ShouldBe(SprActionSql.DefaultFrameRate);

    // A label in the header is not a command, however many digits are in it.
    [Fact]
    public void IgnoresWhatIsWrittenAfterASpritesNumber() =>
        SprActionSql.Generate(
                "#18840 1 Skill_Elf_Pollute_Water_2026\n102.type(0)\n#18841 1 next\n4.attack(1 1,9.0:5)")
            .Sql.ShouldBe("INSERT INTO `spr_action` VALUES ('18841', '4', '5', '24');\n");

    // The names in this file are Chinese, and a name between two numbers must separate
    // them rather than glue them together.
    [Fact]
    public void ReadsPastAName() =>
        SprActionSql.Generate("#18840 64 [弓手黃金]\n110.framerate(25)\n0.walk(1 4,0.0:4 0.1:4 0.2:4 0.3:4)")
            .Sql.ShouldBe("INSERT INTO `spr_action` VALUES ('18840', '0', '16', '25');\n");

    [Fact]
    public void WritesNothingForAnActionTheServerHasNoColumnFor() =>
        SprActionSql.Generate("#100 80 name\n100.shadow(1 1,8.0:2)").Rows.ShouldBe(0);

    // A metadata line with nothing after it must not stop the rest of the file being read.
    [Fact]
    public void ReadsPastALineThatIsNothingButANumber() =>
        SprActionSql.Generate("#18840 1 name\n102\n#18841 1 next\n4.attack(1 1,9.0:5)").Sql
            .ShouldBe("INSERT INTO `spr_action` VALUES ('18841', '4', '5', '24');\n");

    // `4=100` means "this sprite's action 4 is the same as sprite 100's".
    [Fact]
    public void CopiesAnActionAReferencePointsAt() =>
        SprActionSql.Generate("#100 80 name\n4.attack(1 1,9.0:5)\n#200 80 name\n4=100 copied_attack").Sql
            .ShouldBe(
                "INSERT INTO `spr_action` VALUES ('100', '4', '5', '24');\n" +
                "INSERT INTO `spr_action` VALUES ('200', '4', '5', '24');\n");

    // Including the rate, which may have been set by a line the referring sprite is
    // nowhere near.
    [Fact]
    public void CopiesTheRateAlongWithTheFrames()
    {
        var actions = SprActionSql.Read(
            "#100 80 name\n110.framerate(30)\n4.attack(1 1,9.0:5)\n#200 80 name\n4=100 copied");

        actions[1].ShouldBe(new SprAction(200, 4, 5, 30));
    }

    [Fact]
    public void CopiesTheFirstOfItsKind()
    {
        var actions = SprActionSql.Read(
            "#100 80 name\n4.attack(1 1,9.0:5)\n4.attack(1 1,9.0:9)\n#200 80 name\n4=100 copied");

        actions[^1].FrameCount.ShouldBe(5u);
    }

    [Fact]
    public void SkipsAReferenceToSomethingThatIsNotThere() =>
        SprActionSql.Generate("#200 80 name\n4=100 copied_attack").Rows.ShouldBe(0);

    // And does not quietly fall back to reading it as a plain action 4.
    [Fact]
    public void SkipsAReferenceThatNamesNothing() =>
        SprActionSql.Generate("#200 80 name\n4=abc copied_attack").Rows.ShouldBe(0);

    // The client hands these to strtoul, which stops at the first character that is not a
    // digit. Anything after it belongs to the next field.
    [Theory]
    [InlineData("2<478", 2u)]
    [InlineData("110", 110u)]
    public void ReadsTheNumberATokenStartsWith(string token, uint expected) =>
        SprActionSql.Read($"#100 80 name\n4.attack(1 1,9.0:{token})")[0].FrameCount.ShouldBe(expected);

    // A negative count is not a count. The minus sign is kept so that the digits after it
    // stay part of a token that reads as no number at all.
    [Fact]
    public void ReadsNothingFromANegativeCount() =>
        SprActionSql.Read("#100 80 name\n4.attack(1 1,9.0:-3)")[0].FrameCount.ShouldBe(0u);

    [Fact]
    public void AddsUpEveryCountInACommand() =>
        SprActionSql.Read("#100 80 name\n0.walk(1 4,0.0:4 0.1:4 0.2:4 0.3:4)")[0].FrameCount
            .ShouldBe(16u);

    // A table with a nonsense count should produce a row a server will reject, not one
    // that wrapped round to something plausible.
    [Fact]
    public void StopsAtTheLargestCountRatherThanWrappingRound() =>
        SprActionSql.Read("#100 80 name\n0.walk(1 2,0.0:4294967295 0.1:5)")[0].FrameCount
            .ShouldBe(uint.MaxValue);

    [Fact]
    public void ReadsFromTheFirstSpriteRatherThanTheFirstLine() =>
        SprActionSql.Generate("anything at all\nand more\n#100 80 name\n4.attack(1 1,9.0:5)").Rows
            .ShouldBe(1);

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void ReadsEitherKindOfLineEnding(string ending) =>
        SprActionSql.Generate($"#100 80 name{ending}4.attack(1 1,9.0:5)").Rows.ShouldBe(1);

    [Fact]
    public void WritesNothingForNothing() => SprActionSql.ToSql([]).ShouldBeEmpty();

    // Every action the server's table has a column for, and none of the ones it does not.
    [Theory]
    [InlineData(0u, true)]
    [InlineData(67u, true)]
    [InlineData(2u, false)]
    [InlineData(83u, false)]
    [InlineData(110u, false)]
    public void WritesOnlyTheActionsTheServerKnows(uint action, bool written) =>
        SprActionSql.Read($"#100 80 name\n{action}.thing(1 1,9.0:5)").Count
            .ShouldBe(written ? 1 : 0);

    // A real table is tens of thousands of rows, and every reference in it used to be
    // answered by scanning everything read so far.
    [Fact]
    public void ReadsALargeTableWithoutScanningItAgainForEveryReference()
    {
        var text = string.Join('\n',
            Enumerable.Range(0, 4000).Select(i => $"#{i} 80 name\n4.attack(1 1,9.0:5)")
                .Concat(Enumerable.Range(0, 4000).Select(i => $"#{20000 + i} 80 name\n4={i} copied")));

        var actions = SprActionSql.Read(text);

        actions.Count.ShouldBe(8000);
        actions[^1].ShouldBe(new SprAction(23999, 4, 5, 24));
    }
}
