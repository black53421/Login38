using System.Globalization;
using Login38.Core.Servers;
using Login38.Interop;
using Login38.Patching.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the run-cycle hook's machine code, byte for byte.
/// </summary>
/// <remarks>
/// The expected sequence comes from transcribing the reference's <c>build_shellcode</c> and
/// running it for a cave at <c>0x12340000</c>, minus the block that existed only to poke the
/// bot's walk engine. This runs several times a frame per visible character, inside the
/// client's own frame, with the client's registers live — nothing about it can be observed
/// short of running the game, so the bytes are the test.
/// </remarks>
public sealed class SmoothRunPatchTests
{
    private static readonly GameAddress Cave = new(0x1234_0000);

    private const string Expected =
        "8B 45 0C 8B 44 C2 04 81 BA 14 03 00 00 00 00 01 00 0F 82 F2 00 00 00 8B 4D 0C 85 C9 74 46 83 F9 " +
        "04 74 41 83 F9 0B 74 3C 83 F9 14 74 37 83 F9 18 74 32 83 F9 28 74 2D 83 F9 2E 74 28 83 F9 32 74 " +
        "23 83 F9 36 74 1E 83 F9 3A 74 19 83 F9 3E 74 14 83 F9 53 74 0F 83 F9 58 74 0A 83 F9 77 74 05 E9 " +
        "A5 00 00 00 8B 4D 04 81 F9 00 A0 5A 00 0F 82 96 00 00 00 81 F9 00 AA 5A 00 0F 87 8A 00 00 00 8B " +
        "4D 00 8B 49 A4 85 C9 0F 84 7C 00 00 00 50 80 79 29 00 74 74 57 89 C8 C1 E8 03 83 E0 3F C1 E0 02 " +
        "05 00 02 34 12 89 C7 66 39 0F 75 12 0F B6 41 17 3A 47 02 73 04 80 77 03 01 88 47 02 EB 19 66 89 " +
        "0F 0F B6 41 17 88 47 02 C6 47 03 00 8B 45 0C 85 C0 74 04 C6 47 03 01 0F B6 47 03 5F 85 C0 59 75 " +
        "0B 8B 82 14 03 00 00 E9 1D 00 00 00 81 BA 1C 03 00 00 00 00 01 00 72 08 8B 82 1C 03 00 00 EB 09 " +
        "8B 82 14 03 00 00 EB 01 58 5D C3";

    [Fact]
    public void MatchesTheReferenceByteForByte() => Build().ShouldBe(Parse(Expected));

    // The cave shares its page with the per-character foot table it writes to. Code that
    // reached it would be overwriting itself as characters move.
    [Fact]
    public void LeavesTheFootTableClearOfItsCode() => Build().Length.ShouldBeLessThan(0x200);

    // The client's own answer has to be produced on every path that decides not to change
    // it, and the displaced instruction is the only thing that produces it.
    [Fact]
    public void ReplaysTheDisplacedLookupFirst() =>
        Build()[..7].ShouldBe([0x8B, 0x45, 0x0C, 0x8B, 0x44, 0xC2, 0x04]);

    // Entered by a jump from mid-function, so it owes the caller the pop and the return the
    // hook took over.
    [Fact]
    public void EndsWithTheEpilogueItDisplaced() => Build()[^2..].ShouldBe([0x5D, 0xC3]);

    // The one register used here that the client's function does not treat as scratch.
    [Fact]
    public void SavesAndRestoresTheOneNonScratchRegister()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("57");  // push edi
        code.ShouldContain("5F");  // pop edi
    }

    // The stride has to land on a whole animation. Switching feet from the action number
    // would flip it mid-step, every step.
    [Fact]
    public void FlipsTheFootOnlyWhenTheAnimationWraps()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("0F B6 41 17");     // read the character's animation frame
        code.ShouldContain("3A 47 02");        // against the one last seen
        code.ShouldContain("80 77 03 01");     // and flip only when it went backwards
        code.ShouldNotContain("8B 4D 0C 85 C9 59 75");  // never straight from the action number
    }

    // Every walk slot, or a hasted character carrying the wrong weapon keeps shuffling.
    [Theory]
    [InlineData(4)]
    [InlineData(11)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(46)]
    [InlineData(50)]
    [InlineData(54)]
    [InlineData(58)]
    [InlineData(62)]
    [InlineData(83)]
    [InlineData(88)]
    [InlineData(119)]
    public void RecognisesEveryWalkSlot(byte slot) =>
        BytePattern.Format(Build()).ShouldContain($"83 F9 {slot:X2} 74");

    // Slot zero is covered by the test that precedes the compares, which is a byte shorter.
    [Fact]
    public void RecognisesTheDefaultWalkWithoutComparingAgainstZero()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("8B 4D 0C 85 C9 74");
        code.ShouldNotContain("83 F9 00");
    }

    // The lookup serves more than movement. A hasted character's attack animation is still
    // its attack animation.
    [Fact]
    public void OnlyRedirectsCallsFromTheMovementCode()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("8B 4D 04 81 F9 00 A0 5A 00");  // the caller's return address, against the low bound
        code.ShouldContain("81 F9 00 AA 5A 00");           // and the high one
    }

    // A sprite the morph table had no run cycle for leaves these slots empty, and an empty
    // slot holds a small number rather than a pointer.
    [Fact]
    public void ChecksBothSlotsHoldSomethingBeforeUsingThem()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("81 BA 14 03 00 00 00 00 01 00");  // slot 98
        code.ShouldContain("81 BA 1C 03 00 00 00 00 01 00");  // slot 99
    }

    // A table can carry one half of a run cycle and not the other. Half of one on both feet
    // still reads better than a shuffle.
    [Fact]
    public void FallsBackToTheLeftFootWhenTheRightSlotIsEmpty() =>
        BytePattern.Format(Build())
            .ShouldContain("81 BA 1C 03 00 00 00 00 01 00 72 08 8B 82 1C 03 00 00 EB 09 8B 82 14 03 00 00");

    // Nothing is in those slots unless the morph table was preprocessed, and whether it was
    // is a fact about this launch rather than about the configuration.
    [Fact]
    public void WaitsForAMorphTableWithRunCycles()
    {
        ShouldApplyWith(hasRunCycles: false).ShouldBeFalse();
        ShouldApplyWith(hasRunCycles: true).ShouldBeTrue();
    }

    private static bool ShouldApplyWith(bool hasRunCycles)
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        var context = new GamePatchContext(
            process, AppContext.BaseDirectory, new AuxConfig(), new ServerInfo("test", "127.0.0.1", 2000))
        {
            MorphTableHasRunCycles = hasRunCycles,
        };

        return new SmoothRunPatch(NullLogger<SmoothRunPatch>.Instance).ShouldApply(context);
    }

    private static byte[] Build() => SmoothRunPatch.BuildShellcode(Cave);

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
