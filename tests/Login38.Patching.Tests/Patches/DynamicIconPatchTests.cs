using System.Globalization;
using Login38.Core.Icons;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the icon hook, byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The expected sequence comes from transcribing the reference's
/// <c>build_hook_shellcode</c> and running it for a cave at <c>0x0A000000</c> over a table
/// of three at <c>0x0B000000</c>, with two deliberate differences: a guard on a cycle of
/// zero, and the frame returned through the saved register rather than through a fixed slot
/// in the cave. Both are asserted separately below.
/// </para>
/// <para>
/// This runs on the client's render path for every item icon on screen, entered before the
/// resolver has saved anything. A register left changed corrupts the caller; a wrong
/// displacement reads the wrong field of the wrong record.
/// </para>
/// </remarks>
public sealed class DynamicIconPatchTests
{
    private static readonly GameAddress Cave = new(0x0A00_0000);

    private static readonly GameAddress Table = new(0x0B00_0000);

    private static readonly GameAddress Clock = new(0x7712_3456);

    private const int Count = 3;

    private const string Expected =
        "60 8B 44 24 24 BA 00 00 00 0B 81 FA C8 04 00 0B 0F 83 51 00 00 00 0F B7 0A 3B C1 0F 84 08 00 00 " +
        "00 81 C2 98 01 00 00 EB E1 8B EA B8 56 34 12 77 FF D0 0F B7 4D 02 8B 5D 08 0F AF D9 8B 7D 04 03 " +
        "FB 85 FF 0F 84 1E 00 00 00 31 D2 F7 F7 3B D3 0F 83 12 00 00 00 8B C2 31 D2 F7 F1 8B 44 85 0C 89 " +
        "44 24 1C 61 C2 04 00 61 55 8B EC 83 EC 4C E9 03 B2 45 F6";

    [Fact]
    public void MatchesTheReferenceByteForByte() => Build().ShouldBe(Parse(Expected));

    [Fact]
    public void FitsTheCaveItIsWrittenInto() => Build().Length.ShouldBeLessThanOrEqualTo(0x100);

    // Entered before the resolver's own prologue, so every register still belongs to its
    // caller and the graphic id is still on the stack.
    [Fact]
    public void SavesEverythingBeforeItTouchesAnything()
    {
        var code = Build();

        code[0].ShouldBe((byte)0x60);                            // pushad
        code[1..5].ShouldBe([0x8B, 0x44, 0x24, 0x24]);           // mov eax, [esp+0x24]
    }

    // Thirty-two bytes of saved registers plus the return address, so the argument the
    // caller pushed is one dword further along than it was.
    [Fact]
    public void ReadsTheArgumentPastTheRegistersItJustSaved() =>
        BytePattern.Format(Build()).ShouldContain("60 8B 44 24 24");

    // The scan steps by whole records and compares the field the layout puts first.
    [Fact]
    public void WalksTheTableByOneRecordAtATime()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("BA 00 00 00 0B");                    // mov edx, table
        code.ShouldContain("81 FA C8 04 00 0B");                 // cmp edx, table + 3 records
        code.ShouldContain("81 C2 98 01 00 00");                 // add edx, 408
    }

    // A record's icon is sixteen bits and the id being resolved is thirty-two. Comparing
    // the low half only would animate an icon nobody configured.
    [Fact]
    public void ComparesTheWholeIdRatherThanItsLowHalf() =>
        BytePattern.Format(Build()).ShouldContain("0F B7 0A 3B C1");   // movzx ecx, word [edx]; cmp eax, ecx

    // The whole design in one instruction: the frame comes from the clock, so two copies of
    // the same icon cannot be on different frames and nothing has to keep them together.
    [Fact]
    public void AsksTheClockRatherThanRememberingAnything()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("B8 56 34 12 77 FF D0");              // mov eax, GetTickCount; call eax
        code.ShouldContain("31 D2 F7 F7");                       // xor edx, edx; div edi — clock % cycle
        code.ShouldContain("31 D2 F7 F1");                       // xor edx, edx; div ecx — that / frame time
    }

    // The same arithmetic as the manifest side, from the same record: count times frame
    // time is the animation, plus the rest is the cycle.
    [Fact]
    public void ComputesTheCycleFromTheRecordsOwnFields()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("0F B7 4D 02");                       // movzx ecx, word [ebp+2] — frame time
        code.ShouldContain("8B 5D 08");                          // mov ebx, [ebp+8] — frame count
        code.ShouldContain("0F AF D9");                          // imul ebx, ecx
        code.ShouldContain("8B 7D 04 03 FB");                    // mov edi, [ebp+4]; add edi, ebx
    }

    // Not in the reference. A cycle of zero is a division by zero on the render path, which
    // ends the client rather than the animation — and the table can be built by anything.
    [Fact]
    public void RefusesToDivideByACycleOfZero()
    {
        var code = Build();
        var guard = BytePattern.Format(code).IndexOf("85 FF 0F 84", StringComparison.Ordinal);

        guard.ShouldBeGreaterThan(0);

        // Before the division, and landing where the client's own icon is drawn.
        var at = guard / 3;
        code.AsSpan(at + 8, 4).ToArray().ShouldBe([0x31, 0xD2, 0xF7, 0xF7]);
        Target(code, at + 4).ShouldBe(PassAt(code));
    }

    // Reading the frame index from the record puts it four bytes in per frame, past the
    // three fields in front of them.
    [Fact]
    public void ReadsTheFrameTheIndexPointsAt() =>
        BytePattern.Format(Build()).ShouldContain($"8B 44 85 {IconAnimationTable.FramesOffset:X2}");

    // Returned through the register the restore will reload, so there is nothing shared
    // between calls. The reference parked it in a fixed slot in the cave, which two threads
    // drawing at the same moment would have written over each other in.
    [Fact]
    public void ReturnsTheFrameWithoutAnySharedState()
    {
        var code = BytePattern.Format(Build());

        // mov [esp+0x1C], eax; popad; ret 4 — the value is carried out in the register the
        // restore reloads, so nothing outlives the call.
        code.ShouldContain("89 44 24 1C 61 C2 04 00");
    }

    // The resolver is __thiscall with one stack argument, so it cleans that argument itself.
    // Returning with a bare ret leaves the stack one dword out on every icon drawn.
    [Fact]
    public void ReturnsTheWayTheFunctionItReplacedDoes() => Build()[^15..^12].ShouldBe([0xC2, 0x04, 0x00]);

    // Every path that is not an animating icon has to come out of here indistinguishable
    // from never having been entered: registers back, the prologue the jump displaced, and
    // the instruction after it.
    [Fact]
    public void LeavesEveryOtherIconExactlyAsItWas()
    {
        var code = Build();
        var pass = PassAt(code);

        code[pass].ShouldBe((byte)0x61);                          // popad
        code.AsSpan(pass + 1, 6).ToArray().ShouldBe([0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x4C]);
        code[pass + 7].ShouldBe((byte)0xE9);

        (Cave + (pass + 7 + 5 + BitConverter.ToInt32(code, pass + 8)))
            .ShouldBe(new GameAddress(0x0045_B276));
    }

    // Three ways of not animating — past the end of the table, no cycle, and resting — and
    // all of them are the same code.
    [Fact]
    public void SendsEveryOtherOutcomeToTheSameExit()
    {
        var code = Build();
        var pass = PassAt(code);

        foreach (var branch in NearBranches(code))
        {
            if (Target(code, branch) != FoundAt(code))
            {
                Target(code, branch).ShouldBe(pass);
            }
        }
    }

    [Fact]
    public void ScansNothingForAnEmptyTable()
    {
        var code = DynamicIconPatch.BuildShellcode(Cave, Table, 0, Clock);

        // The end is the start, so the first comparison passes straight through.
        BytePattern.Format(code).ShouldContain("BA 00 00 00 0B 81 FA 00 00 00 0B");
    }

    private static byte[] Build() => DynamicIconPatch.BuildShellcode(Cave, Table, Count, Clock);

    /// <summary>Where the shared exit begins: the last <c>popad</c> in the block.</summary>
    private static int PassAt(byte[] code) => code.Length - 12;

    /// <summary>Where a match lands: immediately after the scan's backward jump.</summary>
    private static int FoundAt(byte[] code) =>
        BytePattern.Format(code).IndexOf("EB E1 8B EA", StringComparison.Ordinal) / 3 + 2;

    /// <summary>Offsets of the displacement of every <c>0F 8x rel32</c> in the block.</summary>
    private static IEnumerable<int> NearBranches(byte[] code)
    {
        for (var at = 0; at + 6 <= code.Length; at++)
        {
            if (code[at] == 0x0F && code[at + 1] is 0x83 or 0x84)
            {
                yield return at + 2;
            }
        }
    }

    private static int Target(byte[] code, int displacement) =>
        displacement + 4 + BitConverter.ToInt32(code, displacement);

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
