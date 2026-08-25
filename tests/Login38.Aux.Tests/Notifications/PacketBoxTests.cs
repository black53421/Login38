using Login38.Aux.Notifications;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers reading the server's catch-all packet.
/// </summary>
public sealed class PacketBoxTests
{
    [Fact]
    public void ReadsAPickup()
    {
        var picked = PacketBox.Parse([PacketBox.ItemBoard, 0x34, 0x12, (byte)'A', (byte)'B', (byte)'C', 0])
            .ShouldBeOfType<Notification.Toast>();

        picked.Sprite.ShouldBe((ushort)0x1234);
        picked.Name.ShouldBe("ABC"u8.ToArray());
    }

    [Theory]
    [InlineData(0, DriftKind.Experience)]
    [InlineData(1, DriftKind.Gold)]
    public void ReadsWhatAKillWasWorth(int kind, DriftKind expected)
    {
        var gained = PacketBox.Parse([PacketBox.ShowDrop, (byte)kind, 0x74, 0x18, 0x00, 0x00])
            .ShouldBeOfType<Notification.Drift>();

        gained.Kind.ShouldBe(expected);
        gained.Amount.ShouldBe(6260u);
    }

    [Fact]
    public void HasNothingToSayAboutAnEmptyPayload() => PacketBox.Parse([]).ShouldBeNull();

    [Fact]
    public void HasNothingToSayAboutTheOtherSubIds() =>
        PacketBox.Parse([100, 0x01, 0x02]).ShouldBeNull();

    [Fact]
    public void RefusesAPickupWithoutARealSpriteId() =>
        PacketBox.Parse([PacketBox.ItemBoard, 0x01]).ShouldBeNull();

    // The packet is malformed either way, and a name that runs on is more likely to be a
    // different packet shape than a long item.
    [Fact]
    public void RefusesANameThatIsNotTerminated() =>
        PacketBox.Parse([PacketBox.ItemBoard, 0, 0, (byte)'A', (byte)'B']).ShouldBeNull();

    // Not a limit the protocol has — a limit on what a server can make this launcher draw.
    [Fact]
    public void ClampsAnAbsurdlyLongName()
    {
        byte[] payload = [PacketBox.ItemBoard, 0, 0, .. Enumerable.Repeat((byte)'X', 100), 0];

        PacketBox.Parse(payload).ShouldBeOfType<Notification.Toast>()
            .Name.Length.ShouldBe(PacketBox.LongestName);
    }

    [Fact]
    public void RefusesAKindThatIsNeitherExperienceNorCoin() =>
        PacketBox.Parse([PacketBox.ShowDrop, 99, 0, 0, 0, 0]).ShouldBeNull();

    [Fact]
    public void RefusesAnAmountWithAByteMissing() =>
        PacketBox.Parse([PacketBox.ShowDrop, 0, 0x01, 0x02, 0x03]).ShouldBeNull();

    [Fact]
    public void ReadsAnEmptyName() =>
        PacketBox.Parse([PacketBox.ItemBoard, 0, 0, 0]).ShouldBeOfType<Notification.Toast>()
            .Name.ShouldBeEmpty();
}
