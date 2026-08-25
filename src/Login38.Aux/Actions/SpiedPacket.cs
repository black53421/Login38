using System.Text;
using Login38.Interop;

namespace Login38.Aux.Actions;

/// <summary>One call to <c>SendPacketData</c>, as the recorder caught it.</summary>
/// <param name="Sequence">Which call this was since the recorder went in.</param>
/// <param name="Caller">Where in the client it was called from.</param>
/// <param name="Format">The format string's address, which is a constant in the client.</param>
/// <param name="Descriptor">
/// That string: one letter per argument — <c>c</c> a byte, <c>h</c> a word, <c>d</c> a
/// dword, <c>s</c> a NUL-terminated string.
/// </param>
/// <param name="Arguments">As many arguments as the descriptor names.</param>
public readonly record struct SpiedPacket(
    uint Sequence,
    GameAddress Caller,
    GameAddress Format,
    string Descriptor,
    IReadOnlyList<uint> Arguments)
{
    /// <summary>
    /// Reads one entry, or null where the slot has not been filled in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things have to agree before an entry is believed: the magic the detour writes
    /// last, and the sequence number it claimed. The reference checks only the magic, which
    /// a slot from a previous lap round the ring also satisfies.
    /// </para>
    /// <para>
    /// The descriptor is what says how many of the copied stack slots were arguments at
    /// all. Past it are the caller's own locals, which the reference logs as arguments —
    /// ten of them, every time, whatever was called.
    /// </para>
    /// </remarks>
    /// <param name="expected">The sequence number this slot should be holding.</param>
    internal static SpiedPacket? Read(ReadOnlySpan<byte> entry, uint expected)
    {
        if (entry.Length < PacketSpyCave.EntrySize
            || BitConverter.ToUInt32(entry[PacketSpyCave.MagicOffset..]) != PacketSpyCave.Magic
            || BitConverter.ToUInt32(entry[PacketSpyCave.SequenceOffset..]) != expected)
        {
            return null;
        }

        var descriptor = Text(entry.Slice(PacketSpyCave.HeadOffset, PacketSpyCave.HeadBytes));

        // The return address, then the format, then the arguments the descriptor names.
        var wanted = Math.Min(descriptor.Length, PacketSpyCave.Slots - 2);
        var arguments = new uint[wanted];

        for (var i = 0; i < wanted; i++)
        {
            arguments[i] = BitConverter.ToUInt32(entry[((i + 2) * 4)..]);
        }

        return new SpiedPacket(
            expected,
            new GameAddress(BitConverter.ToUInt32(entry)),
            new GameAddress(BitConverter.ToUInt32(entry[4..])),
            descriptor,
            arguments);
    }

    /// <summary>
    /// Reads the format string out of the bytes copied from it.
    /// </summary>
    /// <remarks>
    /// Plain ASCII, and deliberately not through the code page: these are the client's own
    /// format letters, and anything that is not one of them means the first argument was
    /// not a format string. Decoding it as Big5 would turn that into a plausible-looking
    /// name rather than an obviously empty answer.
    /// </remarks>
    private static string Text(ReadOnlySpan<byte> raw)
    {
        var length = 0;

        while (length < raw.Length && raw[length] is >= (byte)'a' and <= (byte)'z')
        {
            length++;
        }

        return length == 0 || (length < raw.Length && raw[length] != 0)
            ? string.Empty
            : Encoding.ASCII.GetString(raw[..length]);
    }

    /// <summary>One line, for the log.</summary>
    public override string ToString()
    {
        var letters = Descriptor;
        var arguments = string.Join(", ", Arguments.Select((value, at) =>
            $"{letters[at]}={value}/0x{value:X8}"));

        return $"#{Sequence} from {Caller}: \"{Descriptor}\" ({arguments})";
    }
}
