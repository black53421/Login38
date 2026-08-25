using Login38.Core.Icons;
using Shouldly;

namespace Login38.Core.Tests.Icons;

/// <summary>
/// Covers the icon format the client draws from.
/// </summary>
/// <remarks>
/// The proof is a decoder written to match the client's own draw loop rather than to match
/// the encoder: it reads a segment count, halves each skip and copies each run, exactly as
/// the client does. Anything the encoder gets wrong about the format shows up as an image
/// that comes back different, not as an assertion about bytes.
/// </remarks>
public sealed class TbtRawIconTests
{
    [Fact]
    public void WritesTheHeaderTheClientReads()
    {
        var icon = TbtRawIcon.Encode(Opaque(8, 4), 8, 4);

        icon[0].ShouldBe((byte)0);   // no x offset
        icon[1].ShouldBe((byte)0);   // no y offset
        icon[2].ShouldBe((byte)8);
        icon[3].ShouldBe((byte)4);
    }

    // The ordinary case: an icon with transparent corners and colour in the middle.
    [Fact]
    public void RoundTripsThroughTheClientsOwnReading()
    {
        // 4x2. Row 0: transparent, red, green, transparent. Row 1: blue, blue, transparent, white.
        var rgba = new byte[4 * 2 * 4];
        Put(rgba, 1, 0xFF, 0x00, 0x00);
        Put(rgba, 2, 0x00, 0xFF, 0x00);
        Put(rgba, 4, 0x00, 0x00, 0xFF);
        Put(rgba, 5, 0x00, 0x00, 0xFF);
        Put(rgba, 7, 0xFF, 0xFF, 0xFF);

        var drawn = Draw(TbtRawIcon.Encode(rgba, 4, 2));

        drawn[0].ShouldBe([null, Rgb565(0xFF, 0, 0), Rgb565(0, 0xFF, 0), null]);
        drawn[1].ShouldBe([Rgb565(0, 0, 0xFF), Rgb565(0, 0, 0xFF), null, Rgb565(0xFF, 0xFF, 0xFF)]);
    }

    // A fully opaque icon is one run per row, and the shortest the format gets.
    [Fact]
    public void WritesAFullRowAsOneRun()
    {
        var icon = TbtRawIcon.Encode(Opaque(32, 32), 32, 32);

        icon[4].ShouldBe((byte)1);    // one segment
        icon[5].ShouldBe((byte)0);    // skipping nothing
        icon[6].ShouldBe((byte)32);   // thirty-two pixels

        Draw(icon).ShouldAllBe(row => row.All(pixel => pixel.HasValue));
    }

    // The client stops at the segment count, so an empty row costs one byte and a row that
    // ends in transparency costs nothing for the tail.
    [Fact]
    public void WritesNothingForTransparency()
    {
        var empty = TbtRawIcon.Encode(new byte[2 * 1 * 4], 2, 1);

        empty.Length.ShouldBe(5);     // header plus a segment count of zero
        empty[4].ShouldBe((byte)0);
        Draw(empty)[0].ShouldBe([null, null]);

        // One opaque pixel then nothing: the trailing run is not written either.
        var rgba = new byte[4 * 1 * 4];
        Put(rgba, 0, 0x10, 0x20, 0x30);

        TbtRawIcon.Encode(rgba, 4, 1).Length.ShouldBe(4 + 1 + 2 + 2);
    }

    // The client halves the skip because it is stepping through a sixteen-bit buffer. An
    // encoder that wrote pixels would put everything after a gap in the wrong place.
    [Fact]
    public void DoublesTheSkipTheClientHalves()
    {
        var rgba = new byte[5 * 1 * 4];
        Put(rgba, 3, 0xFF, 0xFF, 0xFF);

        var icon = TbtRawIcon.Encode(rgba, 5, 1);

        icon[5].ShouldBe((byte)6);   // three transparent pixels
        Draw(icon)[0][3].ShouldNotBeNull();
    }

    // Anything below the threshold is not drawn at all, because the format has no way to
    // express partial transparency.
    [Theory]
    [InlineData(0x7F, false)]
    [InlineData(0x80, true)]
    public void DrawsAPixelOnlyWhenItIsOpaqueEnough(byte alpha, bool drawn)
    {
        var rgba = new byte[1 * 1 * 4];
        Put(rgba, 0, 0xFF, 0xFF, 0xFF, alpha);

        (Draw(TbtRawIcon.Encode(rgba, 1, 1))[0][0] is not null).ShouldBe(drawn);
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(32, 0)]
    [InlineData(256, 32)]
    [InlineData(32, 256)]
    public void RefusesASizeTheHeaderCannotHold(int width, int height) =>
        Should.Throw<DynamicIconException>(() => TbtRawIcon.Encode(new byte[16], width, height));

    [Fact]
    public void RefusesPixelsThatDoNotMatchTheSize() =>
        Should.Throw<DynamicIconException>(() => TbtRawIcon.Encode(new byte[16], 4, 4));

    // The skip is stored doubled, so half a byte's range is the most one segment can jump.
    // A wide icon with a wide gap has to be refused rather than silently drawn shifted.
    [Fact]
    public void RefusesAGapWiderThanTheFormatHolds()
    {
        var rgba = new byte[200 * 1 * 4];
        Put(rgba, 199, 0xFF, 0xFF, 0xFF);

        Should.Throw<DynamicIconException>(() => TbtRawIcon.Encode(rgba, 200, 1))
            .Message.ShouldContain("transparent");
    }

    /// <summary>Reads an icon the way the client's draw loop does.</summary>
    private static List<ushort?[]> Draw(ReadOnlySpan<byte> icon)
    {
        var width = icon[2];
        var rows = icon[3];
        var image = new List<ushort?[]>();
        var at = 4;

        for (var y = 0; y < rows; y++)
        {
            var row = new ushort?[width];
            var segments = icon[at++];
            var x = 0;

            for (var segment = 0; segment < segments; segment++)
            {
                x += icon[at++] >> 1;
                var run = icon[at++];

                for (var pixel = 0; pixel < run; pixel++)
                {
                    row[x + pixel] = (ushort)(icon[at] | (icon[at + 1] << 8));
                    at += 2;
                }

                x += run;
            }

            image.Add(row);
        }

        return image;
    }

    private static byte[] Opaque(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Array.Fill(rgba, (byte)0xFF);

        return rgba;
    }

    private static void Put(byte[] rgba, int pixel, byte red, byte green, byte blue, byte alpha = 0xFF)
    {
        rgba[pixel * 4] = red;
        rgba[(pixel * 4) + 1] = green;
        rgba[(pixel * 4) + 2] = blue;
        rgba[(pixel * 4) + 3] = alpha;
    }

    private static ushort Rgb565(byte red, byte green, byte blue) =>
        (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
}
