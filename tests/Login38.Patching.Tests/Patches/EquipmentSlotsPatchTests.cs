using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the code this installs against what the Rust build emits, byte for byte.
/// </summary>
/// <remarks>
/// The expected sequences come from transcribing the reference's builders and running
/// them for a lookup cave at <c>0x20000000</c>, a setup cave at <c>0x21000000</c> and a
/// helper at <c>0x00795500</c>.
/// </remarks>
public sealed class EquipmentSlotsPatchTests
{
    private static readonly GameAddress LookupCave = new(0x2000_0000);
    private static readonly GameAddress SetupCave = new(0x2100_0000);
    private static readonly GameAddress SlotHelper = new(0x0079_5500);

    private const string ExpectedIndexTranslation =
        "55 8B EC 8B 45 08 83 F8 1F 77 0F 85 C0 74 0B 0F B6 80 20 00 00 20 5D C2 04 00 31 C0 5D " +
        "C2 04 00 00 02 05 04 06 0B 07 09 0E 10 03 08 01 00 00 00 00 00 0A 0C 0D 0F 11 12 13 10 " +
        "2E 2F 30 31 32 33";

    private const string ExpectedAppendedSlots =
        "C7 45 F8 00 00 00 00 83 7D F8 06 7D 1C 6A 00 6A 00 8B 55 F8 83 C2 2E 52 8B 45 FC 50 8B " +
        "4D F4 E8 DC 54 79 DF FF 45 F8 EB DE 8B E5 5D C3";

    private const string ExpectedBackgroundIndex = "8B 4D 0C 83 F9 2E 7C 04 83 C1 06 C3 83 C1 1A C3";

    [Fact]
    public void IndexTranslationMatchesTheReferenceByteForByte() =>
        EquipmentSlotsPatch.BuildIndexTranslation(LookupCave).ShouldBe(Parse(ExpectedIndexTranslation));

    [Fact]
    public void AppendedSlotsMatchTheReferenceByteForByte() =>
        EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper).ShouldBe(Parse(ExpectedAppendedSlots));

    [Fact]
    public void BackgroundIndexMatchesTheReferenceByteForByte() =>
        EquipmentSlotsPatch.BuildBackgroundIndex().ShouldBe(Parse(ExpectedBackgroundIndex));

    // The lookup addresses its table as a literal computed from the code length. If the
    // code grew or shrank, the literal would point into the middle of it and every slot
    // would translate to a byte of machine code.
    [Fact]
    public void LookupAddressesTheTableThatFollowsIt()
    {
        var code = EquipmentSlotsPatch.BuildIndexTranslation(LookupCave);
        var load = IndexOf(code, [0x0F, 0xB6, 0x80]);

        load.ShouldBeGreaterThanOrEqualTo(0);
        var table = BitConverter.ToUInt32(code, load + 3);

        var tableStart = (int)(table - LookupCave.Value);
        code[tableStart..].ShouldBe(EquipmentSlotsPatch.SlotLookup.ToArray());
    }

    // Out-of-range and zero both mean "no slot". Returning a table byte for either would
    // read past the table or return the unused entry at index zero by accident.
    [Theory]
    [InlineData(0x77)]  // ja  — above 31
    [InlineData(0x74)]  // jz  — index zero
    public void LookupRejectsIndicesItCannotTranslate(byte branch)
    {
        var code = EquipmentSlotsPatch.BuildIndexTranslation(LookupCave);
        var at = Array.IndexOf(code, branch);

        at.ShouldBeGreaterThan(0);
        var landing = at + 2 + code[at + 1];

        // xor eax, eax — the "no slot" answer.
        code.AsSpan(landing, 2).ToArray().ShouldBe([0x31, 0xC0]);
    }

    // __stdcall with one argument: the callee pops it. A bare ret would leak four bytes
    // of the caller's stack on every equipment change.
    [Fact]
    public void LookupPopsItsArgumentOnBothPaths()
    {
        var code = EquipmentSlotsPatch.BuildIndexTranslation(LookupCave);

        CountOf(code, [0x5D, 0xC2, 0x04, 0x00]).ShouldBe(2);
    }

    [Fact]
    public void LookupCoversEveryServerIndex() =>
        EquipmentSlotsPatch.SlotLookup.Length.ShouldBe(32);

    // Every mapped slot has to be distinct, or two equipped items land on the same square
    // and one of them is invisible. Arrows and trousers share by the client's own design.
    [Fact]
    public void MappedSlotsAreDistinctApartFromTheClientsOwnOverlap()
    {
        var mapped = EquipmentSlotsPatch.SlotLookup.ToArray().Where(slot => slot != 0).ToList();

        var duplicates = mapped.GroupBy(slot => slot).Where(g => g.Count() > 1).Select(g => g.Key);

        duplicates.ShouldBe([16]);
    }

    [Fact]
    public void AppendedSlotsCallTheClientsOwnHelper()
    {
        var code = EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper);
        var call = Array.IndexOf(code, (byte)0xE8);

        call.ShouldBeGreaterThan(0);
        var target = unchecked((uint)(SetupCave.Value + call + 5 + BitConverter.ToInt32(code, call + 1)));

        target.ShouldBe(SlotHelper.Value);
    }

    // This replaces the function's epilogue, so it has to end with the epilogue it took.
    [Fact]
    public void AppendedSlotsPerformTheEpilogueTheyReplaced() =>
        EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper)[^4..]
            .ShouldBe([0x8B, 0xE5, 0x5D, 0xC3]);

    [Fact]
    public void AppendedSlotsLoopExactlySixTimes()
    {
        var code = EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper);
        var compare = IndexOf(code, [0x83, 0x7D, 0xF8]);

        compare.ShouldBeGreaterThanOrEqualTo(0);
        code[compare + 3].ShouldBe((byte)6);
    }

    // The loop has to reach its own top, not somewhere in the middle of an instruction.
    [Fact]
    public void AppendedSlotsJumpBackToTheComparison()
    {
        var code = EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper);
        var jump = Array.LastIndexOf(code, (byte)0xEB);

        var landing = jump + 2 + (sbyte)code[jump + 1];

        landing.ShouldBe(IndexOf(code, [0x83, 0x7D, 0xF8, 0x06]));
    }

    [Fact]
    public void AppendedSlotsExitPastTheLoopBody()
    {
        var code = EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper);

        // Located from the comparison it follows: a bare search for 0x7D finds the
        // ModR/M byte of that comparison first.
        var exit = IndexOf(code, [0x83, 0x7D, 0xF8, 0x06]) + 4;
        code[exit].ShouldBe((byte)0x7D, "the comparison should be followed by jge");

        var landing = exit + 2 + code[exit + 1];

        code.AsSpan(landing, 4).ToArray().ShouldBe([0x8B, 0xE5, 0x5D, 0xC3]);
    }

    // Both paths return, and the client's slots keep the offset they always had.
    [Fact]
    public void BackgroundIndexKeepsTheOriginalOffsetForOriginalSlots()
    {
        var code = EquipmentSlotsPatch.BuildBackgroundIndex();
        var branch = Array.IndexOf(code, (byte)0x7C);

        var landing = branch + 2 + code[branch + 1];

        code.AsSpan(landing, 4).ToArray().ShouldBe([0x83, 0xC1, 0x1A, 0xC3]);
        code[^1].ShouldBe((byte)0xC3);
    }

    // Relocating the caves has to change the code, or the builders are ignoring where the
    // code will actually live.
    [Fact]
    public void RelocatingTheCavesChangesTheCode()
    {
        EquipmentSlotsPatch.BuildIndexTranslation(new GameAddress(0x3000_0000))
            .ShouldNotBe(EquipmentSlotsPatch.BuildIndexTranslation(LookupCave));

        EquipmentSlotsPatch.BuildAppendedSlots(new GameAddress(0x3100_0000), SlotHelper)
            .ShouldNotBe(EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper));
    }

    [Fact]
    public void EveryBlockFitsItsAllocation()
    {
        EquipmentSlotsPatch.BuildIndexTranslation(LookupCave).Length.ShouldBeLessThanOrEqualTo(64);
        EquipmentSlotsPatch.BuildAppendedSlots(SetupCave, SlotHelper).Length.ShouldBeLessThanOrEqualTo(80);
        EquipmentSlotsPatch.BuildBackgroundIndex().Length.ShouldBeLessThanOrEqualTo(48);
    }

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    private static int CountOf(byte[] haystack, byte[] needle)
    {
        var found = 0;

        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                found++;
            }
        }

        return found;
    }
}
