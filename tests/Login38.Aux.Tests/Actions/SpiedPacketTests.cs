using System.Text;
using Login38.Aux.Actions;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Actions;

/// <summary>
/// Covers reading one recorded call back out of the ring.
/// </summary>
public sealed class SpiedPacketTests
{
    [Fact]
    public void ReadsTheCallerTheFormatAndTheArgumentsItNames()
    {
        var packet = Read(Entry(7, 0x0055_1234, 0x0080_0000, "cssd", [0x77, 0x0090_0000, 0x0090_0010, 5]), 7);

        packet.ShouldNotBeNull();
        packet.Value.Sequence.ShouldBe(7u);
        packet.Value.Caller.ShouldBe(new GameAddress(0x0055_1234));
        packet.Value.Format.ShouldBe(new GameAddress(0x0080_0000));
        packet.Value.Descriptor.ShouldBe("cssd");
        packet.Value.Arguments.ShouldBe([0x77u, 0x0090_0000u, 0x0090_0010u, 5u]);
    }

    // The recorder copies a fixed sixteen stack slots because a variadic call does not say
    // how many it was given. Only the format does, and past it are the caller's own locals
    // — which the reference logs as arguments, ten of them, every time.
    [Fact]
    public void StopsAtWhatTheFormatNamesRatherThanAtWhatWasCopied() =>
        Read(Entry(1, 0, 0x0080_0000, "cc", [1, 2, 0xDEAD, 0xBEEF]), 1)!
            .Value.Arguments.Count.ShouldBe(2);

    [Fact]
    public void ReadsACallWithNoArgumentsAtAll() =>
        Read(Entry(1, 0, 0x0080_0000, "", []), 1)!.Value.Arguments.ShouldBeEmpty();

    // The magic is written last, so a slot without it is one the detour has not finished.
    [Fact]
    public void RefusesASlotThatHasNotBeenWrittenYet()
    {
        var entry = Entry(1, 0, 0x0080_0000, "c", [1]);

        BitConverter.TryWriteBytes(entry.AsSpan(PacketSpyCave.MagicOffset), 0u);

        Read(entry, 1).ShouldBeNull();
    }

    // And a slot the ring has come back round to still carries a valid magic from its last
    // lap. The reference checks only that, so it reads an old call as a new one.
    [Fact]
    public void RefusesASlotLeftFromAPreviousLapOfTheRing() =>
        Read(Entry(3, 0, 0x0080_0000, "c", [1]), expected: 67).ShouldBeNull();

    [Fact]
    public void RefusesAnEntryTooShortToHoldTheFields() =>
        SpiedPacket.Read(new byte[PacketSpyCave.EntrySize - 1], 0).ShouldBeNull();

    // Anything that is not a run of format letters means the first argument was not a
    // format string, and there is then nothing to say how many arguments there were.
    [Fact]
    public void HasNoDescriptorWhereTheFirstArgumentWasNotOne()
    {
        var entry = Entry(1, 0, 0x0080_0000, "", [1]);

        Encoding.ASCII.GetBytes("not a format!").CopyTo(entry.AsSpan(PacketSpyCave.HeadOffset));

        var packet = Read(entry, 1);

        packet!.Value.Descriptor.ShouldBeEmpty();
        packet.Value.Arguments.ShouldBeEmpty();
    }

    [Fact]
    public void WritesOneReadableLine() =>
        Read(Entry(9, 0x0055_1234, 0x0080_0000, "cd", [0x77, 5]), 9)!.Value.ToString()
            .ShouldBe("#9 from 0x00551234: \"cd\" (c=119/0x00000077, d=5/0x00000005)");

    private static SpiedPacket? Read(byte[] entry, uint expected) => SpiedPacket.Read(entry, expected);

    /// <summary>Lays out an entry the way the detour would have.</summary>
    private static byte[] Entry(
        uint sequence, uint caller, uint format, string descriptor, uint[] arguments)
    {
        var entry = new byte[PacketSpyCave.EntrySize];

        BitConverter.TryWriteBytes(entry, caller);
        BitConverter.TryWriteBytes(entry.AsSpan(4), format);

        for (var i = 0; i < arguments.Length; i++)
        {
            BitConverter.TryWriteBytes(entry.AsSpan((i + 2) * 4), arguments[i]);
        }

        Encoding.ASCII.GetBytes(descriptor).CopyTo(entry.AsSpan(PacketSpyCave.HeadOffset));
        BitConverter.TryWriteBytes(entry.AsSpan(PacketSpyCave.SequenceOffset), sequence);
        BitConverter.TryWriteBytes(entry.AsSpan(PacketSpyCave.MagicOffset), PacketSpyCave.Magic);

        return entry;
    }
}
