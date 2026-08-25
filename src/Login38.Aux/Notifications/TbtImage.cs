namespace Login38.Aux.Notifications;

/// <summary>
/// Reads the client's own item picture format.
/// </summary>
/// <remarks>
/// <para>
/// A four-byte header — two bytes of world alignment, then width and height — and then one
/// run-length encoded row after another. Each row is a count of spans, and each span is a
/// skip, a pixel count, and that many sixteen-bit pixels. The skip is in <em>bytes</em>, so
/// it is half that many pixels: the one detail in the format that is not guessable.
/// </para>
/// <para>
/// Pixels are RGB555 with the top bit unused, and a pixel of zero is transparent rather
/// than black. Each five-bit channel is widened to eight by repeating its top bits, which
/// takes 31 to 255 rather than to 248.
/// </para>
/// <para>
/// The two alignment bytes are ignored. They say where the picture sits relative to the
/// ground when the world draws it, which is not what a square in a list of pickups wants.
/// </para>
/// </remarks>
public static class TbtImage
{
    /// <summary>Two bytes of alignment, then width and height.</summary>
    internal const int HeaderSize = 4;

    /// <summary>How big an icon can be before it is assumed to be something else.</summary>
    /// <remarks>
    /// Both dimensions come out of one byte each, so nothing can claim more than 255 — but
    /// a span may legitimately run past the declared width, and this bounds what that can
    /// grow to.
    /// </remarks>
    internal const int LargestSide = 512;

    /// <summary>
    /// Decodes one, or null if it is not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One walk of the spans rather than two. The reference scans the whole file to find
    /// the width, then scans it again to fill the pixels, with the bounds checks written
    /// out twice — and the second copy carries on past a span it truncated.
    /// </para>
    /// <para>
    /// Running out of file is not an error. Real files in the client declare more rows than
    /// they carry, and the rows that are missing are simply transparent.
    /// </para>
    /// </remarks>
    public static SpriteImage? Decode(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < HeaderSize)
        {
            return null;
        }

        int declared = raw[2];
        int height = raw[3];

        if (declared == 0 || height == 0)
        {
            return null;
        }

        List<Span> spans = [];
        var width = declared;
        var at = HeaderSize;

        for (var row = 0; row < height && at < raw.Length; row++)
        {
            int count = raw[at++];
            var x = 0;

            for (var span = 0; span < count; span++)
            {
                if (at + 2 > raw.Length)
                {
                    return Fill(raw, spans, width, height);
                }

                // The skip is a byte count and a pixel is two bytes wide.
                x += raw[at] / 2;

                int pixels = raw[at + 1];
                at += 2;

                if (at + (pixels * 2) > raw.Length)
                {
                    return Fill(raw, spans, width, height);
                }

                spans.Add(new Span(row, x, at, pixels));
                at += pixels * 2;
                x += pixels;

                // A span is allowed to run past the declared width; real files do.
                width = Math.Min(LargestSide, Math.Max(width, x));
            }
        }

        return Fill(raw, spans, width, height);
    }

    /// <summary>One run of pixels on one row.</summary>
    private readonly record struct Span(int Row, int X, int At, int Pixels);

    private static SpriteImage Fill(ReadOnlySpan<byte> raw, List<Span> spans, int width, int height)
    {
        // Everything not written by a span stays transparent, which covers both a pixel of
        // zero and a row the file never carried.
        var rgba = new byte[width * height * SpriteImage.Channels];

        foreach (var span in spans)
        {
            for (var i = 0; i < span.Pixels; i++)
            {
                var x = span.X + i;

                if (x >= width)
                {
                    break;
                }

                var pixel = BitConverter.ToUInt16(raw[(span.At + (i * 2))..]);

                if (pixel == 0)
                {
                    continue;
                }

                var into = ((span.Row * width) + x) * SpriteImage.Channels;

                rgba[into] = Widen(pixel >> 10);
                rgba[into + 1] = Widen(pixel >> 5);
                rgba[into + 2] = Widen(pixel);
                rgba[into + 3] = byte.MaxValue;
            }
        }

        return new SpriteImage(width, height, rgba);
    }

    /// <summary>Five bits to eight, by repeating the top three.</summary>
    private static byte Widen(int channel)
    {
        var five = channel & 0x1F;

        return (byte)((five << 3) | (five >> 2));
    }
}
