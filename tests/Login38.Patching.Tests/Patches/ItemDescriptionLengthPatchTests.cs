using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the replacement description wrapper, byte for byte.
/// </summary>
/// <remarks>
/// The expected sequence comes from transcribing the reference's
/// <c>build_split_sidecar_cave</c> and running it for a cave at <c>0x0A000000</c> with its
/// records at <c>0x0B000000</c>. This one replaces a function outright rather than adding to
/// one, so a wrong frame or a missed register leaves the client running with a corrupt stack
/// — which does not fail where the mistake is.
/// </remarks>
public sealed class ItemDescriptionLengthPatchTests
{
    private static readonly GameAddress Cave = new(0x0A00_0000);

    private static readonly GameAddress State = new(0x0B00_0000);

    private const string Expected =
        "55 8B EC 83 EC 04 56 57 53 8B 45 08 85 C0 75 12 8B 4D 0C C7 01 00 00 00 00 33 C0 5B 5F 5E 8B E5 " +
        "5D C3 C7 45 FC 17 00 00 00 8B 75 0C 8B C6 C1 E8 02 83 E0 3F C1 E0 0C BF 00 00 00 0B 03 F8 89 37 " +
        "8B 45 08 89 47 04 50 E8 E0 A0 79 F6 83 C4 04 6A 00 6A 00 8D 4D FC 51 8D 57 10 52 50 8B 45 08 50 " +
        "E8 AB AE 42 F6 83 C4 18 89 47 08 8B D8 BA 13 00 00 00 81 7D 04 4A F0 4A 00 74 14 81 7D 04 C3 F1 " +
        "4A 00 74 0B 81 7D 04 02 AA 45 00 74 02 EB 05 BA 1F 00 00 00 3B DA 76 02 8B DA 89 5F 0C 8B 4D 10 " +
        "89 19 33 C9 3B CB 7D 0E 0F BF 44 4F 12 8B 55 0C 89 04 8A 41 EB EE 33 C9 8B 55 08 8A 04 0A 84 C0 " +
        "74 0B 3C 0A 75 04 C6 04 0A 20 41 EB EB 8B 45 08 50 FF 15 64 86 8C 00 83 C4 04 89 47 04 5B 5F 5E " +
        "8B E5 5D C3";

    [Fact]
    public void MatchesTheReferenceByteForByte() => Build().ShouldBe(Parse(Expected));

    // The client's own wrapper is cdecl and so is this. Returning with the wrong stack, or
    // with esi/edi/ebx changed, corrupts the caller rather than this.
    [Fact]
    public void KeepsTheCallingConventionItReplaces()
    {
        var code = Build();

        code[..9].ShouldBe([0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x04, 0x56, 0x57, 0x53]);
        code[^7..].ShouldBe([0x5B, 0x5F, 0x5E, 0x8B, 0xE5, 0x5D, 0xC3]);
    }

    // Both exits restore the same registers. The early one is easy to write without them,
    // and the caller would go wrong long after the item with no description.
    [Fact]
    public void RestoresTheSameRegistersOnBothPaths()
    {
        var code = BytePattern.Format(Build());
        var epilogue = "5B 5F 5E 8B E5 5D C3";

        code.Split(epilogue).Length.ShouldBe(3);   // two occurrences
    }

    // No description at all is ordinary — most items have none. Zero lines, not a call into
    // the helper with a null string.
    [Fact]
    public void ReportsNoLinesForNoText()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("8B 45 08 85 C0 75");                  // test the text, branch if it exists
        code.ShouldContain("8B 4D 0C C7 01 00 00 00 00 33 C0");   // *out_count = 0; return 0
    }

    // The whole point: the helper writes into a page of the launcher's own, not into the
    // caller's thirty-two-slot table below the stack cookie.
    [Fact]
    public void GivesTheHelperTheRecordRatherThanTheCallersTable()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("BF 00 00 00 0B 03 F8");   // mov edi, state; add edi, eax
        code.ShouldContain("8D 57 10 52");            // lea edx, [edi+0x10]; push edx
    }

    // Keyed on the table pointer because that is the only value the later rendering will
    // also have. Reading the pointer any other way finds a different record.
    [Fact]
    public void SelectsTheRecordFromTheCallersTablePointer()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("8B 75 0C");                     // mov esi, [ebp+0xC]
        code.ShouldContain("8B C6 C1 E8 02 83 E0 3F C1 E0 0C");
        code.ShouldContain("89 37");                        // mov [edi], esi — the tag
    }

    // The record has to survive the call that produced it, and the caller's string does not.
    [Fact]
    public void KeepsACopyOfTheTextInTheRecord()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("FF 15 64 86 8C 00");   // call dword [strdup]
        code.ShouldContain("83 C4 04 89 47 04");   // and the copy replaces the caller's pointer
    }

    // Nineteen unless the return address says thirty-two slots. Getting the default the other
    // way round overflows the small tables, which is a heap fault somewhere else entirely.
    [Fact]
    public void CapsAtTheSmallTableUnlessTheCallerIsKnown()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("BA 13 00 00 00");   // mov edx, 19 — before any comparison
        code.ShouldContain("BA 1F 00 00 00");   // mov edx, 31

        code.IndexOf("BA 13 00 00 00", StringComparison.Ordinal)
            .ShouldBeLessThan(code.IndexOf("BA 1F 00 00 00", StringComparison.Ordinal));
    }

    // The three callers that clear thirty-two slots, each recognised by its return address.
    [Theory]
    [InlineData("81 7D 04 4A F0 4A 00")]
    [InlineData("81 7D 04 C3 F1 4A 00")]
    [InlineData("81 7D 04 02 AA 45 00")]
    public void RecognisesEachLargeTableCaller(string comparison) =>
        BytePattern.Format(Build()).ShouldContain(comparison);

    // A description shorter than the cap must not have the cap written to the caller as its
    // count — the table past the real end holds nothing.
    [Fact]
    public void ClampsRatherThanPads() =>
        BytePattern.Format(Build()).ShouldContain("3B DA 76 02 8B DA");   // cmp; jbe; mov ebx,edx

    // The helper writes words and the caller's table holds dwords, so the copy widens. The
    // first word of the helper's table is not a break, hence 0x12 rather than 0x10.
    [Fact]
    public void WidensTheBreaksIntoTheCallersTable() =>
        BytePattern.Format(Build()).ShouldContain("0F BF 44 4F 12 8B 55 0C 89 04 8A 41");

    // The record keeps the count the helper actually returned, not the clamped one — that is
    // what a later pass would need to draw the lines past the cap.
    [Fact]
    public void RecordsBothCounts()
    {
        var code = BytePattern.Format(Build());

        code.ShouldContain("89 47 08 8B D8");   // mov [edi+8], eax — full
        code.ShouldContain("89 5F 0C");         // mov [edi+0xC], ebx — clamped
    }

    // The breaks are the line structure now. A newline left in the text would draw as a box
    // and break a line the wrapper never chose.
    [Fact]
    public void FlattensNewlinesIntoSpaces() =>
        BytePattern.Format(Build()).ShouldContain("3C 0A 75 04 C6 04 0A 20");

    [Fact]
    public void FitsTheCaveItIsWrittenInto() => Build().Length.ShouldBeLessThanOrEqualTo(0x200);

    private static byte[] Build() => ItemDescriptionLengthPatch.BuildShellcode(Cave, State);

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
