using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the pixel format shellcode against what the Rust build emits, byte for byte.
/// </summary>
/// <remarks>
/// The expected sequences come from transcribing the reference's
/// <c>build_surface_pf_shellcode</c> and running it for a cave at <c>0x07000000</c>
/// rejoining at <c>0x00448347</c>. This code runs inside a surface creation this test
/// cannot reach, and a wrong field displacement writes over the caller's frame rather than
/// failing anywhere visible — so a byte comparison is the only check available.
/// </remarks>
public sealed class SurfacePixelFormatPatchTests
{
    private static readonly GameAddress Cave = new(0x0700_0000);

    private static readonly GameAddress Rejoin = new(0x0044_8347);

    private const string ExpectedAuto =
        "C7 45 E0 40 08 00 00 C7 45 C0 20 00 00 00 C7 45 C4 40 00 00 00 C7 45 CC 10 00 00 00 " +
        "C7 45 D8 1F 00 00 00 A0 5C 23 9A 00 84 C0 74 10 C7 45 D0 00 F8 00 00 C7 45 D4 E0 07 " +
        "00 00 EB 0E C7 45 D0 00 7C 00 00 C7 45 D4 E0 03 00 00 E9 F8 82 44 F9";

    private const string Expected555 =
        "C7 45 E0 40 08 00 00 C7 45 C0 20 00 00 00 C7 45 C4 40 00 00 00 C7 45 CC 10 00 00 00 " +
        "C7 45 D8 1F 00 00 00 C7 45 D0 00 7C 00 00 C7 45 D4 E0 03 00 00 E9 11 83 44 F9";

    private const string Expected565 =
        "C7 45 E0 40 08 00 00 C7 45 C0 20 00 00 00 C7 45 C4 40 00 00 00 C7 45 CC 10 00 00 00 " +
        "C7 45 D8 1F 00 00 00 C7 45 D0 00 F8 00 00 C7 45 D4 E0 07 00 00 E9 11 83 44 F9";

    [Theory]
    [InlineData(SurfaceColourLayout.Auto, ExpectedAuto)]
    [InlineData(SurfaceColourLayout.Rgb555, Expected555)]
    [InlineData(SurfaceColourLayout.Rgb565, Expected565)]
    public void MatchesTheReferenceByteForByte(SurfaceColourLayout layout, string expected) =>
        Build(layout).ShouldBe(Parse(expected));

    // The detour displaces this store, so the cave has to put it back before doing
    // anything else — the client reads those caps immediately after.
    [Theory]
    [InlineData(SurfaceColourLayout.Auto)]
    [InlineData(SurfaceColourLayout.Rgb555)]
    [InlineData(SurfaceColourLayout.Rgb565)]
    public void ReplaysTheDisplacedCapsStoreFirst(SurfaceColourLayout layout) =>
        Build(layout)[..7].ShouldBe([0xC7, 0x45, 0xE0, 0x40, 0x08, 0x00, 0x00]);

    // rel32 is measured from the end of the jump. Off by one here lands in the middle of
    // an instruction in a function that is about to create a surface.
    [Theory]
    [InlineData(SurfaceColourLayout.Auto)]
    [InlineData(SurfaceColourLayout.Rgb555)]
    [InlineData(SurfaceColourLayout.Rgb565)]
    public void ReturnsToTheInstructionAfterTheDetour(SurfaceColourLayout layout)
    {
        var code = Build(layout);

        code[^5].ShouldBe((byte)0xE9);

        var displacement = BitConverter.ToInt32(code, code.Length - 4);
        (Cave + code.Length + displacement).ShouldBe(Rejoin);
    }

    // Every field is written through ebp, which is still the client's frame. A ModR/M byte
    // other than 45 means some other base register, and the frame is the only thing whose
    // contents this knows.
    [Theory]
    [InlineData(SurfaceColourLayout.Rgb555)]
    [InlineData(SurfaceColourLayout.Rgb565)]
    public void WritesEveryFieldThroughTheClientFrame(SurfaceColourLayout layout)
    {
        var code = Build(layout);

        // Seven-byte stores, then the five-byte jump.
        code.Length.ShouldBe(7 * 7 + 5);

        for (var offset = 0; offset < 7 * 7; offset += 7)
        {
            code[offset].ShouldBe((byte)0xC7);
            code[offset + 1].ShouldBe((byte)0x45);
        }
    }

    // Blue is the bottom five bits in both layouts, so only red and green may differ. If
    // more than that changes, one of the two is wrong.
    [Fact]
    public void DiffersBetweenLayoutsOnlyInRedAndGreen()
    {
        var rgb555 = Build(SurfaceColourLayout.Rgb555);
        var rgb565 = Build(SurfaceColourLayout.Rgb565);

        rgb555.Length.ShouldBe(rgb565.Length);

        var differing = Enumerable.Range(0, rgb555.Length).Where(i => rgb555[i] != rgb565[i]).ToArray();

        // One byte inside each of the two mask immediates: red's high byte and green's low.
        differing.ShouldBe([39, 46]);
    }

    // The Auto path decides at run time, so both masks are present and one is jumped over.
    [Fact]
    public void AutoCarriesBothLayouts()
    {
        var code = BytePattern.Format(Build(SurfaceColourLayout.Auto));

        code.ShouldContain("C7 45 D0 00 7C 00 00");
        code.ShouldContain("C7 45 D0 00 F8 00 00");
    }

    // The selector is a byte the client sets to zero for 555. Reading it as anything wider
    // would pick up whatever follows it.
    [Fact]
    public void AutoReadsTheSelectorAsAByte() =>
        BytePattern.Format(Build(SurfaceColourLayout.Auto)).ShouldContain("A0 5C 23 9A 00 84 C0");

    // A branch that lands mid-instruction is the failure this whole layout risks, and it
    // would show up as a crash inside DirectDraw with nothing pointing back here.
    [Fact]
    public void AutoBranchesLandOnInstructionBoundaries()
    {
        var code = Build(SurfaceColourLayout.Auto);

        // mov al,[selector]; test al,al — 7 bytes in, at the end of the shared fields.
        const int TestAt = 5 * 7 + 5;
        code[TestAt].ShouldBe((byte)0x84);

        // jz over the 565 masks, to the 555 masks.
        var jzAt = TestAt + 2;
        code[jzAt].ShouldBe((byte)0x74);
        var rgb555At = jzAt + 2 + code[jzAt + 1];
        code[rgb555At..(rgb555At + 3)].ShouldBe([0xC7, 0x45, 0xD0]);
        code[(rgb555At + 3)..(rgb555At + 5)].ShouldBe([0x00, 0x7C]);

        // jmp from the end of the 565 masks to the tail, past the 555 masks.
        var jmpAt = rgb555At - 2;
        code[jmpAt].ShouldBe((byte)0xEB);
        (jmpAt + 2 + code[jmpAt + 1]).ShouldBe(code.Length - 5);
    }

    [Theory]
    [InlineData(null, SurfaceColourLayout.Auto)]
    [InlineData("", SurfaceColourLayout.Auto)]
    [InlineData("auto", SurfaceColourLayout.Auto)]
    [InlineData("555", SurfaceColourLayout.Rgb555)]
    [InlineData("rgb555", SurfaceColourLayout.Rgb555)]
    [InlineData("565", SurfaceColourLayout.Rgb565)]
    [InlineData("RGB565", SurfaceColourLayout.Rgb565)]
    [InlineData("  565  ", SurfaceColourLayout.Rgb565)]
    public void ReadsTheLayoutOverride(string? value, SurfaceColourLayout expected) =>
        SurfacePixelFormatPatch.ParseLayout(value).ShouldBe(expected);

    // Set by hand on a player's machine. A typo has to mean "carry on as normal", not
    // "refuse to launch".
    [Fact]
    public void TreatsAnUnrecognisedLayoutAsAuto() =>
        SurfacePixelFormatPatch.ParseLayout("16bpp").ShouldBe(SurfaceColourLayout.Auto);

    [Theory]
    [InlineData(null, null)]
    [InlineData("none", null)]
    [InlineData("off", null)]
    [InlineData("555", (byte)0)]
    [InlineData("0", (byte)0)]
    [InlineData("565", (byte)1)]
    [InlineData("1", (byte)1)]
    public void ReadsThePinOverride(string? value, byte? expected) =>
        SurfacePixelFormatPatch.ParsePin(value).ShouldBe(expected);

    // Pinning is a diagnostic, so nothing is the right default: the patch already follows
    // whatever the client chose.
    [Fact]
    public void DoesNotPinTheSelectorByDefault() =>
        SurfacePixelFormatPatch.ParsePin(null).ShouldBeNull();

    private static byte[] Build(SurfaceColourLayout layout) =>
        SurfacePixelFormatPatch.BuildShellcode(Cave, Rejoin, layout);

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
