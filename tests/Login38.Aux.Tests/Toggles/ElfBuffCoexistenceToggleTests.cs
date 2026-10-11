using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Login38.Interop;
using Login38.Patching;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

public sealed class ElfBuffCoexistenceToggleTests
{
    [Fact]
    public void FollowsItsOwnSwitch()
    {
        var settings = new AuxSettings();
        var toggle = new ElfBuffCoexistenceToggle(NullLogger<ElfBuffCoexistenceToggle>.Instance);

        toggle.WantedBy(settings).ShouldBeFalse();
        settings.Misc.ElfBuffCoexistence = true;
        toggle.WantedBy(settings).ShouldBeTrue();
    }

    [Fact]
    public void StockCallStillTargetsApplyBuff()
    {
        DeferredSites.RelativeTarget(
            ElfBuffCoexistenceToggle.PeerClearCallSite,
            ElfBuffCoexistenceToggle.StockCall).ShouldBe(ElfBuffCoexistenceToggle.ApplyBuff);
    }

    [Fact]
    public void OnlyVerifiedFireAndEarthStatesAreProtected() =>
        ElfBuffCoexistenceToggle.ProtectedStates.ShouldBe([6, 7, 8, 18, 19, 20]);

    [Fact]
    public void CaveSkipsVerifiedStatesAndTailJumpsEverythingElse()
    {
        var cave = new GameAddress(0x1000_0000);
        var code = ElfBuffCoexistenceToggle.BuildCave(cave);

        code.Length.ShouldBe(55);

        // cmp [esp+8],0 ; jne passthrough
        code.AsSpan(0, 7).ToArray().ShouldBe([0x83, 0x7C, 0x24, 0x08, 0x00, 0x75, 0x2A]);

        var states = new[] { 6, 7, 8, 18, 19, 20 };
        var compareOffsets = new[] { 7, 14, 21, 28, 35, 42 };
        var skipDisplacements = new byte[] { 0x28, 0x21, 0x1A, 0x13, 0x0C, 0x05 };

        for (var index = 0; index < states.Length; index++)
        {
            var at = compareOffsets[index];
            code.AsSpan(at, 5).ToArray().ShouldBe(
                [0x83, 0x7C, 0x24, 0x04, checked((byte)states[index])]);
            code[at + 5].ShouldBe((byte)0x74);
            code[at + 6].ShouldBe(skipDisplacements[index]);
        }

        DeferredSites.RelativeTarget(cave + 49, code.AsSpan(49, 5))
            .ShouldBe(ElfBuffCoexistenceToggle.ApplyBuff);
        code[54].ShouldBe((byte)0xC3);
    }
}
