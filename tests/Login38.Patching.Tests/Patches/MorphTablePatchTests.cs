using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the morph hook's shellcode against what the Rust build emits, byte for byte.
/// </summary>
/// <remarks>
/// The expected sequence comes from transcribing the reference's
/// <c>build_file_hook_shellcode</c> and running it for a cave at <c>0x07000000</c>, a
/// buffer at <c>0x08000000</c> of <c>0x1234</c> bytes, rejoining at <c>0x0058794F</c>.
/// Both destinations are locals of the client's own frame; a wrong displacement writes
/// somewhere else in that frame and the client crashes later with nothing pointing here.
/// </remarks>
public sealed class MorphTablePatchTests
{
    private static readonly GameAddress Cave = new(0x0700_0000);

    private static readonly GameAddress Buffer = new(0x0800_0000);

    private static readonly GameAddress Rejoin = new(0x0058_794F);

    private const int Length = 0x1234;

    private const string Expected =
        "B8 34 12 00 00 89 45 EC B8 01 00 00 08 8B 95 C4 FD FF FF 89 42 08 E9 34 79 58 F9";

    [Fact]
    public void MatchesTheReferenceByteForByte() => Build().ShouldBe(Parse(Expected));

    // The client's own header convention: the buffer starts with a marker byte and the
    // pointer it keeps is past it.
    [Fact]
    public void PointsPastTheMarkerByte() =>
        BitConverter.ToUInt32(Build(), 9).ShouldBe(Buffer.Value + 1);

    [Fact]
    public void StoresTheWholeBufferLength() =>
        BitConverter.ToUInt32(Build(), 1).ShouldBe((uint)Length);

    // Far enough from ebp to need the four-byte displacement form, which is a different
    // ModR/M byte rather than the same instruction with a wider field.
    [Fact]
    public void ReadsTheOwnerThroughTheLongDisplacementForm()
    {
        var code = Build();

        code[13..15].ShouldBe([0x8B, 0x95]);
        BitConverter.ToInt32(code, 15).ShouldBe(-0x23C);
    }

    // The length is a local of the frame and fits the short form, so a long-form encoding
    // here would be four bytes of someone else's instruction.
    [Fact]
    public void WritesTheLengthThroughTheShortDisplacementForm()
    {
        var code = Build();

        code[5..7].ShouldBe([0x89, 0x45]);
        ((sbyte)code[7]).ShouldBe((sbyte)-0x14);
    }

    [Fact]
    public void RejoinsTheClientPastTheCodeItReplaced()
    {
        var code = Build();

        code[^5].ShouldBe(InlineHook.JumpOpcode);
        (Cave + code.Length + BitConverter.ToInt32(code, code.Length - 4)).ShouldBe(Rejoin);
    }

    // Nothing is pushed or popped, so a register the client still needed would be lost.
    // eax and edx are scratch across the replaced code, and nothing else is touched.
    [Fact]
    public void TouchesOnlyTheScratchRegisters()
    {
        var code = BytePattern.Format(Build());

        code.ShouldNotContain("60");  // pushad, which would be unbalanced without popad
        code.ShouldNotContain("61");
    }

    private static byte[] Build() =>
        MorphTablePatch.BuildShellcode(Cave, Rejoin, Buffer, Length);

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
