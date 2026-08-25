using Login38.Core.Icons;
using Shouldly;

namespace Login38.Core.Tests.Icons;

/// <summary>
/// Covers the manifest operators write by hand.
/// </summary>
/// <remarks>
/// It is read once, at launch, and nothing else in the feature can report a problem with
/// it — a bad entry either animates the wrong icon or crashes the client the first time the
/// item is drawn. So every way of getting it wrong has to be refused here, by name.
/// </remarks>
public sealed class DynamicIconManifestTests
{
    private const string Sample = """
        <dynamicicons>
          <item tbt="1524" speed="80" interval="2000">
            <png>30001</png>
            <png>30002</png>
          </item>
        </dynamicicons>
        """;

    [Fact]
    public void ReadsAnEntry()
    {
        var animation = DynamicIconManifest.Parse(Sample).ShouldHaveSingleItem();

        animation.Icon.ShouldBe((ushort)1524);
        animation.FrameMilliseconds.ShouldBe((ushort)80);
        animation.RestMilliseconds.ShouldBe(2000u);
        animation.Frames.ShouldBe([30001u, 30002u]);
    }

    // The table the game scans is in icon order and searched by stepping through it, so the
    // order has to be settled here rather than left to however the file was written.
    [Fact]
    public void OrdersEntriesByIcon()
    {
        var xml = """
            <dynamicicons>
              <item tbt="900" speed="50" interval="0"><png>1</png></item>
              <item tbt="12" speed="50" interval="0"><png>2</png></item>
              <item tbt="400" speed="50" interval="0"><png>3</png></item>
            </dynamicicons>
            """;

        DynamicIconManifest.Parse(xml).Select(a => a.Icon).ShouldBe([(ushort)12, 400, 900]);
    }

    [Fact]
    public void RoundTripsThroughItsOwnOutput()
    {
        var animations = DynamicIconManifest.Parse(Sample);

        DynamicIconManifest.Parse(DynamicIconManifest.ToXml(animations)).ShouldBe(animations);
    }

    [Fact]
    public void ReadsAnEmptyManifestAsNoAnimations() =>
        DynamicIconManifest.Parse("<dynamicicons></dynamicicons>").ShouldBeEmpty();

    // An icon number past the field's width used to be truncated, so tbt="70000" animated
    // icon 4464 — an icon the operator never mentioned and would never think to look at.
    [Fact]
    public void RefusesAnIconNumberTooLargeToMean() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            """<dynamicicons><item tbt="70000" speed="50" interval="0"><png>1</png></item></dynamicicons>"""))
            .Message.ShouldContain("70000");

    // With no rest either, the whole cycle is zero — and the game divides by it. That is a
    // client that exits the moment the item appears, with nothing to connect it to this file.
    [Fact]
    public void RefusesAFrameTimeOfZero() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            """<dynamicicons><item tbt="1" speed="0" interval="0"><png>1</png></item></dynamicicons>"""))
            .Message.ShouldContain("millisecond");

    [Fact]
    public void RefusesAnEntryWithNoFrames() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            """<dynamicicons><item tbt="1" speed="5" interval="1"></item></dynamicicons>"""))
            .Message.ShouldContain("frames");

    // The table has fixed-size records, so this is not a soft limit.
    [Fact]
    public void RefusesMoreFramesThanARecordHolds()
    {
        var frames = string.Concat(Enumerable.Repeat("<png>1</png>", IconAnimation.MaxFrames + 1));

        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            $"""<dynamicicons><item tbt="1" speed="5" interval="1">{frames}</item></dynamicicons>"""));
    }

    private static readonly string[] Attributes = ["tbt=\"1\"", "speed=\"5\"", "interval=\"1\""];

    [Theory]
    [InlineData("tbt")]
    [InlineData("speed")]
    [InlineData("interval")]
    public void RefusesAnEntryMissingAnAttribute(string missing)
    {
        var attributes = Attributes.Where(a => !a.StartsWith(missing, StringComparison.Ordinal));

        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            $"""<dynamicicons><item {string.Join(' ', attributes)}><png>1</png></item></dynamicicons>"""))
            .Message.ShouldContain(missing);
    }

    [Fact]
    public void RefusesAFrameThatIsNotAResourceNumber() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            """<dynamicicons><item tbt="1" speed="5" interval="1"><png>flame.png</png></item></dynamicicons>"""))
            .Message.ShouldContain("flame.png");

    // Two entries for one icon has no sensible reading. The reference kept whichever came
    // last and said nothing, so an operator who duplicated a line while editing saw the
    // wrong animation and no reason for it.
    [Fact]
    public void RefusesTwoEntriesForOneIcon() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            """
            <dynamicicons>
              <item tbt="7" speed="5" interval="1"><png>1</png></item>
              <item tbt="7" speed="5" interval="1"><png>2</png></item>
            </dynamicicons>
            """))
            .Message.ShouldContain("more than one");

    // The reference searched for "<item" as a substring, so a sibling element whose name
    // merely started the same way was read as an entry and failed on a missing attribute.
    [Fact]
    public void IgnoresElementsThatAreNotEntries() =>
        DynamicIconManifest.Parse(
            """
            <dynamicicons>
              <items count="1" />
              <item tbt="7" speed="5" interval="1"><png>1</png></item>
            </dynamicicons>
            """)
            .ShouldHaveSingleItem().Icon.ShouldBe((ushort)7);

    [Fact]
    public void ReportsTheLineOfAProblem() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse(
            """
            <dynamicicons>
              <item tbt="1" speed="5" interval="1"><png>1</png></item>
              <item tbt="2" speed="5"><png>1</png></item>
            </dynamicicons>
            """))
            .Message.ShouldContain("line 3");

    [Fact]
    public void RefusesSomethingThatIsNotTheManifest() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse("<servers></servers>"))
            .Message.ShouldContain("dynamicicons");

    [Fact]
    public void RefusesADocumentThatIsNotXml() =>
        Should.Throw<DynamicIconException>(() => DynamicIconManifest.Parse("<dynamicicons>"));
}
