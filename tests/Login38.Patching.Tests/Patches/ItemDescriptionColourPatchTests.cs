using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the description colour cave, byte for byte.
/// </summary>
/// <remarks>
/// The expected sequence comes from transcribing the reference's
/// <c>build_line_color_cave</c> and running it for a cave at <c>0x0A000000</c> with scratch
/// at <c>0x0B000000</c>. This runs on every line of every item description, entered by a
/// call with the caller's arguments still on the stack — a wrong stack displacement forwards
/// the wrong argument, and the text still draws.
/// </remarks>
public sealed class ItemDescriptionColourPatchTests
{
    private static readonly GameAddress Cave = new(0x0A00_0000);

    private static readonly GameAddress Scratch = new(0x0B00_0000);

    private const string Expected =
        "56 57 8B 74 24 10 8B 4C 24 14 81 F9 FF 00 00 00 76 05 B9 FF 00 00 00 8B D1 BF 00 00 00 0B F3 A4 " +
        "C6 07 00 B8 00 00 00 0B 8A 08 84 C9 74 12 80 F9 5C 75 0A 80 78 01 46 75 04 C6 40 01 66 40 EB E8 " +
        "6A 00 8B 44 24 24 50 8B 44 24 24 50 8B 44 24 24 50 B8 00 00 00 0B 50 8B 44 24 20 50 E8 8F E0 46 " +
        "F6 83 C4 18 5F 5E C3";

    [Fact]
    public void MatchesTheReferenceByteForByte() => Build().ShouldBe(Parse(Expected));

    // Callee-saved, and both are used to copy the line. Losing either corrupts the caller,
    // which is the client's own text layout code.
    [Fact]
    public void SavesAndRestoresTheRegistersItBorrows()
    {
        var code = Build();

        code[..2].ShouldBe([0x56, 0x57]);          // push esi; push edi
        code[^3..].ShouldBe([0x5F, 0x5E, 0xC3]);   // pop edi; pop esi; ret
    }

    // Both functions are cdecl. The six original arguments are the caller's to clean; the
    // six this pushes are its own.
    [Fact]
    public void CleansOnlyTheArgumentsItPushed()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("83 C4 18");            // add esp, 0x18 — six dwords
        code.ShouldEndWith("5F 5E C3");            // and a bare ret
    }

    // A description with a length the client got wrong would otherwise write past the end
    // of the scratch and into whatever follows it.
    [Fact]
    public void ClampsTheLineToTheScratchItCopiesInto() =>
        BytePattern.Format(Build()).ShouldContain("81 F9 FF 00 00 00 76 05 B9 FF 00 00 00");

    // The renderer reads to a terminator rather than taking a length, so the copy has to
    // end in one — otherwise it draws whatever was in the scratch last time.
    [Fact]
    public void TerminatesTheCopy() =>
        BytePattern.Format(Build()).ShouldContain("F3 A4 C6 07 00");

    // Operators write both cases and neither should be visible to a player. Rewritten in
    // the copy, so the client's own string is never touched.
    [Fact]
    public void NormalisesTheUppercaseFormInTheCopy()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("80 F9 5C");            // cmp cl, '\'
        code.ShouldContain("80 78 01 46");         // cmp byte [eax+1], 'F'
        code.ShouldContain("C6 40 01 66");         // mov byte [eax+1], 'f'
    }

    // The normaliser walks the copy, not the client's string. Two different addresses would
    // mean it either rewrote the original or normalised nothing.
    [Fact]
    public void NormalisesTheSameBufferItCopiedInto()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain($"BF 00 00 00 0B");     // mov edi, scratch — the copy destination
        code.ShouldContain($"B8 00 00 00 0B 8A 08");  // mov eax, scratch — where the walk starts
    }

    // Non-zero takes the renderer's branch that strips the code and then skips the colour,
    // so the text comes out clean and white — which looks like the patch did nothing.
    [Fact]
    public void PassesTheFlagThatMakesTheRendererSetTheColour()
    {
        var code = Build();
        var pushFlag = BytePattern.Format(code).IndexOf("6A 00", StringComparison.Ordinal);

        pushFlag.ShouldBeGreaterThan(0);

        // Pushed first, so it is the renderer's last argument.
        BytePattern.Format(code)[pushFlag..].ShouldStartWith("6A 00 8B 44 24 24 50");
    }

    // Colour, y and x, each read from one dword further down as the previous push moves the
    // stack. Three identical instructions that must read three different arguments.
    [Fact]
    public void ForwardsThreeArgumentsThroughTheSameDisplacement() =>
        BytePattern.Format(Build())
            .ShouldContain("8B 44 24 24 50 8B 44 24 24 50 8B 44 24 24 50");

    // The surface is read after five pushes, so its displacement differs from the other
    // three. Getting this one wrong draws onto the wrong target.
    [Fact]
    public void ReadsTheSurfaceAtItsOwnDisplacement() =>
        BytePattern.Format(Build()).ShouldContain("8B 44 24 20 50 E8");

    [Fact]
    public void CallsTheRendererThatUnderstandsColourCodes()
    {
        var code = Build();

        // The tail is the call, then add esp and the three-byte epilogue.
        var callAt = code.Length - (5 + 3 + 3);

        code[callAt].ShouldBe((byte)0xE8);
        (Cave + (callAt + 5 + BitConverter.ToInt32(code, callAt + 1))).ShouldBe(new GameAddress(0x0046_E0F0));
    }

    [Fact]
    public void FitsTheCaveItIsWrittenInto() => Build().Length.ShouldBeLessThanOrEqualTo(0x100);

    private static byte[] Build() => ItemDescriptionColourPatch.BuildShellcode(Cave, Scratch);

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
