namespace Login38.Core.Icons;

/// <summary>
/// Encodes an image the way the client's item icons are stored.
/// </summary>
/// <remarks>
/// <para>
/// The client draws an icon by walking rows and, within a row, alternating between skipping
/// transparent pixels and copying opaque ones straight into a 16-bit frame buffer. So the
/// format is a run-length encoding with the colour already in the frame buffer's own
/// arrangement — there is no palette, no alpha channel, and no conversion at draw time.
/// </para>
/// <para>
/// A header of four bytes: two offsets the client adds to the draw position, the width, and
/// the number of rows. Then one row at a time: a segment count, and per segment a skip, a
/// length and that many colours. A row that is entirely transparent is a segment count of
/// zero, and a run of transparency at the end of a row is simply not written.
/// </para>
/// <para>
/// The skip is stored doubled, because the client halves it — it is counting bytes in a
/// 16-bit buffer, not pixels.
/// </para>
/// </remarks>
public static class TbtRawIcon
{
    /// <summary>The widest and tallest an icon can be, from the header's field widths.</summary>
    public const int MaxSize = 255;

    /// <summary>How opaque a pixel has to be to be drawn at all.</summary>
    /// <remarks>
    /// The format has no partial transparency, so a threshold is the only choice available.
    /// Half way, which puts a soft edge where the eye expects it.
    /// </remarks>
    public const byte DefaultAlphaThreshold = 128;

    private const int BytesPerPixel = 4;

    /// <summary>The longest run of transparency one segment can skip.</summary>
    /// <remarks>Half of a byte's range, because the skip is stored doubled.</remarks>
    private const int MaxSkip = 127;

    /// <summary>Encodes an RGBA image.</summary>
    /// <exception cref="DynamicIconException">
    /// The image is too large for the format, or a run within it is.
    /// </exception>
    public static byte[] Encode(
        ReadOnlySpan<byte> rgba, int width, int height, byte alphaThreshold = DefaultAlphaThreshold)
    {
        if (width is < 1 or > MaxSize || height is < 1 or > MaxSize)
        {
            throw new DynamicIconException(
                $"An icon frame is {width}x{height}; it has to be between 1x1 and {MaxSize}x{MaxSize}, and 32x32 is what the client expects.");
        }

        var expected = width * height * BytesPerPixel;

        if (rgba.Length != expected)
        {
            throw new DynamicIconException(
                $"An icon frame of {width}x{height} needs {expected} bytes of RGBA but has {rgba.Length}.");
        }

        var icon = new List<byte> { 0, 0, (byte)width, (byte)height };
        var row = new List<byte>();

        for (var y = 0; y < height; y++)
        {
            row.Clear();
            var segments = 0;
            var x = 0;

            while (x < width)
            {
                var transparentFrom = x;

                while (x < width && rgba[(((y * width) + x) * BytesPerPixel) + 3] < alphaThreshold)
                {
                    x++;
                }

                // Transparency running to the end of a row says nothing the client needs:
                // it stops at the segment count and leaves the rest of the row untouched.
                if (x == width)
                {
                    break;
                }

                var skip = x - transparentFrom;
                var opaqueFrom = x;

                while (x < width && rgba[(((y * width) + x) * BytesPerPixel) + 3] >= alphaThreshold)
                {
                    x++;
                }

                if (skip > MaxSkip)
                {
                    throw new DynamicIconException(
                        $"An icon frame has {skip} transparent pixels in a row on line {y}; the format holds {MaxSkip}.");
                }

                row.Add((byte)(skip * 2));
                row.Add((byte)(x - opaqueFrom));

                for (var pixel = opaqueFrom; pixel < x; pixel++)
                {
                    var at = ((y * width) + pixel) * BytesPerPixel;
                    var colour = Rgb565(rgba[at], rgba[at + 1], rgba[at + 2]);

                    row.Add((byte)colour);
                    row.Add((byte)(colour >> 8));
                }

                // Not reachable while a segment covers at least one pixel and the row is at
                // most 255 wide, but the count is a byte and the width limit is the only
                // thing keeping it one.
                if (++segments > byte.MaxValue)
                {
                    throw new DynamicIconException(
                        $"An icon frame has more than {byte.MaxValue} runs on line {y}.");
                }
            }

            icon.Add((byte)segments);
            icon.AddRange(row);
        }

        return [.. icon];
    }

    /// <summary>Packs a colour the way the client's frame buffer holds one.</summary>
    private static ushort Rgb565(byte red, byte green, byte blue) =>
        (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
}
