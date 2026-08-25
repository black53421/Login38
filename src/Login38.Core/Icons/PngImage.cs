using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Login38.Core.Icons;

/// <summary>
/// A decoded image, as eight-bit RGBA in rows from the top.
/// </summary>
/// <remarks>
/// <para>
/// Decoded here rather than by an imaging library because of what this is for: icon frames
/// are a handful of small images read once at launch, and the only thing that happens to
/// them afterwards is a conversion into the client's own format. A decoder for the subset
/// PNG files are actually written in is smaller than the dependency, has no platform
/// requirements, and can say precisely which part of a file it could not use.
/// </para>
/// <para>
/// The subset is eight bits a channel, not interlaced, in any of the five colour types.
/// That is what image editors produce for a 32x32 icon. Sixteen-bit and interlaced files
/// are refused by name rather than misread — the reference refused paletted and greyscale
/// files too, which is most of what an operator gets from a pixel editor by default.
/// </para>
/// </remarks>
public sealed record PngImage(int Width, int Height, byte[] Rgba)
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int ChunkHeader = 8;   // length and type
    private const int ChunkFooter = 4;   // CRC

    private const int BytesPerPixel = 4;

    /// <summary>Decodes a PNG.</summary>
    /// <exception cref="DynamicIconException">
    /// The file is not a PNG, is damaged, or is in a form this does not read.
    /// </exception>
    public static PngImage Decode(ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(Signature))
        {
            throw new DynamicIconException("An icon frame is not a PNG file.");
        }

        var header = default(PngHeader);
        var seenHeader = false;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;

        using var compressed = new MemoryStream();

        var at = Signature.Length;

        while (at + ChunkHeader <= png.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[at..]));
            var type = Encoding.ASCII.GetString(png.Slice(at + 4, 4));
            var body = at + ChunkHeader;

            if (body + length + ChunkFooter > png.Length)
            {
                throw new DynamicIconException($"An icon frame ends in the middle of its {type} chunk.");
            }

            var data = png.Slice(body, length);

            switch (type)
            {
                case "IHDR":
                    header = PngHeader.Read(data);
                    seenHeader = true;
                    break;

                case "PLTE":
                    palette = data.ToArray();
                    break;

                case "tRNS":
                    paletteAlpha = data.ToArray();
                    break;

                case "IDAT":
                    compressed.Write(data);
                    break;

                case "IEND":
                    return Build(header, seenHeader, compressed, palette, paletteAlpha);

                default:
                    // Everything else is metadata: colour profiles, text, timestamps.
                    break;
            }

            at = body + length + ChunkFooter;
        }

        throw new DynamicIconException("An icon frame has no IEND chunk; the file is truncated.");
    }

    private static PngImage Build(
        PngHeader header, bool seenHeader, MemoryStream compressed, byte[]? palette, byte[]? paletteAlpha)
    {
        if (!seenHeader)
        {
            throw new DynamicIconException("An icon frame has no IHDR chunk.");
        }

        if (header.ColourType == PngColourType.Palette && palette is null)
        {
            throw new DynamicIconException("An icon frame is paletted but carries no palette.");
        }

        var channels = header.Channels;
        var stride = header.Width * channels;
        var raw = Inflate(compressed, (stride + 1) * header.Height);

        Unfilter(raw, stride, header.Height, channels);

        return new PngImage(header.Width, header.Height, ToRgba(raw, header, stride, palette, paletteAlpha));
    }

    private static byte[] Inflate(MemoryStream compressed, int expected)
    {
        compressed.Position = 0;

        var raw = new byte[expected];

        try
        {
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            inflater.ReadExactly(raw);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            throw new DynamicIconException("An icon frame's image data is damaged.", e);
        }

        return raw;
    }

    /// <summary>
    /// Removes the per-row filters, in place.
    /// </summary>
    /// <remarks>
    /// Each row is written as the difference from something already decoded — the pixel to
    /// its left, the row above, or a blend of both. So rows have to be undone in order, and
    /// each one reads the finished version of the one before it.
    /// </remarks>
    private static void Unfilter(Span<byte> raw, int stride, int height, int channels)
    {
        for (var y = 0; y < height; y++)
        {
            var start = y * (stride + 1);
            var filter = raw[start];
            var row = raw.Slice(start + 1, stride);
            var above = y == 0 ? default : raw.Slice(((y - 1) * (stride + 1)) + 1, stride);

            for (var i = 0; i < stride; i++)
            {
                var left = i >= channels ? row[i - channels] : (byte)0;
                var up = above.IsEmpty ? (byte)0 : above[i];
                var upLeft = !above.IsEmpty && i >= channels ? above[i - channels] : (byte)0;

                row[i] = filter switch
                {
                    0 => row[i],
                    1 => (byte)(row[i] + left),
                    2 => (byte)(row[i] + up),
                    3 => (byte)(row[i] + ((left + up) / 2)),
                    4 => (byte)(row[i] + Paeth(left, up, upLeft)),
                    _ => throw new DynamicIconException(
                        $"An icon frame uses filter {filter} on row {y}, which is not one of the five PNG filters."),
                };
            }
        }
    }

    /// <summary>Picks whichever neighbour the gradient of the other two points at.</summary>
    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);

        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }

        return toUp <= toUpLeft ? up : upLeft;
    }

    private static byte[] ToRgba(
        ReadOnlySpan<byte> raw, PngHeader header, int stride, byte[]? palette, byte[]? paletteAlpha)
    {
        var rgba = new byte[header.Width * header.Height * BytesPerPixel];

        for (var y = 0; y < header.Height; y++)
        {
            var row = raw.Slice((y * (stride + 1)) + 1, stride);

            for (var x = 0; x < header.Width; x++)
            {
                var to = (((y * header.Width) + x) * BytesPerPixel);
                var from = x * header.Channels;

                switch (header.ColourType)
                {
                    case PngColourType.Greyscale:
                        Set(rgba, to, row[from], row[from], row[from], 0xFF);
                        break;

                    case PngColourType.Rgb:
                        Set(rgba, to, row[from], row[from + 1], row[from + 2], 0xFF);
                        break;

                    case PngColourType.Palette:
                        var index = row[from];
                        var entry = index * 3;

                        if (entry + 2 >= palette!.Length)
                        {
                            throw new DynamicIconException(
                                $"An icon frame uses palette entry {index}, which its palette does not have.");
                        }

                        Set(rgba, to, palette[entry], palette[entry + 1], palette[entry + 2],
                            paletteAlpha is not null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)0xFF);
                        break;

                    case PngColourType.GreyscaleAlpha:
                        Set(rgba, to, row[from], row[from], row[from], row[from + 1]);
                        break;

                    case PngColourType.Rgba:
                        Set(rgba, to, row[from], row[from + 1], row[from + 2], row[from + 3]);
                        break;

                    default:
                        throw new DynamicIconException($"An icon frame has colour type {(int)header.ColourType}.");
                }
            }
        }

        return rgba;
    }

    private static void Set(byte[] rgba, int at, byte red, byte green, byte blue, byte alpha)
    {
        rgba[at] = red;
        rgba[at + 1] = green;
        rgba[at + 2] = blue;
        rgba[at + 3] = alpha;
    }

    private enum PngColourType : byte
    {
        Greyscale = 0,
        Rgb = 2,
        Palette = 3,
        GreyscaleAlpha = 4,
        Rgba = 6,
    }

    private readonly record struct PngHeader(int Width, int Height, PngColourType ColourType)
    {
        public int Channels => ColourType switch
        {
            PngColourType.Greyscale or PngColourType.Palette => 1,
            PngColourType.GreyscaleAlpha => 2,
            PngColourType.Rgb => 3,
            _ => 4,
        };

        public static PngHeader Read(ReadOnlySpan<byte> data)
        {
            const int Length = 13;

            if (data.Length < Length)
            {
                throw new DynamicIconException("An icon frame's IHDR chunk is too short to be one.");
            }

            var width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
            var height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));

            if (width <= 0 || height <= 0)
            {
                throw new DynamicIconException($"An icon frame says it is {width}x{height}.");
            }

            if (data[8] != 8)
            {
                throw new DynamicIconException(
                    $"An icon frame is {data[8]} bits a channel; save it as 8-bit.");
            }

            if (data[12] != 0)
            {
                throw new DynamicIconException(
                    "An icon frame is interlaced; save it without Adam7 interlacing.");
            }

            if (!Enum.IsDefined((PngColourType)data[9]))
            {
                throw new DynamicIconException($"An icon frame has colour type {data[9]}, which is not a PNG one.");
            }

            return new PngHeader(width, height, (PngColourType)data[9]);
        }
    }
}
