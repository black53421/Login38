using System.Collections.Concurrent;
using Login38.Interop;

namespace Login38.Patching.Diagnostics;

/// <summary>
/// Shared control block for the receive-side packet recorder installed in front of the
/// client's packet dispatcher.
/// </summary>
/// <remarks>
/// The recorder itself runs in the game process. This type only remembers where its data
/// block was allocated so the auxiliary task can turn recording on and drain the ring.
/// Keeping the control plane here lets the recorder coexist with the long-item-status
/// router, which already owns the dispatcher entry point.
/// </remarks>
public static class ReceivePacketProbe
{
    public const int RingLength = 128;
    public const int EntrySize = 32;
    public const int PacketBytes = 16;

    public const uint EnabledOffset = 0x00;
    public const uint IndexOffset = 0x04;
    public const uint RingOffset = 0x100;

    public const int CallerOffset = 0;
    public const int PacketOffset = 4;
    public const int SequenceOffset = PacketOffset + PacketBytes;

    public const int DataSize = checked((int)RingOffset + (RingLength * EntrySize));

    private static readonly ConcurrentDictionary<uint, GameAddress> Blocks = new();

    internal static void Register(uint processId, GameAddress data) => Blocks[processId] = data;

    /// <summary>Turns receive recording on or off for this game process.</summary>
    public static bool TrySetEnabled(RemoteProcess process, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!Blocks.TryGetValue(process.Id, out var data))
        {
            return false;
        }

        var value = enabled ? 1u : 0u;
        process.Write(data + EnabledOffset, value);
        return true;
    }

    /// <summary>Reads the number of ring slots claimed by the recorder.</summary>
    public static bool TryReadWriteIndex(RemoteProcess process, out uint written)
    {
        ArgumentNullException.ThrowIfNull(process);
        written = 0;

        return Blocks.TryGetValue(process.Id, out var data)
               && process.TryRead(data + IndexOffset, out written);
    }

    /// <summary>Reads one completed ring entry.</summary>
    public static bool TryRead(RemoteProcess process, uint sequence, out ReceivePacketRecord record)
    {
        ArgumentNullException.ThrowIfNull(process);
        record = default;

        if (!Blocks.TryGetValue(process.Id, out var data))
        {
            return false;
        }

        Span<byte> entry = stackalloc byte[EntrySize];
        var slot = data + RingOffset + ((sequence % RingLength) * EntrySize);

        if (!process.TryReadBytes(slot, entry))
        {
            return false;
        }

        // Zero is reserved for "not committed" so sequence zero is distinguishable from
        // the zero-filled allocation before the first packet finishes writing.
        var committed = BitConverter.ToUInt32(entry[SequenceOffset..]);
        if (committed != unchecked(sequence + 1))
        {
            return false;
        }

        var caller = new GameAddress(BitConverter.ToUInt32(entry[CallerOffset..]));
        var packet = entry.Slice(PacketOffset, PacketBytes).ToArray();
        record = new ReceivePacketRecord(sequence, caller, packet);
        return true;
    }
}

/// <summary>One Server-to-Client packet caught before the client dispatches it.</summary>
public readonly record struct ReceivePacketRecord(
    uint Sequence,
    GameAddress DispatcherCaller,
    ReadOnlyMemory<byte> Bytes)
{
    public byte Opcode => Bytes.IsEmpty ? (byte)0 : Bytes.Span[0];

    public override string ToString() =>
        $"#{Sequence} caller={DispatcherCaller} opcode={Opcode}/0x{Opcode:X2} data={BytePattern.Format(Bytes.Span)}";
}
