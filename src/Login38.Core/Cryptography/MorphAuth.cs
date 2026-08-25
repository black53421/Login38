using System.Security.Cryptography;

namespace Login38.Core.Cryptography;

/// <summary>
/// Authentication tag that binds a morph <c>.pak</c> to this toolchain.
/// </summary>
/// <remarks>
/// <para>
/// The launcher only loads <c>.pak</c> files produced by the matching encoder. A
/// 16-byte tag is appended to the file and checked on load; anything else is
/// rejected. The file layout is:
/// </para>
/// <code>
/// [originalLength:4][key:16][encrypted:N][tag:16]
///                                        ^^^^^^^ computed over everything before it
/// </code>
/// <para>
/// The threat model is casual substitution — a <c>.pak</c> from a third-party
/// packing tool, or a renamed file dropped in place. It is deliberately not a
/// defence against someone who reverses <see cref="Key"/> out of the binary, and
/// nothing security-critical depends on it.
/// </para>
/// <para>
/// This is a CBC-MAC with a length prefix and ISO 7816-4 padding, not NIST CMAC.
/// The length prefix is what stops the classic CBC-MAC length-extension forgery.
/// Changing <see cref="Key"/> invalidates every previously produced file and
/// requires rebuilding the encoder and the launcher together.
/// </para>
/// </remarks>
public static class MorphAuth
{
    /// <summary>Length of the tag, in bytes.</summary>
    public const int TagLength = 16;

    private const int BlockSize = 16;

    /// <summary>ISO 7816-4 padding marker.</summary>
    private const byte PaddingMarker = 0x80;

    private static ReadOnlySpan<byte> Key =>
    [
        0xC7, 0x9A, 0x14, 0xFE, 0x81, 0x23, 0x6B, 0x4D,
        0xA8, 0x50, 0xE2, 0x9C, 0x3F, 0x17, 0xDA, 0x6B,
    ];

    /// <summary>Computes the tag for <paramref name="data"/>.</summary>
    public static byte[] ComputeTag(ReadOnlySpan<byte> data)
    {
        var tag = new byte[TagLength];
        ComputeTag(data, tag);
        return tag;
    }

    /// <summary>Computes the tag for <paramref name="data"/> into <paramref name="destination"/>.</summary>
    public static void ComputeTag(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, TagLength);

        using var aes = Aes.Create();
        aes.Key = Key.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        Span<byte> state = stackalloc byte[BlockSize];
        Span<byte> block = stackalloc byte[BlockSize];

        // Block 0 is the length, so two inputs of different lengths can never share a
        // prefix chain. Without this a CBC-MAC is trivially forgeable by extension.
        block.Clear();
        BitConverter.TryWriteBytes(block, (ulong)data.Length);
        block[sizeof(ulong)] = PaddingMarker;
        ChainBlock(aes, state, block);

        for (var offset = 0; offset < data.Length; offset += BlockSize)
        {
            var take = Math.Min(BlockSize, data.Length - offset);
            block.Clear();
            data.Slice(offset, take).CopyTo(block);
            if (take < BlockSize)
            {
                block[take] = PaddingMarker;
            }

            ChainBlock(aes, state, block);
        }

        // When the data ends exactly on a block boundary there was nowhere to put the
        // padding marker, so it gets a block of its own.
        if (data.Length > 0 && data.Length % BlockSize == 0)
        {
            block.Clear();
            block[0] = PaddingMarker;
            ChainBlock(aes, state, block);
        }

        state.CopyTo(destination);
    }

    /// <summary>
    /// Verifies a tag in constant time, so a mismatch reveals nothing about where it
    /// diverged.
    /// </summary>
    public static bool VerifyTag(ReadOnlySpan<byte> data, ReadOnlySpan<byte> tag)
    {
        if (tag.Length != TagLength)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[TagLength];
        ComputeTag(data, expected);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }

    private static void ChainBlock(Aes aes, Span<byte> state, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < BlockSize; i++)
        {
            state[i] ^= block[i];
        }

        Span<byte> encrypted = stackalloc byte[BlockSize];
        aes.EncryptEcb(state, encrypted, PaddingMode.None);
        encrypted.CopyTo(state);
    }
}
