using Login38.Core.Icons;
using Shouldly;

namespace Login38.Core.Tests.Icons;

/// <summary>
/// Covers the PNG subset icon frames are written in.
/// </summary>
/// <remarks>
/// Operators export these from whatever editor they use, so what matters is that every
/// ordinary export reads back exactly, and that the ones that cannot be read say so by name
/// instead of producing a plausible wrong image.
/// </remarks>
public sealed class PngImageTests
{
    [Fact]
    public void ReadsAnRgbaImage()
    {
        byte[] pixels =
        [
            0x10, 0x20, 0x30, 0xFF,   0x40, 0x50, 0x60, 0x80,
            0x70, 0x80, 0x90, 0x00,   0xA0, 0xB0, 0xC0, 0xFF,
        ];

        var image = PngImage.Decode(PngWriter.Rgba(2, 2, pixels));

        image.Width.ShouldBe(2);
        image.Height.ShouldBe(2);
        image.Rgba.ShouldBe(pixels);
    }

    // A row is written as the difference from its neighbours, so the decoder has to undo
    // them in order and read the finished version of the row above. All five have to work:
    // an encoder picks per row, and most pick more than one within a file.
    [Theory]
    [InlineData(0)]   // none
    [InlineData(1)]   // left
    [InlineData(2)]   // above
    [InlineData(3)]   // the average of both
    [InlineData(4)]   // Paeth
    public void UndoesEveryRowFilter(byte filter)
    {
        var pixels = new byte[4 * 4 * 4];

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)((i * 37) + 11);
        }

        PngImage.Decode(PngWriter.Rgba(4, 4, pixels, filter)).Rgba.ShouldBe(pixels);
    }

    // Three channels, so every pixel comes out fully opaque. Without this, an icon exported
    // without an alpha channel would be entirely transparent and draw as nothing.
    [Fact]
    public void TreatsAnImageWithoutAlphaAsOpaque()
    {
        var image = PngImage.Decode(PngWriter.Rgb(2, 1, [0x11, 0x22, 0x33, 0x44, 0x55, 0x66]));

        image.Rgba.ShouldBe([0x11, 0x22, 0x33, 0xFF, 0x44, 0x55, 0x66, 0xFF]);
    }

    [Fact]
    public void ReadsGreyscale()
    {
        var image = PngImage.Decode(PngWriter.Greyscale(3, 1, [0x00, 0x7F, 0xFF]));

        image.Rgba.ShouldBe([0, 0, 0, 0xFF, 0x7F, 0x7F, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
    }

    [Fact]
    public void ReadsGreyscaleWithAlpha()
    {
        var image = PngImage.Decode(PngWriter.GreyscaleAlpha(2, 1, [0x40, 0x00, 0x80, 0xFF]));

        image.Rgba.ShouldBe([0x40, 0x40, 0x40, 0x00, 0x80, 0x80, 0x80, 0xFF]);
    }

    // What a pixel editor produces for a small icon by default. The reference refused these
    // outright, which is the most likely way an operator's first attempt failed.
    [Fact]
    public void ReadsAPalettedImage()
    {
        byte[] palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF];

        var image = PngImage.Decode(PngWriter.Palette(3, 1, [2, 0, 1], palette));

        image.Rgba.ShouldBe(
        [
            0x00, 0x00, 0xFF, 0xFF,
            0xFF, 0x00, 0x00, 0xFF,
            0x00, 0xFF, 0x00, 0xFF,
        ]);
    }

    // The only way a paletted icon can have a transparent background, and the reason
    // paletted support is worth having at all.
    [Fact]
    public void ReadsPalettedTransparency()
    {
        byte[] palette = [0x00, 0x00, 0x00, 0xAB, 0xCD, 0xEF];

        var image = PngImage.Decode(PngWriter.Palette(2, 1, [0, 1], palette, alpha: [0x00]));

        image.Rgba.ShouldBe([0x00, 0x00, 0x00, 0x00, 0xAB, 0xCD, 0xEF, 0xFF]);
    }

    // Chunks the decoder has no use for are between the ones it does in almost every real
    // file — colour profiles, timestamps, the editor's name.
    [Fact]
    public void SkipsChunksItDoesNotUnderstand()
    {
        var png = PngWriter.Rgba(1, 1, [1, 2, 3, 4]).ToList();

        // A tEXt chunk, inserted immediately after the signature and header.
        png.InsertRange(8 + 25, [0, 0, 0, 4, (byte)'t', (byte)'E', (byte)'X', (byte)'t', 1, 2, 3, 4, 0, 0, 0, 0]);

        PngImage.Decode([.. png]).Rgba.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void RefusesSomethingThatIsNotAPng() =>
        Should.Throw<DynamicIconException>(() => PngImage.Decode([0x42, 0x4D, 0x00, 0x00]))
            .Message.ShouldContain("not a PNG");

    // Sixteen-bit and interlaced files are readable images that this does not read. Saying
    // so by name is the difference between a fixable export setting and a mystery.
    [Fact]
    public void RefusesSixteenBitByName() =>
        Should.Throw<DynamicIconException>(() => PngImage.Decode(PngWriter.WithHeader(16, 6, 0)))
            .Message.ShouldContain("8-bit");

    [Fact]
    public void RefusesInterlacingByName() =>
        Should.Throw<DynamicIconException>(() => PngImage.Decode(PngWriter.WithHeader(8, 6, 1)))
            .Message.ShouldContain("interlaced");

    [Fact]
    public void RefusesAColourTypeThatIsNotOne() =>
        Should.Throw<DynamicIconException>(() => PngImage.Decode(PngWriter.WithHeader(8, 7, 0)));

    [Fact]
    public void RefusesAPalettedImageWithNoPalette()
    {
        var png = PngWriter.WithHeader(8, 3, 0);

        Should.Throw<DynamicIconException>(() => PngImage.Decode(png))
            .Message.ShouldContain("palette");
    }

    // A file cut short mid-download or mid-copy. Reading past the end would be an exception
    // from somewhere unrelated.
    [Fact]
    public void RefusesATruncatedFile()
    {
        var png = PngWriter.Rgba(4, 4, new byte[4 * 4 * 4]);

        Should.Throw<DynamicIconException>(() => PngImage.Decode(png.AsSpan(0, png.Length - 20)));
    }

    [Fact]
    public void RefusesDamagedImageData()
    {
        var png = PngWriter.Rgba(4, 4, new byte[4 * 4 * 4]);
        var body = png.AsSpan().IndexOf("IDAT"u8) + 4;

        // The two bytes the compressed stream starts with, which say how it is compressed.
        png[body] = 0xFF;
        png[body + 1] = 0xFF;

        Should.Throw<DynamicIconException>(() => PngImage.Decode(png))
            .Message.ShouldContain("damaged");
    }
}
