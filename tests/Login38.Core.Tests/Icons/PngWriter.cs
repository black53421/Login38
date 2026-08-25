using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Login38.Core.Tests.Icons;

/// <summary>
/// Writes PNG files for the decoder to read back.
/// </summary>
/// <remarks>
/// Deliberately an independent implementation rather than a mirror of the decoder: it
/// builds chunks and compresses with the framework's own deflate, so a round trip proves
/// the decoder against the format rather than against its own assumptions. The one thing
/// it shares with the decoder is the filter arithmetic, which the fixed-byte tests pin
/// separately.
/// </remarks>
internal static class PngWriter
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Writes an RGBA image with every row unfiltered.</summary>
    public static byte[] Rgba(int width, int height, byte[] pixels) =>
        Write(width, height, colourType: 6, channels: 4, pixels, filter: 0);

    /// <summary>Writes an RGBA image with one filter applied to every row.</summary>
    public static byte[] Rgba(int width, int height, byte[] pixels, byte filter) =>
        Write(width, height, colourType: 6, channels: 4, pixels, filter);

    /// <summary>Writes a three-channel image, which has no alpha at all.</summary>
    public static byte[] Rgb(int width, int height, byte[] pixels) =>
        Write(width, height, colourType: 2, channels: 3, pixels, filter: 0);

    /// <summary>Writes a single-channel greyscale image.</summary>
    public static byte[] Greyscale(int width, int height, byte[] pixels) =>
        Write(width, height, colourType: 0, channels: 1, pixels, filter: 0);

    /// <summary>Writes a greyscale image with an alpha channel.</summary>
    public static byte[] GreyscaleAlpha(int width, int height, byte[] pixels) =>
        Write(width, height, colourType: 4, channels: 2, pixels, filter: 0);

    /// <summary>Writes a paletted image, optionally with per-entry transparency.</summary>
    public static byte[] Palette(int width, int height, byte[] indices, byte[] palette, byte[]? alpha = null)
    {
        var extra = new List<(string Type, byte[] Body)> { ("PLTE", palette) };

        if (alpha is not null)
        {
            extra.Add(("tRNS", alpha));
        }

        return Write(width, height, colourType: 3, channels: 1, indices, filter: 0, extra);
    }

    /// <summary>Writes a header claiming something the decoder does not read.</summary>
    public static byte[] WithHeader(byte bitDepth, byte colourType, byte interlace)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = bitDepth;
        header[9] = colourType;
        header[12] = interlace;

        using var png = new MemoryStream();
        png.Write(Signature);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", Deflate([0, 0, 0, 0, 0]));
        Chunk(png, "IEND", []);

        return png.ToArray();
    }

    private static byte[] Write(
        int width, int height, byte colourType, int channels, byte[] pixels, byte filter,
        List<(string Type, byte[] Body)>? extra = null)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = colourType;

        using var png = new MemoryStream();
        png.Write(Signature);
        Chunk(png, "IHDR", header);

        foreach (var (type, body) in extra ?? [])
        {
            Chunk(png, type, body);
        }

        Chunk(png, "IDAT", Deflate(Filter(width, height, channels, pixels, filter)));
        Chunk(png, "IEND", []);

        return png.ToArray();
    }

    private static byte[] Filter(int width, int height, int channels, byte[] pixels, byte filter)
    {
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];

        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = filter;

            for (var i = 0; i < stride; i++)
            {
                var value = pixels[(y * stride) + i];
                var left = i >= channels ? pixels[(y * stride) + i - channels] : 0;
                var up = y > 0 ? pixels[((y - 1) * stride) + i] : 0;
                var upLeft = y > 0 && i >= channels ? pixels[((y - 1) * stride) + i - channels] : 0;

                raw[(y * (stride + 1)) + 1 + i] = filter switch
                {
                    0 => value,
                    1 => (byte)(value - left),
                    2 => (byte)(value - up),
                    3 => (byte)(value - ((left + up) / 2)),
                    4 => (byte)(value - Paeth(left, up, upLeft)),
                    _ => throw new ArgumentOutOfRangeException(nameof(filter)),
                };
            }
        }

        return raw;
    }

    private static int Paeth(int left, int up, int upLeft)
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

    private static byte[] Deflate(byte[] raw)
    {
        using var compressed = new MemoryStream();

        using (var deflater = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflater.Write(raw);
        }

        return compressed.ToArray();
    }

    /// <summary>Writes a chunk, CRC included — the decoder ignores it, but a real file has one.</summary>
    private static void Chunk(Stream png, string type, ReadOnlySpan<byte> body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        png.Write(length);

        var name = Encoding.ASCII.GetBytes(type);
        png.Write(name);
        png.Write(body);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. name, .. body]));
        png.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;

        foreach (var b in data)
        {
            crc ^= b;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB8_8320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFF_FFFF;
    }
}
