using System.Text;
using Login38.Aux.Game;
using Shouldly;

namespace Login38.Aux.Tests.Game;

/// <summary>
/// Covers reading the client's sprite table out of its heap.
/// </summary>
/// <remarks>
/// <para>
/// The shape is the client's own <c>SPR.txt</c>: a record header at the start of a line —
/// optionally behind a <c>#</c> — and its properties on the lines after it, each indented
/// with a tab. One of those properties is <c>102.type(n)</c>, which is what tells a monster
/// from a door.
/// </para>
/// <para>
/// The indent is load-bearing. A property line begins with digits too, so without it the
/// parser would read <c>102.type(10)</c> as a record numbered 102 and hang the type on
/// that instead of on the record it belongs to.
/// </para>
/// </remarks>
public sealed class SpriteTypesTests
{
    [Fact]
    public void ReadsTheTypeOffARecord() =>
        Parse("100 48 goblin\n\t102.type(10)\n")[100].ShouldBe(SpriteTypes.Monster);

    // A property line starts with digits as well, and is only told from a header by its
    // indent. Without that this maps 102 to 10 and says nothing about 100 at all.
    [Fact]
    public void DoesNotTakeAnIndentedPropertyForARecordOfItsOwn() =>
        Parse("100 48 goblin\n\t102.type(10)\n").ShouldBe(new Dictionary<ushort, byte> { [100] = 10 });

    // The client also writes the type on the header line itself for some records.
    [Fact]
    public void ReadsATypeWrittenOnTheHeaderLine() =>
        Parse("#94 48 sword orc 102.type(10)\n")[94].ShouldBe(SpriteTypes.Monster);

    // Records are separated by newlines in some of the blob and by nul bytes in the rest,
    // depending on which loader put them there.
    [Fact]
    public void SplitsOnNulAsWellAsNewline() =>
        Parse("#94 48 sword orc 102.type(10)\n\0148 48 kent castle guard 102.type(12)\n\0")
            .ShouldBe(new Dictionary<ushort, byte> { [94] = 10, [148] = 12 });

    [Fact]
    public void IgnoresTheHashTheClientPutsOnSomeHeaders() =>
        Parse("#100 48 goblin\n\t102.type(10)\n")[100].ShouldBe(SpriteTypes.Monster);

    [Fact]
    public void CarriesTheHeaderThroughAllOfItsPropertyLines() =>
        Parse("""
            100 48 goblin
            	0.(1 4,0.0:4 0.1:4)
            	4.attack(1 1,9.0:5)
            	102.type(10)
            """)[100].ShouldBe(SpriteTypes.Monster);

    // A record can be declared as a copy of another. The client writes that in the second
    // field of the header, not the first.
    [Fact]
    public void FollowsAnAliasToTheRecordThatDeclaresAType()
    {
        var types = Parse("#94 48 sword orc 102.type(10)\n336 32=94 guard archer shadow\n");

        types[336].ShouldBe(SpriteTypes.Monster);
        types[94].ShouldBe(SpriteTypes.Monster);
    }

    // A record that both copies another and declares its own type keeps its own.
    [Fact]
    public void PrefersARecordsOwnTypeOverTheOneItCopies() =>
        Parse("#94 48 sword orc 102.type(10)\n336 32=94 guard archer shadow 102.type(0)\n")[336]
            .ShouldBe((byte)0);

    [Fact]
    public void FollowsAChainOfThem() =>
        Parse("100 48 a 102.type(10)\n200 1=100 b\n300 1=200 c\n400 1=300 d\n")[400]
            .ShouldBe(SpriteTypes.Monster);

    // A table that points at itself would otherwise be walked for ever.
    [Fact]
    public void GivesUpOnAnAliasThatPointsBackAtItself() =>
        Parse("100 1=200 a\n200 1=100 b\n").ShouldBeEmpty();

    [Fact]
    public void HasNothingToSayAboutARecordWithNoTypeAnywhere() =>
        Parse("100 48 goblin\n\t0.(1 4,0.0:4)\n").ShouldBeEmpty();

    [Fact]
    public void IgnoresALineThatIsNeitherAHeaderNorAProperty() =>
        Parse("; a comment\n\n   \n").ShouldBeEmpty();

    [Fact]
    public void ReadsATypeWithSpaceAroundIt() =>
        Parse("100 48 a\n\t102.type( 10 )\n")[100].ShouldBe(SpriteTypes.Monster);

    [Fact]
    public void IgnoresATypeThatIsNotANumber() =>
        Parse("100 48 a\n\t102.type(monster)\n").ShouldBeEmpty();

    [Fact]
    public void IgnoresARecordNumberTooBigForASprite() =>
        Parse("999999 48 a 102.type(10)\n").ShouldBeEmpty();

    // Windows line endings are in the blob and are not part of the number.
    [Fact]
    public void StripsTheCarriageReturn() =>
        Parse("100 48 a\r\n\t102.type(10)\r\n")[100].ShouldBe(SpriteTypes.Monster);

    [Fact]
    public void ReadsTheLastRecordWithNoTrailingNewline() =>
        Parse("100 48 a\n\t102.type(10)")[100].ShouldBe(SpriteTypes.Monster);

    // The client's own names are in its code page, not UTF-8. Nothing here reads them, and
    // nothing here may be thrown off by them either.
    [Fact]
    public void ReadsPastNamesInTheClientsCodePage()
    {
        byte[] raw = [.. "100 48 "u8, 0xA5, 0x5B, 0xB0, 0xAA, .. "\n\t102.type(10)\n"u8];

        SpriteTypes.Parse(raw)[100].ShouldBe(SpriteTypes.Monster);
    }

    [Fact]
    public void HasNothingToSayAboutAnEmptyBlob() => SpriteTypes.Parse([]).ShouldBeEmpty();

    private static Dictionary<ushort, byte> Parse(string table) =>
        SpriteTypes.Parse(Encoding.ASCII.GetBytes(table));
}
