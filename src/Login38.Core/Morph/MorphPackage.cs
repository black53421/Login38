using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Login38.Core.Configuration;
using Login38.Core.Cryptography;

namespace Login38.Core.Morph;

/// <summary>A morph table that could not be read.</summary>
public sealed class MorphPackageException : Exception
{
    public MorphPackageException(string message) : base(message)
    {
    }

    public MorphPackageException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public MorphPackageException()
    {
    }
}

/// <summary>
/// The encrypted morph table the launcher feeds to the client.
/// </summary>
/// <remarks>
/// <para>
/// The table lists which sprite each appearance-changing item maps to. Operators edit it as
/// text and publish it as a <c>.pak</c>, which exists to stop players reading the sprite
/// list for content that has not been released yet — not to stop a determined reverser,
/// which nothing here would.
/// </para>
/// <para>
/// The body is scrambled the same way the operator's config files are — see
/// <see cref="ConfigCipher"/> — so this type is only the container around it: the layout,
/// the tag, and the compression the encoder applies when it helps. The reference carried a
/// second copy of that cipher, fixed table and all, in the file that loads morph tables.
/// </para>
/// <para>
/// The layout is a length, a key, the body, and a tag:
/// </para>
/// <code>
/// plainSize : u32     only ever sanity-checked
/// key       : 16      the AES key, in the clear
/// body      : n       AES-ECB, then XOR, then usually zlib
/// tag       : 16      over everything before it
/// </code>
/// <para>
/// The key travels with the file, so this is obfuscation rather than encryption. The tag is
/// the part that matters: it is what says the file came from the operator's own encoder
/// rather than from a third-party tool that guessed the format, and a file that fails it is
/// refused rather than handed to the client.
/// </para>
/// </remarks>
public static class MorphPackage
{
    /// <summary>Marks a buffer as the plain morph table the client expects.</summary>
    /// <remarks>
    /// The client reads this byte first and stops if it is missing, so every path out of
    /// here produces it — including the one that reads a plain <c>.txt</c> and never
    /// decrypts anything.
    /// </remarks>
    public const byte Marker = (byte)'S';

    private const int PlainSizeLength = sizeof(uint);

    private const int KeyLength = 16;

    private const int HeaderLength = PlainSizeLength + KeyLength;

    /// <summary>The smallest file that could be a package at all.</summary>
    public static int MinimumLength => HeaderLength + MorphAuth.TagLength;

    /// <summary>
    /// The largest plain size a header may claim.
    /// </summary>
    /// <remarks>
    /// A real table is tens of kilobytes. This is here so a corrupt header is rejected on
    /// the spot rather than after an allocation the machine cannot serve.
    /// </remarks>
    private const uint MaximumPlainSize = 100_000_000;

    /// <summary>Reads a morph table from disk, decrypting it if it needs it.</summary>
    /// <param name="path">A <c>.pak</c> from the encoder, or a plain <c>.txt</c>.</param>
    /// <exception cref="MorphPackageException">Not a morph table this launcher will use.</exception>
    public static byte[] Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var raw = File.ReadAllBytes(path);

        return Path.GetExtension(path).Equals(".pak", StringComparison.OrdinalIgnoreCase)
            ? Decrypt(raw)
            : [Marker, .. raw];
    }

    /// <summary>
    /// Whether a file is a package this launcher will accept.
    /// </summary>
    /// <remarks>
    /// Used to decide whether to turn the feature on at all, so it answers false for
    /// anything unreadable rather than throwing. <see cref="Decrypt(ReadOnlySpan{byte})"/>
    /// is what explains why, once the decision to use the file has been made.
    /// </remarks>
    public static bool IsPackage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return IsPackage(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc cref="IsPackage(string)"/>
    public static bool IsPackage(ReadOnlySpan<byte> raw) =>
        raw.Length >= MinimumLength &&
        PlainSize(raw) <= MaximumPlainSize &&
        MorphAuth.VerifyTag(raw[..^MorphAuth.TagLength], raw[^MorphAuth.TagLength..]);

    /// <summary>Turns a package back into the plain table, marker byte and all.</summary>
    /// <exception cref="MorphPackageException">Not a package from the encoder.</exception>
    public static byte[] Decrypt(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < MinimumLength)
        {
            throw new MorphPackageException(
                $"A morph package is at least {MinimumLength} bytes; this one is {raw.Length}.");
        }

        var body = raw[..^MorphAuth.TagLength];

        // Checked before anything is decrypted or inflated: past this point the file is
        // treated as the operator's own, and inflating arbitrary bytes is not that.
        if (!MorphAuth.VerifyTag(body, raw[^MorphAuth.TagLength..]))
        {
            throw new MorphPackageException(
                "This morph package was not produced by the encoder that goes with this launcher. Rebuild it.");
        }

        var plainSize = PlainSize(raw);

        if (plainSize > MaximumPlainSize)
        {
            throw new MorphPackageException(
                $"The morph package claims a plain size of {plainSize} bytes, which is not credible.");
        }

        var key = raw.Slice(PlainSizeLength, KeyLength);
        var payload = body[HeaderLength..].ToArray();

        // The same scheme the operator's config files use, and the same fixed table. The
        // reference carried a second copy of that table here; there is only one.
        ConfigCipher.Decrypt(key, payload);

        // The encoder compresses only when it helps, and what says which it did is whether
        // the first plain byte is still there.
        return payload.Length > 0 && payload[0] == Marker ? payload : Inflate(payload);
    }

    private static byte[] Inflate(byte[] payload)
    {
        try
        {
            using var source = new MemoryStream(payload, writable: false);
            using var inflate = new ZLibStream(source, CompressionMode.Decompress);
            using var plain = new MemoryStream();

            inflate.CopyTo(plain);
            return plain.ToArray();
        }
        catch (InvalidDataException e)
        {
            throw new MorphPackageException(
                "The morph package decrypted to something that is neither a table nor compressed data.", e);
        }
    }

    private static uint PlainSize(ReadOnlySpan<byte> raw) => BinaryPrimitives.ReadUInt32LittleEndian(raw);

    /// <summary>
    /// Packs a plain morph table into a package the launcher will accept.
    /// </summary>
    /// <param name="table">The table as the operator wrote it, without the marker byte.</param>
    /// <param name="compress">Whether to deflate; the encoder's default is to try.</param>
    /// <remarks>
    /// Here rather than in the encoder so that the two halves of the format are written
    /// together and can be round-tripped in one test. A format whose reader and writer live
    /// in different programs is a format that drifts.
    /// </remarks>
    public static byte[] Encrypt(ReadOnlySpan<byte> table, bool compress = true)
    {
        byte[] plain = [Marker, .. table];
        var payload = compress ? Deflate(plain) : plain;

        // Not the reference's seeded generator. The key is written into the header in the
        // clear, so its quality changes nothing about the file's secrecy — but a real one
        // costs nothing and removes the question.
        var key = RandomNumberGenerator.GetBytes(KeyLength);
        ConfigCipher.Encrypt(key, payload);

        var package = new byte[HeaderLength + payload.Length + MorphAuth.TagLength];
        BinaryPrimitives.WriteUInt32LittleEndian(package, (uint)plain.Length);
        key.CopyTo(package.AsSpan(PlainSizeLength));
        payload.CopyTo(package.AsSpan(HeaderLength));

        var body = package.AsSpan(0, HeaderLength + payload.Length);
        MorphAuth.ComputeTag(body, package.AsSpan(body.Length));

        return package;
    }

    private static byte[] Deflate(byte[] plain)
    {
        using var compressed = new MemoryStream();

        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(plain);
        }

        return compressed.ToArray();
    }
}
