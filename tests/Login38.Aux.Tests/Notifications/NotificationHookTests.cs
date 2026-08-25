using Login38.Aux.Notifications;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers reading one slot back out of the ring.
/// </summary>
public sealed class NotificationHookTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    [Fact]
    public void ReadsAPickupBackOutOfItsSlot()
    {
        var slot = Slot(4, PacketBox.ItemBoard, [0x34, 0x12, (byte)'A', (byte)'B', 0]);

        var picked = NotificationHook.Read(slot, 4).ShouldBeOfType<Notification.Toast>();

        picked.Sprite.ShouldBe((ushort)0x1234);
        picked.Name.ShouldBe("AB"u8.ToArray());
    }

    [Fact]
    public void ReadsWhatAKillWasWorth()
    {
        var slot = Slot(0, PacketBox.ShowDrop, [0x00, 0x74, 0x18, 0x00, 0x00]);

        NotificationHook.Read(slot, 0).ShouldBeOfType<Notification.Drift>()
            .Amount.ShouldBe(6260u);
    }

    // A slot the ring has come back round to still holds a whole packet from its last lap.
    // The reference marks slots with a constant, which that one satisfies too.
    [Fact]
    public void RefusesASlotLeftFromAPreviousLapOfTheRing() =>
        NotificationHook.Read(Slot(3, PacketBox.ShowDrop, [0, 1, 0, 0, 0]), expected: 19).ShouldBeNull();

    // A slot claimed for a packet the detour then found nothing to copy from keeps whatever
    // sequence it had, so it is passed over rather than read as a packet.
    [Fact]
    public void RefusesASlotThatWasClaimedAndNeverFilledIn()
    {
        var slot = new byte[NotificationHookCave.SlotSize];

        slot[0] = PacketBox.ShowDrop;

        NotificationHook.Read(slot, 5).ShouldBeNull();
    }

    [Fact]
    public void RefusesASlotTooShortToHoldTheFields() =>
        NotificationHook.Read(new byte[NotificationHookCave.SlotSize - 1], 0).ShouldBeNull();

    [Fact]
    public void HasNothingToSayAboutAPacketItDoesNotShow() =>
        NotificationHook.Read(Slot(1, 100, [1, 2, 3]), 1).ShouldBeNull();

    // Against a process that is not the game: the branch does not hold what the client is
    // expected to have there, so nothing is written.
    [Fact]
    public void RefusesToDetourAClientItDoesNotRecognise() =>
        Should.Throw<GameProcessException>(() => Hook().Install(_process));

    [Fact]
    public void HasNothingToDrainBeforeItIsInstalled() =>
        Hook().Drain(_process).ShouldBeEmpty();

    [Fact]
    public void HasNothingToRemoveBeforeItIsInstalled() =>
        Should.NotThrow(() => Hook().Remove(_process));

    [Fact]
    public void IsNotInstalledToStartWith() => Hook().Installed.ShouldBeFalse();

    private static NotificationHook Hook() => new(NullLogger<NotificationHook>.Instance);

    /// <summary>Lays out a slot the way the detour would have.</summary>
    private static byte[] Slot(uint sequence, byte opcode, byte[] payload)
    {
        var slot = new byte[NotificationHookCave.SlotSize];

        slot[0] = opcode;
        payload.CopyTo(slot.AsSpan(NotificationHookCave.PayloadOffset));
        BitConverter.TryWriteBytes(slot.AsSpan(NotificationHookCave.SequenceOffset), sequence);

        return slot;
    }
}
