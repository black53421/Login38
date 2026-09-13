using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

public sealed class RangeSkillDamageProtocolPatchTests
{
    private static readonly GameAddress Cave = new(0x0300_0000);

    [Fact]
    public void CapabilityMarkerTargetsTheObservedServerVersionCall()
    {
        var code = RangeSkillDamageProtocolPatch.BuildMarkerCave(Cave);

        Contains(code,
        [
            0x81, 0x3C, 0x24, 0xE1, 0x0E, 0x4E, 0x00,
        ]).ShouldBeTrue();

        Contains(code,
        [
            0x81, 0x7C, 0x24, 0x08, 0x0E, 0x00, 0x00, 0x00,
        ]).ShouldBeTrue();

        Contains(code,
        [
            0xC7, 0x44, 0x24, 0x1C, 0x4C, 0x33, 0x38, 0x44,
        ]).ShouldBeTrue();
    }

    [Fact]
    public void DoesNotWriteTheOldSeventhArgumentSlot()
    {
        var code = RangeSkillDamageProtocolPatch.BuildMarkerCave(Cave);

        Contains(code,
        [
            0xC7, 0x44, 0x24, 0x20, 0x4C, 0x33, 0x38, 0x44,
        ]).ShouldBeFalse();
    }

    private static bool Contains(byte[] source, byte[] wanted)
    {
        for (var at = 0; at <= source.Length - wanted.Length; at++)
        {
            if (source.AsSpan(at, wanted.Length).SequenceEqual(wanted))
            {
                return true;
            }
        }

        return false;
    }
}
