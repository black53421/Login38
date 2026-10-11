using System.Buffers.Binary;
using Login38.Aux.Game;

namespace Login38.Aux.Runtime;

/// <summary>
/// Decodes the elf buff icon packets whose client-side mutual exclusion is under study.
/// </summary>
internal static class ElfBuffDiagnostic
{
    internal const byte SkillIconOpcode = 0xFA;
    internal const byte AuraSubtype = 0x16;
    internal const byte ShieldIconOpcode = 0xD8;

    private static readonly ElfBuffIcon[] AuraIcons =
    [
        new(ElfBuffPacketKind.Aura, 147, 18, "FIRE_WEAPON"),
        new(ElfBuffPacketKind.Aura, 154, 19, "FIRE_BLESS"),
        new(ElfBuffPacketKind.Aura, 162, 20, "BURNING_WEAPON"),
        new(ElfBuffPacketKind.Aura, 148, 21, "WIND_SHOT"),
        new(ElfBuffPacketKind.Aura, 155, 22, "STORM_EYE"),
        new(ElfBuffPacketKind.Aura, 165, 23, "STORM_SHOT"),
    ];

    private static readonly ElfBuffIcon[] ShieldIcons =
    [
        new(ElfBuffPacketKind.Shield, 6, 6, "EARTH_SKIN"),
        new(ElfBuffPacketKind.Shield, 7, 7, "EARTH_BLESS"),
        new(ElfBuffPacketKind.Shield, 10, 8, "IRON_SKIN"),
    ];

    internal static bool TryParse(ReadOnlySpan<byte> packet, out ElfBuffIconPacket iconPacket)
    {
        iconPacket = default;

        if (packet.Length >= 5 && packet[0] == SkillIconOpcode && packet[1] == AuraSubtype)
        {
            return TryMap(
                AuraIcons,
                packet[2],
                BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(3, 2)),
                out iconPacket);
        }

        if (packet.Length >= 4 && packet[0] == ShieldIconOpcode)
        {
            return TryMap(
                ShieldIcons,
                packet[3],
                BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(1, 2)),
                out iconPacket);
        }

        return false;
    }

    internal static string FormatStates(BuffState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return $"earth=[{FormatGroup(state, ShieldIcons)}] attack=[{FormatGroup(state, AuraIcons)}]";
    }

    internal static string StateName(int stateId)
    {
        foreach (var icon in ShieldIcons)
        {
            if (icon.StateId == stateId)
            {
                return icon.Name;
            }
        }

        foreach (var icon in AuraIcons)
        {
            if (icon.StateId == stateId)
            {
                return icon.Name;
            }
        }

        return "UNKNOWN";
    }

    private static bool TryMap(
        ReadOnlySpan<ElfBuffIcon> icons,
        byte wireId,
        ushort duration,
        out ElfBuffIconPacket iconPacket)
    {
        foreach (var icon in icons)
        {
            if (icon.WireId != wireId)
            {
                continue;
            }

            iconPacket = new ElfBuffIconPacket(icon, duration);
            return true;
        }

        iconPacket = default;
        return false;
    }

    private static string FormatGroup(BuffState state, IEnumerable<ElfBuffIcon> icons) =>
        string.Join(" ", icons.Select(icon =>
        {
            var raw = state.RawAt(icon.StateId);
            return $"{icon.StateId}:{(state.At(icon.StateId) ? 1 : 0)}({raw.Primary:X2}/{raw.Secondary:X2})";
        }));
}

internal enum ElfBuffPacketKind
{
    Aura,
    Shield,
}

internal readonly record struct ElfBuffIcon(
    ElfBuffPacketKind Kind,
    byte WireId,
    int StateId,
    string Name);

internal readonly record struct ElfBuffIconPacket(ElfBuffIcon Icon, ushort Duration);
