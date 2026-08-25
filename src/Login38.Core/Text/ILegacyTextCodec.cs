namespace Login38.Core.Text;

/// <summary>A decoded string together with the encoding it was decoded from.</summary>
/// <param name="Text">The decoded text.</param>
/// <param name="Encoding">Which encoding produced it.</param>
public readonly record struct LegacyString(string Text, LegacyEncoding Encoding)
{
    /// <inheritdoc/>
    public override string ToString() => Text;

    public static implicit operator string(LegacyString value) => value.Text;
}

/// <summary>
/// Converts between .NET strings and the legacy code pages the game speaks.
/// </summary>
/// <remarks>
/// Every string that crosses into or out of game memory goes through here. Using
/// <see cref="System.Text.Encoding.UTF8"/> directly against game bytes produces
/// mojibake, which is how the reference implementation ended up with corrupted
/// literals in its own source.
/// </remarks>
public interface ILegacyTextCodec
{
    /// <summary>The configured strategy for ambiguous input.</summary>
    TextEncodingMode Mode { get; }

    /// <summary>Decodes using <see cref="Mode"/>.</summary>
    LegacyString Decode(ReadOnlySpan<byte> bytes);

    /// <summary>Decodes using an explicit strategy, ignoring <see cref="Mode"/>.</summary>
    LegacyString Decode(ReadOnlySpan<byte> bytes, TextEncodingMode mode);

    /// <summary>
    /// Decodes a NUL-terminated buffer, as read straight out of game memory.
    /// Bytes after the first NUL are ignored.
    /// </summary>
    string DecodeNullTerminated(ReadOnlySpan<byte> bytes);

    /// <summary>Encodes text back to a specific code page, for writing into game memory.</summary>
    byte[] Encode(string text, LegacyEncoding encoding);

    /// <summary>Reads a whole file and decodes it using <see cref="Mode"/>.</summary>
    string ReadTextFile(string path);
}
