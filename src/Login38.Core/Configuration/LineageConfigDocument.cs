using System.Buffers.Binary;

namespace Login38.Core.Configuration;

/// <summary>
/// The client's own <c>lineage.cfg</c>, as an editable document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the launcher touches this file at all.</b> On Windows 11, when the client
/// starts on its fullscreen path its internal display state is latched. Switching to
/// windowed from the in-game settings menu then crashes inside an ntdll heap check.
/// Writing the desired mode into the config before launch means DirectDraw takes the
/// right path from the start and the mode switch never happens.
/// </para>
/// <para>
/// <b>Format</b>, reverse-engineered from the parser at <c>0x444520</c>: a 28-byte
/// header, then a stream of tag-length-value records terminated by key
/// <c>0xFFFFFFFF</c>. Keys below <c>0x2710</c> are <c>key:u32, size:u32, value</c>;
/// keys at or above it are strings written as <c>key:u32</c> followed by
/// NUL-terminated text with no length prefix.
/// </para>
/// <para>
/// Values are edited in place and never resized, so records this launcher does not
/// understand pass through untouched.
/// </para>
/// </remarks>
public sealed class LineageConfigDocument
{
    /// <summary>Fullscreen flag, one byte. Mirrored into game memory at <c>0x9A84D0</c>.</summary>
    public const uint FullScreenKey = 0x12;

    /// <summary>Window size, four bytes. Mirrored at <c>0x963E54</c>.</summary>
    public const uint WindowModeKey = 0x1A;

    /// <summary>Previous window size, four bytes. Mirrored at <c>0x963E58</c>.</summary>
    public const uint PreviousWindowModeKey = 0x1B;

    private const int HeaderLength = 0x1C;
    private const uint Terminator = 0xFFFF_FFFF;

    /// <summary>Keys at or above this are NUL-terminated strings with no size field.</summary>
    private const uint StringKeyThreshold = 0x2710;

    /// <summary>
    /// Sanity bound on a record's size field. Real records are one or four bytes; a
    /// larger value means the file is damaged, and trusting it would index far out of
    /// bounds.
    /// </summary>
    private const int MaxRecordSize = 1024;

    /// <summary>
    /// 28 bytes: the literal text, then SUB and NUL. The SUB is a DOS-era end-of-text
    /// convention and is part of the format, written as an explicit escape so it does
    /// not sit in the source as an invisible control byte.
    /// </summary>
    private static ReadOnlySpan<byte> Header => "lineage configuration file\u001A\0"u8;

    private readonly byte[] _data;

    private LineageConfigDocument(byte[] data) => _data = data;

    /// <summary>Whether a value has been changed since the document was loaded.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Parses an existing document.</summary>
    /// <exception cref="InvalidDataException">The file is truncated or damaged.</exception>
    public static LineageConfigDocument Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < HeaderLength + sizeof(uint))
        {
            throw new InvalidDataException($"lineage.cfg is only {data.Length} bytes.");
        }

        var document = new LineageConfigDocument(data);

        // Walk once up front so a damaged file is rejected here rather than halfway
        // through an edit.
        foreach (var _ in document.EnumerateRecords())
        {
        }

        return document;
    }

    /// <summary>Reads and parses a document from disk.</summary>
    public static LineageConfigDocument Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Builds a minimal document containing just the display settings.
    /// </summary>
    /// <remarks>
    /// Needed because a player who has never opened the in-game settings menu has no
    /// <c>lineage.cfg</c> at all, and the launcher still has to be able to force
    /// windowed mode.
    /// </remarks>
    public static LineageConfigDocument CreateMinimal(bool fullScreen, WindowMode windowMode)
    {
        var raw = BitConverter.GetBytes((uint)windowMode);

        var data = new List<byte>(HeaderLength + 64);
        data.AddRange(Header);
        AppendRecord(data, FullScreenKey, [fullScreen ? (byte)1 : (byte)0]);
        AppendRecord(data, WindowModeKey, raw);
        AppendRecord(data, PreviousWindowModeKey, raw);
        data.AddRange(BitConverter.GetBytes(Terminator));

        return new LineageConfigDocument([.. data]) { IsDirty = true };
    }

    /// <summary>Reads a record's value, or false if the key is not present.</summary>
    public bool TryGetValue(uint key, out ReadOnlySpan<byte> value)
    {
        foreach (var record in EnumerateRecords())
        {
            if (record.Key == key)
            {
                value = _data.AsSpan(record.ValueOffset, record.ValueLength);
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Overwrites a record's value in place.
    /// </summary>
    /// <returns>
    /// False if the key is absent. That is not an error: a config written by an older
    /// client legitimately lacks records this launcher knows about, and adding one
    /// would mean resizing the stream.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// The record exists but is a different size than expected, which means the format
    /// assumption is wrong and writing anyway would corrupt the file.
    /// </exception>
    public bool TrySetValue(uint key, ReadOnlySpan<byte> value)
    {
        foreach (var record in EnumerateRecords())
        {
            if (record.Key != key)
            {
                continue;
            }

            if (record.ValueLength != value.Length)
            {
                throw new InvalidDataException(
                    $"lineage.cfg key 0x{key:X} holds {record.ValueLength} bytes, not {value.Length}.");
            }

            var target = _data.AsSpan(record.ValueOffset, record.ValueLength);
            if (target.SequenceEqual(value))
            {
                // Already correct. Not marking dirty keeps the file's timestamp stable
                // across launches.
                return true;
            }

            value.CopyTo(target);
            IsDirty = true;
            return true;
        }

        return false;
    }

    /// <summary>Convenience wrapper over <see cref="TrySetValue"/> for 32-bit records.</summary>
    public bool TrySetUInt32(uint key, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        return TrySetValue(key, buffer);
    }

    /// <summary>Convenience wrapper over <see cref="TrySetValue"/> for single-byte records.</summary>
    public bool TrySetByte(uint key, byte value) => TrySetValue(key, [value]);

    /// <summary>The document's bytes.</summary>
    public byte[] ToBytes() => [.. _data];

    /// <summary>Writes to disk only if something actually changed.</summary>
    /// <returns>Whether a write happened.</returns>
    public bool SaveIfDirty(string path)
    {
        if (!IsDirty)
        {
            return false;
        }

        File.WriteAllBytes(path, _data);
        IsDirty = false;
        return true;
    }

    private readonly record struct Record(uint Key, int ValueOffset, int ValueLength);

    /// <summary>
    /// Walks the record stream, skipping string records and stopping at the terminator.
    /// </summary>
    /// <exception cref="InvalidDataException">The stream is truncated or a size is implausible.</exception>
    private IEnumerable<Record> EnumerateRecords()
    {
        var offset = HeaderLength;

        while (offset + sizeof(uint) <= _data.Length)
        {
            var key = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset));
            if (key == Terminator)
            {
                yield break;
            }

            if (key >= StringKeyThreshold)
            {
                var nul = Array.IndexOf(_data, (byte)0, offset + sizeof(uint));
                if (nul < 0)
                {
                    throw new InvalidDataException(
                        $"lineage.cfg string record 0x{key:X} at 0x{offset:X} has no terminator.");
                }

                offset = nul + 1;
                continue;
            }

            if (offset + (2 * sizeof(uint)) > _data.Length)
            {
                throw new InvalidDataException($"lineage.cfg is truncated at 0x{offset:X}.");
            }

            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset + sizeof(uint)));
            var valueOffset = offset + (2 * sizeof(uint));

            if (size is < 0 or > MaxRecordSize || valueOffset + size > _data.Length)
            {
                throw new InvalidDataException(
                    $"lineage.cfg record at 0x{offset:X} is damaged (key 0x{key:X}, size {size}).");
            }

            yield return new Record(key, valueOffset, size);
            offset = valueOffset + size;
        }
    }

    private static void AppendRecord(List<byte> data, uint key, ReadOnlySpan<byte> value)
    {
        data.AddRange(BitConverter.GetBytes(key));
        data.AddRange(BitConverter.GetBytes((uint)value.Length));
        data.AddRange(value);
    }
}
