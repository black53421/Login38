using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

public sealed class ElfBuffDiagnosticTests
{
    [Theory]
    [InlineData(147, 18, "FIRE_WEAPON")]
    [InlineData(154, 19, "FIRE_BLESS")]
    [InlineData(162, 20, "BURNING_WEAPON")]
    [InlineData(148, 21, "WIND_SHOT")]
    [InlineData(155, 22, "STORM_EYE")]
    [InlineData(165, 23, "STORM_SHOT")]
    public void MapsElfAuraPacketsToTheirClientStates(int auraId, int stateId, string name)
    {
        Span<byte> packet = stackalloc byte[] { 0xFA, 0x16, (byte)auraId, 0xC0, 0x03 };

        ElfBuffDiagnostic.TryParse(packet, out var parsed).ShouldBeTrue();
        parsed.Icon.Kind.ShouldBe(ElfBuffPacketKind.Aura);
        parsed.Icon.WireId.ShouldBe((byte)auraId);
        parsed.Icon.StateId.ShouldBe(stateId);
        parsed.Icon.Name.ShouldBe(name);
        parsed.Duration.ShouldBe((ushort)960);
    }

    [Theory]
    [InlineData(6, 6, "EARTH_SKIN")]
    [InlineData(7, 7, "EARTH_BLESS")]
    [InlineData(10, 8, "IRON_SKIN")]
    public void MapsElfShieldPacketsToTheirClientStates(int type, int stateId, string name)
    {
        Span<byte> packet = stackalloc byte[] { 0xD8, 0xD0, 0x02, (byte)type, 0, 0, 0, 0 };

        ElfBuffDiagnostic.TryParse(packet, out var parsed).ShouldBeTrue();
        parsed.Icon.Kind.ShouldBe(ElfBuffPacketKind.Shield);
        parsed.Icon.WireId.ShouldBe((byte)type);
        parsed.Icon.StateId.ShouldBe(stateId);
        parsed.Icon.Name.ShouldBe(name);
        parsed.Duration.ShouldBe((ushort)720);
    }

    [Theory]
    [InlineData(0xF9, 0x16, 147)]
    [InlineData(0xFA, 0x15, 147)]
    [InlineData(0xFA, 0x16, 146)]
    public void IgnoresAuraPacketsOutsideTheTargetSet(int opcode, int subtype, int auraId)
    {
        Span<byte> packet = stackalloc byte[] { (byte)opcode, (byte)subtype, (byte)auraId, 0, 0 };

        ElfBuffDiagnostic.TryParse(packet, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(11)]
    public void IgnoresShieldPacketsOutsideTheTargetSet(int type)
    {
        Span<byte> packet = stackalloc byte[] { 0xD8, 0, 0, (byte)type, 0, 0, 0, 0 };

        ElfBuffDiagnostic.TryParse(packet, out _).ShouldBeFalse();
    }

    [Fact]
    public void FormatsEarthAndAttackClientEffectHalves()
    {
        var bytes = new byte[BuffState.Half * 2];
        bytes[6] = 1;
        bytes[18] = 1;
        bytes[BuffState.Half + 21] = 2;
        var state = BuffState.From(bytes, characterClass: 4);

        ElfBuffDiagnostic.FormatStates(state).ShouldBe(
            "earth=[6:1(01/00) 7:0(00/00) 8:0(00/00)] " +
            "attack=[18:1(01/00) 19:0(00/00) 20:0(00/00) 21:1(00/02) 22:0(00/00) 23:0(00/00)]");
    }

    [Theory]
    [InlineData(6, "EARTH_SKIN")]
    [InlineData(8, "IRON_SKIN")]
    [InlineData(18, "FIRE_WEAPON")]
    [InlineData(23, "STORM_SHOT")]
    [InlineData(99, "UNKNOWN")]
    public void ResolvesStateNames(int stateId, string expected) =>
        ElfBuffDiagnostic.StateName(stateId).ShouldBe(expected);
}
