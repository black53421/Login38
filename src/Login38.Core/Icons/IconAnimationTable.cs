using System.Buffers.Binary;

namespace Login38.Core.Icons;

/// <summary>
/// The animation table as the game reads it.
/// </summary>
/// <remarks>
/// Fixed-size records in ascending icon order, scanned by machine code that steps a
/// constant and compares one field. Everything about the layout is chosen for that: no
/// pointers to follow, no lengths to read before stepping, and the field the scan compares
/// first in the record.
/// </remarks>
public static class IconAnimationTable
{
    /// <summary>Bytes per record.</summary>
    /// <remarks>
    /// <c>icon:u16, speed:u16, rest:u32, frames:u32</c>, then ninety-nine frame slots.
    /// Unused slots are zero and never read, because the count says how many there are.
    /// </remarks>
    public const int RecordSize = 2 + 2 + 4 + 4 + (IconAnimation.MaxFrames * 4);

    private const int SpeedOffset = 2;
    private const int RestOffset = 4;
    private const int CountOffset = 8;

    /// <summary>Offset of the first frame slot within a record.</summary>
    public const int FramesOffset = 12;

    /// <summary>Serialises the table, in the order the scan expects.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An entry carries more frames than a record holds.</exception>
    public static byte[] Serialise(IEnumerable<IconAnimation> animations)
    {
        ArgumentNullException.ThrowIfNull(animations);

        var ordered = animations.OrderBy(a => a.Icon).ToList();
        var table = new byte[ordered.Count * RecordSize];

        for (var i = 0; i < ordered.Count; i++)
        {
            Write(table.AsSpan(i * RecordSize, RecordSize), ordered[i]);
        }

        return table;
    }

    private static void Write(Span<byte> record, IconAnimation animation)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(animation.Frames.Count, IconAnimation.MaxFrames);

        BinaryPrimitives.WriteUInt16LittleEndian(record, animation.Icon);
        BinaryPrimitives.WriteUInt16LittleEndian(record[SpeedOffset..], animation.FrameMilliseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(record[RestOffset..], animation.RestMilliseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(record[CountOffset..], (uint)animation.Frames.Count);

        for (var frame = 0; frame < animation.Frames.Count; frame++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                record[(FramesOffset + (frame * 4))..], animation.Frames[frame]);
        }
    }
}
