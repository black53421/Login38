using System.Text;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Settings;

/// <summary>
/// Covers the file of item names the helper window offers.
/// </summary>
/// <remarks>
/// This is the one file in the port a player is expected to open in a text editor, so the
/// interesting cases are the ones where they have already done so: a section removed, a
/// section this build has never heard of, a file saved in the code page their editor
/// defaulted to.
/// </remarks>
public sealed class ItemCatalogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-catalog-" + Guid.NewGuid().ToString("N"));

    private const string Sample = """
        # a preamble
        [AllHP]
        ; what to drink
        Item0=治癒藥水
        Item1 = 強力治癒藥水
        GoHome0=傳送回家的卷軸
        HPMP0=心靈轉換/M

        [AllState]
        Item0=0_加速術/ME
        """;

    private ItemCatalog Catalog() =>
        new(LegacyTextCodec.Auto, NullLogger<ItemCatalog>.Instance, _directory);

    private string CatalogPath => System.IO.Path.Combine(_directory, ItemCatalog.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void TakesOnlyTheKeysWithTheAskedForPrefix() =>
        ItemCatalog.Values(Sample, ItemCatalog.Healing)
            .ShouldBe(["治癒藥水", "強力治癒藥水"]);

    // Two lists share the healing section, told apart by nothing but the key.
    [Fact]
    public void TellsTwoListsInOneSectionApart() =>
        ItemCatalog.Values(Sample, ItemCatalog.ManaConversion).ShouldBe(["心靈轉換/M"]);

    [Fact]
    public void IgnoresOtherSections() =>
        ItemCatalog.Values(Sample, ItemCatalog.States).ShouldBe(["0_加速術/ME"]);

    [Fact]
    public void SaysNothingForASectionThatIsNotThere() =>
        ItemCatalog.Values(Sample, ItemCatalog.Antidotes).ShouldBeEmpty();

    // The order is the player's: it is what they see in the dropdown, and they arranged it.
    [Fact]
    public void KeepsTheOrderTheFileIsIn() =>
        ItemCatalog.Values("[AllHP]\nItem9=z\nItem0=a\nItem5=m", ItemCatalog.Healing)
            .ShouldBe(["z", "a", "m"]);

    // Two identical rows in a list of choices are never what was meant. The reference
    // offered them.
    [Fact]
    public void OffersARepeatedNameOnce() =>
        ItemCatalog.Values("[AllHP]\nItem0=藥水\nItem1=藥水\nItem2=別的", ItemCatalog.Healing)
            .ShouldBe(["藥水", "別的"]);

    [Theory]
    [InlineData("# hash")]
    [InlineData("; semicolon")]
    [InlineData("")]
    [InlineData("   ")]
    public void SkipsWhatIsNotAnEntry(string line) =>
        ItemCatalog.Values($"[AllHP]\n{line}\nItem0=藥水", ItemCatalog.Healing).ShouldBe(["藥水"]);

    [Fact]
    public void ReadsASectionHeaderWhateverItsCase() =>
        ItemCatalog.Values("[allhp]\nITEM0=藥水", ItemCatalog.Healing).ShouldBe(["藥水"]);

    // A file written on Windows and one written by anything else.
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void ReadsEitherKindOfLineEnding(string ending) =>
        ItemCatalog.Values($"[AllHP]{ending}Item0=藥水", ItemCatalog.Healing).ShouldBe(["藥水"]);

    // The suffixes that say what an entry is are part of the value, and one of them is
    // `/M=名字`. Splitting on the last `=` would cut it in half.
    [Fact]
    public void KeepsAnEqualsSignInsideTheValue() =>
        ItemCatalog.Values("[AllState]\nItem0=0_加速術/M=小明", ItemCatalog.States)
            .ShouldBe(["0_加速術/M=小明"]);

    [Fact]
    public void ListsTheSectionsInTheOrderTheyAppear() =>
        ItemCatalog.Sections(Sample).ShouldBe(["AllHP", "AllState"]);

    [Fact]
    public void TakesAWholeSectionWithTheCommentsThatExplainIt()
    {
        var block = ItemCatalog.Block(Sample, "AllHP");

        block.ShouldContain("[AllHP]");
        block.ShouldContain("; what to drink");
        block.ShouldContain("HPMP0=心靈轉換/M");
        block.ShouldNotContain("[AllState]");
        block.ShouldNotContain("0_加速術/ME");
        block.ShouldNotContain("# a preamble");
    }

    [Fact]
    public void TakesNothingForASectionThatIsNotThere() =>
        ItemCatalog.Block(Sample, "AllAntidote").ShouldBeEmpty();

    // Pins the copy that ships inside the launcher: every list the window fills has to
    // have something in it, or a fresh install shows empty dropdowns.
    [Theory]
    [MemberData(nameof(EveryList))]
    public void ShipsWithSomethingInEveryList(ItemList list) =>
        Catalog().Offer(list).ShouldNotBeEmpty();

    public static TheoryData<ItemList> EveryList() => new()
    {
        ItemCatalog.Healing,
        ItemCatalog.ManaConversion,
        ItemCatalog.States,
        ItemCatalog.TransformItems,
        ItemCatalog.Transformations,
        ItemCatalog.Antidotes,
    };

    [Fact]
    public void WritesAStartingFileForAPlayerWhoHasNone()
    {
        Catalog().Ensure().ShouldBeTrue();

        File.Exists(CatalogPath).ShouldBeTrue();
        ItemCatalog.Sections(File.ReadAllText(CatalogPath)).ShouldContain("AllHP");
    }

    // The file is the player's, and it is usually the reason they opened this window at
    // all. A section they already have is never rewritten, however out of date it looks.
    [Fact]
    public void NeverRewritesASectionThePlayerAlreadyHas()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CatalogPath, Sample);

        Catalog().Ensure().ShouldBeTrue();

        var after = File.ReadAllText(CatalogPath);

        after.ShouldContain("# a preamble");
        ItemCatalog.Block(after, "AllHP").ShouldBe(ItemCatalog.Block(Sample, "AllHP"));
    }

    // The reference only wrote its template when the file was missing, so a player who
    // upgraded never got the sections a newer build added — and the code that eventually
    // added them held a second copy of their text, which drifted from the first.
    [Fact]
    public void AddsASectionAnOlderFileNeverHad()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CatalogPath, Sample);

        var catalog = Catalog();
        catalog.Ensure().ShouldBeTrue();

        var after = File.ReadAllText(CatalogPath);

        ItemCatalog.Sections(after).ShouldContain("AllAntidote");
        catalog.Offer(ItemCatalog.Antidotes).ShouldNotBeEmpty();

        // And the sections it already had are untouched.
        ItemCatalog.Values(after, ItemCatalog.Healing).ShouldBe(["治癒藥水", "強力治癒藥水"]);
    }

    [Fact]
    public void AddsNothingTwice()
    {
        var catalog = Catalog();

        catalog.Ensure().ShouldBeTrue();
        var once = File.ReadAllText(CatalogPath);

        catalog.Ensure().ShouldBeTrue();
        File.ReadAllText(CatalogPath).ShouldBe(once);
    }

    [Fact]
    public void PrefersWhatThePlayerWroteOverWhatItShipsWith()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CatalogPath, "[AllHP]\nItem0=我自己的藥水");

        Catalog().Offer(ItemCatalog.Healing).ShouldBe(["我自己的藥水"]);
    }

    [Fact]
    public void FallsBackToTheShippedListForASectionThePlayerEmptied()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CatalogPath, "[AllHP]\n[AllAntidote]\n");

        Catalog().Offer(ItemCatalog.Antidotes).ShouldNotBeEmpty();
    }

    // The reference read this file as UTF-8 and treated anything else as an unreadable
    // file — which is an empty dropdown and no explanation. A player who saved it from an
    // older editor on a Taiwanese system has a code page 950 file.
    [Fact]
    public void ReadsAFileSavedInTheClientsOwnCodePage()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(CatalogPath, Encoding.GetEncoding(950).GetBytes("[AllHP]\r\nItem0=治癒藥水\r\n"));

        Catalog().Offer(ItemCatalog.Healing).ShouldBe(["治癒藥水"]);
    }

    [Fact]
    public void ReadsAFileSavedWithAByteOrderMark()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CatalogPath, "[AllHP]\r\nItem0=治癒藥水\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Catalog().Offer(ItemCatalog.Healing).ShouldBe(["治癒藥水"]);
    }

    // The window fills six dropdowns from this. The reference re-read and re-parsed the
    // whole file for each one.
    [Fact]
    public void ReadsTheFileOnce()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CatalogPath, "[AllHP]\nItem0=第一次");

        var catalog = Catalog();
        catalog.Offer(ItemCatalog.Healing).ShouldBe(["第一次"]);

        File.WriteAllText(CatalogPath, "[AllHP]\nItem0=第二次");
        catalog.Offer(ItemCatalog.Healing).ShouldBe(["第一次"]);

        catalog.Forget();
        catalog.Offer(ItemCatalog.Healing).ShouldBe(["第二次"]);
    }
}
