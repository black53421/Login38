using System.Security.Cryptography;
using System.Text;

namespace Login38.Core.Configuration;

/// <summary>
/// The obfuscation scheme used by <c>list.txt</c> and <c>config.ini</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two passes, in this order when encrypting: XOR every byte against a 256-byte
/// table, then AES-128-ECB every <em>complete</em> 16-byte block. A trailing partial
/// block is left XOR-only. Decryption reverses both.
/// </para>
/// <para>
/// The table is a fixed constant from the original client's <c>configenc.cpp</c>,
/// mixed with the file key so different keys produce different tables. The byte
/// ordering, the "whole blocks only" rule and the key itself all have to match the
/// original exactly, or existing <c>list.txt</c> files stop loading — this is an
/// interoperability format, not a security boundary. ECB and a hard-coded key are
/// what the format specifies.
/// </para>
/// </remarks>
public static class ConfigCipher
{
    /// <summary>The fixed key every <c>list.txt</c> is encrypted with.</summary>
    public static ReadOnlySpan<byte> FileKey => "4zF8sAc5bYkCRM3w"u8;

    /// <summary>Marks a config file as using this scheme, followed by base64.</summary>
    public const string EncryptedTextPrefix = "ENC1:";

    private const int BlockSize = 16;
    private const int TableSize = 256;

    /// <summary>Encrypts <paramref name="data"/> in place.</summary>
    public static void Encrypt(ReadOnlySpan<byte> key, Span<byte> data)
    {
        ApplyXorTable(key, data);
        TransformWholeBlocks(key, data, encrypt: true);
    }

    /// <summary>Decrypts <paramref name="data"/> in place.</summary>
    public static void Decrypt(ReadOnlySpan<byte> key, Span<byte> data)
    {
        TransformWholeBlocks(key, data, encrypt: false);
        ApplyXorTable(key, data);
    }

    /// <summary>
    /// Runs ECB over the block-aligned prefix of <paramref name="data"/>, leaving any
    /// trailing partial block untouched.
    /// </summary>
    private static void TransformWholeBlocks(ReadOnlySpan<byte> key, Span<byte> data, bool encrypt)
    {
        var alignedLength = data.Length - data.Length % BlockSize;
        if (alignedLength == 0)
        {
            return;
        }

        var region = data[..alignedLength];

        // Transformed into a separate buffer and copied back: overlapping source and
        // destination spans are not a documented guarantee of the Aes API.
        var transformed = new byte[alignedLength];
        using var aes = CreateAes(key);

        if (encrypt)
        {
            aes.EncryptEcb(region, transformed, PaddingMode.None);
        }
        else
        {
            aes.DecryptEcb(region, transformed, PaddingMode.None);
        }

        transformed.CopyTo(region);
    }

    /// <summary>Encrypts config text and wraps it as <c>ENC1:&lt;base64&gt;</c>.</summary>
    public static string EncryptText(string plaintext)
    {
        var buffer = Encoding.UTF8.GetBytes(plaintext);
        Encrypt(FileKey, buffer);
        // LF, not Environment.NewLine: the file is byte-compared by other tools.
        return $"{EncryptedTextPrefix}{Convert.ToBase64String(buffer)}\n";
    }

    /// <summary>
    /// Decrypts config text. Plain INI is passed through unchanged so that config
    /// files written before encryption was introduced still load.
    /// </summary>
    /// <exception cref="FormatException">The content is neither form.</exception>
    public static string DecryptText(string content)
    {
        var trimmed = content.TrimStart();

        if (trimmed.StartsWith(EncryptedTextPrefix, StringComparison.Ordinal))
        {
            // Whitespace is stripped because an editor may have wrapped the payload
            // across lines. Padding '=' is significant and must survive.
            var base64 = new StringBuilder(trimmed.Length - EncryptedTextPrefix.Length);
            foreach (var c in trimmed.AsSpan(EncryptedTextPrefix.Length))
            {
                if (!char.IsWhiteSpace(c))
                {
                    base64.Append(c);
                }
            }

            byte[] buffer;
            try
            {
                buffer = Convert.FromBase64String(base64.ToString());
            }
            catch (FormatException e)
            {
                throw new FormatException("Encrypted config payload is not valid base64.", e);
            }

            Decrypt(FileKey, buffer);
            return Encoding.UTF8.GetString(buffer);
        }

        if (trimmed.StartsWith('['))
        {
            return content;
        }

        throw new FormatException(
            "Config content is neither an encrypted payload nor a plain INI document.");
    }

    private static Aes CreateAes(ReadOnlySpan<byte> key)
    {
        var aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        return aes;
    }

    /// <summary>
    /// XORs each byte against the key-mixed table. The table repeats every 256 bytes
    /// and the key every 16, exactly as the original implementation does.
    /// </summary>
    private static void ApplyXorTable(ReadOnlySpan<byte> key, Span<byte> data)
    {
        Span<byte> table = stackalloc byte[TableSize];
        BaseTable.CopyTo(table);
        for (var i = 0; i < TableSize; i++)
        {
            table[i] ^= key[i % key.Length];
        }

        for (var i = 0; i < data.Length; i++)
        {
            data[i] ^= table[i % TableSize];
        }
    }

    /// <summary>Verbatim from the original client's <c>configenc.cpp</c>.</summary>
    private static ReadOnlySpan<byte> BaseTable =>
    [
        0x7E, 0x89, 0xDC, 0x78, 0x7F, 0x4B, 0xB6, 0x4F, 0x7D, 0x0D, 0x08, 0x16, 0x7C, 0xCF, 0x62, 0x21,
        0x79, 0x80, 0x74, 0xA4, 0x78, 0x42, 0x1E, 0x93, 0x7A, 0x04, 0xA0, 0xCA, 0x7B, 0xC6, 0xCA, 0xFD,
        0x6C, 0xBC, 0x2E, 0xB0, 0x6D, 0x7E, 0x44, 0x87, 0x6F, 0x38, 0xFA, 0xDE, 0x6E, 0xFA, 0x90, 0xE9,
        0x6B, 0xB5, 0x86, 0x6C, 0x6A, 0x77, 0xEC, 0x5B, 0x68, 0x31, 0x52, 0x02, 0x69, 0xF3, 0x38, 0x35,
        0x62, 0xAF, 0x7F, 0x08, 0x63, 0x6D, 0x15, 0x3F, 0x61, 0x2B, 0xAB, 0x66, 0x60, 0xE9, 0xC1, 0x51,
        0x65, 0xA6, 0xD7, 0xD4, 0x64, 0x64, 0xBD, 0xE3, 0x66, 0x22, 0x03, 0xBA, 0x67, 0xE0, 0x69, 0x8D,
        0x48, 0xD7, 0xCB, 0x20, 0x49, 0x15, 0xA1, 0x17, 0x4B, 0x53, 0x1F, 0x4E, 0x4A, 0x91, 0x75, 0x79,
        0x4F, 0xDE, 0x63, 0xFC, 0x4E, 0x1C, 0x09, 0xCB, 0x4C, 0x5A, 0xB7, 0x92, 0x4D, 0x98, 0xDD, 0xA5,
        0x46, 0xC4, 0x9A, 0x98, 0x47, 0x06, 0xF0, 0xAF, 0x45, 0x40, 0x4E, 0xF6, 0x44, 0x82, 0x24, 0xC1,
        0x41, 0xCD, 0x32, 0x44, 0x40, 0x0F, 0x58, 0x73, 0x42, 0x49, 0xE6, 0x2A, 0x43, 0x8B, 0x8C, 0x1D,
        0x54, 0xF1, 0x68, 0x50, 0x55, 0x33, 0x02, 0x67, 0x57, 0x75, 0xBC, 0x3E, 0x56, 0xB7, 0xD6, 0x09,
        0x53, 0xF8, 0xC0, 0x8C, 0x52, 0x3A, 0xAA, 0xBB, 0x50, 0x7C, 0x14, 0xE2, 0x51, 0xBE, 0x7E, 0xD5,
        0x5A, 0xE2, 0x39, 0xE8, 0x5B, 0x20, 0x53, 0xDF, 0x59, 0x66, 0xED, 0x86, 0x58, 0xA4, 0x87, 0xB1,
        0x5D, 0xEB, 0x91, 0x34, 0x5C, 0x29, 0xFB, 0x03, 0x5E, 0x6F, 0x45, 0x5A, 0x5F, 0xAD, 0x2F, 0x6D,
        0xE1, 0x35, 0x1B, 0x80, 0xE0, 0xF7, 0x71, 0xB7, 0xE2, 0xB1, 0xCF, 0xEE, 0xE3, 0x73, 0xA5, 0xD9,
        0xE6, 0x3C, 0xB3, 0x5C, 0xE7, 0xFE, 0xD9, 0x6B, 0xE5, 0xB8, 0x67, 0x32, 0xE4, 0x7A, 0x0D, 0x05,
    ];
}
