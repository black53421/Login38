using Login38.Aux.Notifications;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers the client's own item picture format.
/// </summary>
public sealed class TbtImageTests
{
    [Fact]
    public void ReadsASolidRow()
    {
        // 2x1, one span of two pixels: red then blue.
        var image = TbtImage.Decode([0, 0, 2, 1, 1, 0, 2, .. Pixel(31, 0, 0), .. Pixel(0, 0, 31)])!;

        image.Width.ShouldBe(2);
        image.Height.ShouldBe(1);
        image.Rgba.ShouldBe([255, 0, 0, 255, 0, 0, 255, 255]);
    }

    // The skip is a byte count and a pixel is two bytes wide, so the offset is half of it.
    // This is the one thing about the format that cannot be guessed from the data.
    [Fact]
    public void CountsTheSkipInBytesRatherThanPixels()
    {
        // 3x1, one span skipping 4 bytes = 2 pixels, then one pixel.
        var image = TbtImage.Decode([0, 0, 3, 1, 1, 4, 1, .. Pixel(31, 31, 31)])!;

        image.Rgba[..8].ShouldBe([0, 0, 0, 0, 0, 0, 0, 0]);
        image.Rgba[8..].ShouldBe([255, 255, 255, 255]);
    }

    // Zero is transparent, not black. A picture whose background were black would otherwise
    // be a black square.
    [Fact]
    public void TreatsAPixelOfZeroAsNothingAtAll() =>
        TbtImage.Decode([0, 0, 1, 1, 1, 0, 1, 0, 0])!.Rgba.ShouldBe([0, 0, 0, 0]);

    // 31 has to reach 255, not 248, or everything the client draws is slightly dark.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 8)]
    [InlineData(16, 132)]
    [InlineData(31, 255)]
    public void WidensFiveBitsToEightByRepeatingThem(int five, int expected) =>
        TbtImage.Decode([0, 0, 1, 1, 1, 0, 1, .. Pixel(five, five, five)])!
            .Rgba[..3].ShouldBe([(byte)expected, (byte)expected, (byte)expected]);

    [Fact]
    public void KeepsTheChannelsInTheRightOrder()
    {
        var image = TbtImage.Decode([0, 0, 1, 1, 1, 0, 1, .. Pixel(31, 16, 0)])!;

        image.Rgba.ShouldBe([255, 132, 0, 255]);
    }

    [Fact]
    public void ReadsMoreThanOneRow()
    {
        byte[] raw =
        [
            0, 0, 1, 2,
            1, 0, 1, .. Pixel(31, 0, 0),
            1, 0, 1, .. Pixel(0, 31, 0),
        ];

        var image = TbtImage.Decode(raw)!;

        image.Height.ShouldBe(2);
        image.Rgba.ShouldBe([255, 0, 0, 255, 0, 255, 0, 255]);
    }

    // Real files in the client declare more rows than they carry — one in the reference's
    // own notes declares thirty and holds twenty-three.
    [Fact]
    public void LeavesTheRowsAFileRanOutBeforeTransparent()
    {
        var image = TbtImage.Decode([0, 0, 1, 4, 1, 0, 1, .. Pixel(31, 31, 31)])!;

        image.Height.ShouldBe(4);
        image.Rgba.Length.ShouldBe(4 * 4);
        image.Rgba[4..].ShouldAllBe(b => b == 0);
    }

    // The declared width is a floor rather than a limit.
    [Fact]
    public void WidensThePictureForASpanThatRunsPastTheDeclaredWidth()
    {
        var image = TbtImage.Decode([0, 0, 1, 1, 1, 0, 3, .. Pixel(31, 0, 0), .. Pixel(31, 0, 0), .. Pixel(31, 0, 0)])!;

        image.Width.ShouldBe(3);
    }

    [Fact]
    public void SurvivesASpanHeaderCutInHalf() =>
        Should.NotThrow(() => TbtImage.Decode([0, 0, 2, 1, 1, 0]));

    [Fact]
    public void SurvivesPixelsCutInHalf() =>
        TbtImage.Decode([0, 0, 2, 1, 1, 0, 2, 0xFF])!.Rgba.ShouldAllBe(b => b == 0);

    [Fact]
    public void RefusesSomethingTooShortToBeAHeader() => TbtImage.Decode([0, 0, 1]).ShouldBeNull();

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void RefusesAPictureWithNoSize(int width, int height) =>
        TbtImage.Decode([0, 0, (byte)width, (byte)height]).ShouldBeNull();

    // The first two header bytes say where the picture sits relative to the ground in the
    // world, which is not what a square in a list of pickups wants.
    [Fact]
    public void IgnoresTheWorldAlignment()
    {
        var aligned = TbtImage.Decode([40, 60, 1, 1, 1, 0, 1, .. Pixel(31, 0, 0)])!;
        var plain = TbtImage.Decode([0, 0, 1, 1, 1, 0, 1, .. Pixel(31, 0, 0)])!;

        aligned.Width.ShouldBe(plain.Width);
        aligned.Height.ShouldBe(plain.Height);
        aligned.Rgba.ShouldBe(plain.Rgba);
        aligned.Width.ShouldBe(1);
        aligned.Height.ShouldBe(1);
    }

    [Fact]
    public void ProducesAWholeImage() =>
        TbtImage.Decode([0, 0, 3, 2, 1, 0, 1, .. Pixel(31, 0, 0), 0])!.IsWholeImage.ShouldBeTrue();

    /// <summary>An RGB555 pixel, low byte first.</summary>
    private static byte[] Pixel(int red, int green, int blue)
    {
        var packed = (ushort)(((red & 0x1F) << 10) | ((green & 0x1F) << 5) | (blue & 0x1F));

        return BitConverter.GetBytes(packed);
    }
}
