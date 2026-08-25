using Login38.Core.Icons;
using Shouldly;

namespace Login38.Core.Tests.Icons;

/// <summary>
/// Pins the table layout the game's scan depends on.
/// </summary>
/// <remarks>
/// Machine code inside the game steps this table by a constant and reads fields at fixed
/// offsets. There is nothing on that side that would notice a layout change, so the shape
/// is asserted here in the same terms the shellcode uses: the record size, the offset of
/// each field, and the order records appear in.
/// </remarks>
public sealed class IconAnimationTableTests
{
    [Fact]
    public void HasTheRecordSizeTheScanStepsBy() => IconAnimationTable.RecordSize.ShouldBe(408);

    [Fact]
    public void WritesOneRecordPerAnimation() =>
        IconAnimationTable.Serialise([Sample(80), Sample(81)]).Length.ShouldBe(408 * 2);

    [Fact]
    public void PutsEveryFieldWhereTheScanReadsIt()
    {
        var table = IconAnimationTable.Serialise([new IconAnimation(80, 120, 1500, [30101, 30102])]);

        BitConverter.ToUInt16(table, 0).ShouldBe((ushort)80);      // compared first by the scan
        BitConverter.ToUInt16(table, 2).ShouldBe((ushort)120);
        BitConverter.ToUInt32(table, 4).ShouldBe(1500u);
        BitConverter.ToUInt32(table, 8).ShouldBe(2u);
        BitConverter.ToUInt32(table, IconAnimationTable.FramesOffset).ShouldBe(30101u);
        BitConverter.ToUInt32(table, IconAnimationTable.FramesOffset + 4).ShouldBe(30102u);
    }

    // The count is what stops the scan, so slots past it are never read — but they are part
    // of the record either way, and a record that shrank would move the next one.
    [Fact]
    public void LeavesUnusedFrameSlotsEmpty()
    {
        var table = IconAnimationTable.Serialise([new IconAnimation(1, 50, 0, [7])]);

        table.AsSpan(IconAnimationTable.FramesOffset + 4).ToArray().ShouldAllBe(b => b == 0);
    }

    // The scan walks the table in order and stops at the end; it does not sort or search.
    [Fact]
    public void OrdersRecordsByIcon()
    {
        var table = IconAnimationTable.Serialise([Sample(900), Sample(12), Sample(400)]);

        BitConverter.ToUInt16(table, 0).ShouldBe((ushort)12);
        BitConverter.ToUInt16(table, 408).ShouldBe((ushort)400);
        BitConverter.ToUInt16(table, 816).ShouldBe((ushort)900);
    }

    // By the time the table reaches the game the frames are addresses of decoded images in
    // its memory, which is why the field is as wide as a pointer.
    [Fact]
    public void CarriesFramesWideEnoughToBeAddresses()
    {
        var table = IconAnimationTable.Serialise([new IconAnimation(1, 50, 0, [0xDEAD_BEEF])]);

        BitConverter.ToUInt32(table, IconAnimationTable.FramesOffset).ShouldBe(0xDEAD_BEEFu);
    }

    [Fact]
    public void WritesNothingForNoAnimations() => IconAnimationTable.Serialise([]).ShouldBeEmpty();

    [Fact]
    public void RefusesMoreFramesThanARecordHolds()
    {
        var frames = Enumerable.Repeat(1u, IconAnimation.MaxFrames + 1).ToList();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            IconAnimationTable.Serialise([new IconAnimation(1, 50, 0, frames)]));
    }

    private static IconAnimation Sample(ushort icon) => new(icon, 50, 0, [1]);
}
