using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers the code that draws damage numbers.
/// </summary>
/// <remarks>
/// <para>
/// Pinned against an independent transcription of the reference's builder. Everything the
/// four-byte fields hold depends on where the cave landed, so those are matched as
/// "anything" and checked separately — what is pinned here is the instructions, which is
/// where a mistake would be a crash inside somebody else's packet handler.
/// </para>
/// <para>
/// The one thing not pinned is where each block sits in the cave. The reference emits two
/// further detours that nothing ever jumps into, so its offsets are not this port's.
/// </para>
/// </remarks>
public sealed class DamageCaveTests
{
    private static readonly GameAddress Cave = new(0x0300_0000);
    private static readonly GameAddress Tick = new(0x7654_3210);

    /// <summary>Stands for a four-byte field whose value depends on where the cave is.</summary>
    private const int Any = -1;

    [Fact]
    public void WatchesOneSwingAtOneThing()
    {
        int[] expected =
        [
            0x9C, 0x60,                                     // pushfd; pushad
            0x89, 0x25, Any, Any, Any, Any,                 // mov [stack_save], esp
            0x8B, 0x55, 0xEC,                               // mov edx, [ebp-0x14]   who swung

            .. OnlyMine,

            0x8B, 0x4D, 0xF0,                               // mov ecx, [ebp-0x10]   what was hit
            0x0F, 0xB7, 0x55, 0xCC,                         // movzx edx, word [ebp-0x34]  how much
            0x85, 0xC9,
            0x0F, 0x84, Any, Any, Any, Any,                 // nothing hit
            0x85, 0xD2,
            0x0F, 0x84, Any, Any, Any, Any,                 // nothing to show

            .. Show,
            .. Leave,

            0x83, 0x7D, 0xE0, 0x00,                         // the displaced test, replayed
            0x0F, 0x8E, Any, Any, Any, Any,                 // ...including where it branches to
            0xE9, Any, Any, Any, Any,                       // and back
        ];

        Block(e => e.Single, expected.Length).ShouldMatch(expected);
    }

    [Fact]
    public void WatchesOneTargetOutOfASpell()
    {
        int[] expected =
        [
            0x8B, 0x10,                                     // mov edx, [eax]    the extra damage
            0x83, 0xC0, 0x04,                               // add eax, 4        step the cursor past it
            0x9C, 0x60,
            0x89, 0x25, Any, Any, Any, Any,
            0x89, 0x15, Any, Any, Any, Any,                 // mov [damage], edx
            0x83, 0x3D, Any, Any, Any, Any, 0x00,           // cmp dword ptr [enabled], 0
            0x0F, 0x84, Any, Any, Any, Any,                 // display disabled
            0x8B, 0x55, 0xE4,                               // mov edx, [ebp-0x1C]   who cast it

            .. OnlyMine,

            0x8B, 0x75, 0xBC,                               // mov esi, [ebp-0x44]   the hits
            0x85, 0xF6,
            0x0F, 0x84, Any, Any, Any, Any,
            0x8B, 0x9D, 0x10, 0xFE, 0xFF, 0xFF,             // mov ebx, [ebp-0x1F0]  which one
            0x8B, 0x7E, 0x08,                               // mov edi, [esi+8]      the hit flags
            0x85, 0xFF,
            0x0F, 0x84, Any, Any, Any, Any,
            0x0F, 0xB7, 0x04, 0x5F,                         // movzx eax, word [edi+ebx*2]
            0x85, 0xC0,
            0x0F, 0x84, Any, Any, Any, Any,                 // a miss
            0x8B, 0x7E, 0x04,                               // mov edi, [esi+4]      the targets
            0x85, 0xFF,
            0x0F, 0x84, Any, Any, Any, Any,
            0x8B, 0x0C, 0x9F,                               // mov ecx, [edi+ebx*4]
            0x85, 0xC9,
            0x0F, 0x84, Any, Any, Any, Any,
            0x8B, 0x15, Any, Any, Any, Any,                 // mov edx, [damage]
            0x85, 0xD2,
            0x0F, 0x8E, Any, Any, Any, Any,                 // nothing to show

            .. Show,
            .. Leave,

            0x83, 0xC4, 0x10,                               // the displaced pair, replayed
            0x89, 0x45, 0xD0,
            0xE9, Any, Any, Any, Any,
        ];

        Block(e => e.Area, expected.Length).ShouldMatch(expected);
    }

    // Digits come out of a division backwards, so they go on the stack and come off again.
    // Zero produces none at all and is the one case written directly.
    [Fact]
    public void WritesANumberAsDecimal()
    {
        byte[] expected =
        [
            0x53, 0x51, 0x52, 0x56,                         // push ebx; ecx; edx; esi
            0x31, 0xC9,                                     // xor ecx, ecx     how many digits
            0xBB, 0x0A, 0x00, 0x00, 0x00,                   // mov ebx, 10
            0x85, 0xC0,
            0x75, 0x06,                                     // jnz digits
            0xC6, 0x07, 0x30,                               // mov byte [edi], '0'
            0x47,                                           // inc edi
            0xEB, 0x13,                                     // jmp done
            0x31, 0xD2,                                     // digits: xor edx, edx
            0xF7, 0xF3,                                     // div ebx
            0x80, 0xC2, 0x30,                               // add dl, '0'
            0x52,                                           // push edx
            0x41,                                           // inc ecx
            0x85, 0xC0,
            0x75, 0xF3,                                     // jnz digits
            0x5A,                                           // write: pop edx
            0x88, 0x17,                                     // mov [edi], dl
            0x47,                                           // inc edi
            0xE2, 0xFA,                                     // loop write
            0x5E, 0x5A, 0x59, 0x5B,                         // done: pop esi; edx; ecx; ebx
            0xC3,
        ];

        var (code, entries) = DamageCave.Build(Cave, Tick);

        code[entries.Formatter..entries.Length].ShouldBe(expected);
    }

    // Both detours call one copy of it rather than carrying one each. Four calls: one per
    // number in the line, from each of the two detours.
    [Fact]
    public void SharesOneCopyOfTheFormatter()
    {
        var (code, entries) = DamageCave.Build(Cave, Tick);

        CallsToTheFormatter(code, entries).ShouldBe(4);
    }

    [Fact]
    public void HandsTheLineToTheClientsOwnRoutine()
    {
        int[] expected =
        [
            0x6A, 0x00, 0x6A, 0x00, 0x6A, 0x01,             // the three the client wants last
            0x68, 0x00, 0xF8, 0x00, 0x00,                   // push red
            0x68, Any, Any, Any, Any,                       // push text
            0xFF, 0x35, Any, Any, Any, Any,                 // push [target]
            0xB8, 0xB0, 0xB7, 0x42, 0x00,                   // mov eax, 0x0042B7B0
            0xFF, 0xD0,                                     // call eax
            0x83, 0xC4, 0x18,                               // six arguments back off
        ];

        Show[^expected.Length..].ShouldBe(expected);
    }

    [Fact]
    public void FitsInWhatIsReservedForIt()
    {
        var (code, _) = DamageCave.Build(Cave, Tick);

        code.Length.ShouldBeLessThan(DamageCave.CaveSize);
    }

    // Everything the code refers to by absolute address has to be inside the allocation,
    // or the detour writes over whatever else is at that address.
    [Fact]
    public void KeepsEverythingItRemembersInsideItsOwnAllocation()
    {
        var (code, entries) = DamageCave.Build(Cave, Tick);
        var data = new GameAddress(Cave.Value + (uint)entries.Length);

        code.Length.ShouldBeGreaterThan(entries.Length);
        data.Value.ShouldBeGreaterThanOrEqualTo(Cave.Value);
        (Cave.Value + (uint)code.Length).ShouldBeLessThanOrEqualTo(Cave.Value + DamageCave.CaveSize);
    }

    // Two clients side by side must not pick the same sequence of colours, and where the
    // cave landed is the one number to hand that differs between them.
    [Fact]
    public void StartsTheColourPickerSomewhereThatDependsOnTheGame()
    {
        var (one, entries) = DamageCave.Build(Cave, Tick);
        var (two, _) = DamageCave.Build(new GameAddress(0x0400_0000), Tick);

        BitConverter.ToUInt32(one, entries.Length + (4 * 4))
            .ShouldNotBe(BitConverter.ToUInt32(two, entries.Length + (4 * 4)));
    }

    [Fact]
    public void PutsTheClientsOwnClockRoutineWhereTheCodeLooksForIt()
    {
        var (code, entries) = DamageCave.Build(Cave, Tick);

        BitConverter.ToUInt32(code, entries.Length + (11 * 4)).ShouldBe(Tick.Value);
    }

    [Fact]
    public void LeavesRoomForTheLineBeingBuilt()
    {
        var (code, entries) = DamageCave.Build(Cave, Tick);

        (code.Length - entries.Length).ShouldBe((13 * 4) + DamageCave.TextLength);
    }

    /// <summary>
    /// Gives up unless the player is the one doing the hitting.
    /// </summary>
    /// <remarks>
    /// Three ids, because which one a packet carries depends on which path it came in on.
    /// </remarks>
    private static int[] OnlyMine =>
    [
        0x8B, 0x0D, 0xB4, 0xF4, 0xAB, 0x00,                 // mov ecx, [0x00ABF4B4]
        0x39, 0xCA,
        0x0F, 0x84, Any, Any, Any, Any,                     // it is the player
        0xA1, 0xB8, 0xD2, 0xC2, 0x00,                       // mov eax, [0x00C2D2B8]
        0x85, 0xC0,
        0x0F, 0x84, Any, Any, Any, Any,                     // no record to compare against
        0x8B, 0x48, 0x0C,                                   // mov ecx, [eax+0x0C]
        0x39, 0xCA,
        0x0F, 0x84, Any, Any, Any, Any,                     // it is the player
        0x8B, 0x48, 0x14,                                   // mov ecx, [eax+0x14]
        0x39, 0xCA,
        0x0F, 0x85, Any, Any, Any, Any,                     // somebody else's hit
    ];

    /// <summary>Adds the hit to the running total and puts the result on screen.</summary>
    private static int[] Show =>
    [
        0x89, 0x15, Any, Any, Any, Any,                     // mov [damage], edx
        0x89, 0x0D, Any, Any, Any, Any,                     // mov [target], ecx

        0x51, 0x52,                                         // the clock may use these
        0xFF, 0x15, Any, Any, Any, Any,                     // call [gettickcount]
        0x5A, 0x59,
        0x89, 0xC3,                                         // mov ebx, eax
        0x2B, 0x1D, Any, Any, Any, Any,                     // sub ebx, [last_tick]
        0x3B, 0x0D, Any, Any, Any, Any,                     // cmp ecx, [last_target]
        0x0F, 0x85, Any, Any, Any, Any,                     // something else now
        0x81, 0xFB, 0x40, 0x1F, 0x00, 0x00,                 // cmp ebx, 8000
        0x0F, 0x87, Any, Any, Any, Any,                     // the fight is over
        0x01, 0x15, Any, Any, Any, Any,                     // add [last_total], edx
        0xE9, Any, Any, Any, Any,
        0x89, 0x0D, Any, Any, Any, Any,                     // start again: mov [last_target], ecx
        0x89, 0x15, Any, Any, Any, Any,                     // mov [last_total], edx
        0xA3, Any, Any, Any, Any,                           // mov [last_tick], eax
        0xA1, Any, Any, Any, Any,                           // mov eax, [last_total]
        0xA3, Any, Any, Any, Any,                           // mov [total], eax

        0xBF, Any, Any, Any, Any,                           // mov edi, text
        0xC7, 0x07, 0x5C, 0x5C, 0x66, 0x52,                 // "\\fR"
        0xC7, 0x47, 0x04, 0x66, 0x3E, 0x28, 0x20,           // "f>( "
        0xC7, 0x47, 0x08, 0x5C, 0x5C, 0x66, 0x52,           // "\\fR"
        0x66, 0xC7, 0x47, 0x0C, 0x66, 0x30,                 // "f0"
        0x83, 0xC7, 0x0E,
        0xA1, Any, Any, Any, Any,                           // mov eax, [random]
        0x69, 0xC0, 0xFD, 0x43, 0x03, 0x00,                 // imul eax, eax, 0x343FD
        0x03, 0x05, Any, Any, Any, Any,                     // add eax, [damage]
        0x03, 0x05, Any, Any, Any, Any,                     // add eax, [target]
        0x05, 0xC3, 0x9E, 0x26, 0x00,
        0xA3, Any, Any, Any, Any,                           // mov [random], eax
        0xC1, 0xE8, 0x08,
        0x83, 0xE0, 0x03,
        0x8A, 0x98, Any, Any, Any, Any,                     // mov bl, [colours+eax]
        0x88, 0x5F, 0xFF,                                   // over the digit just written
        0xA1, Any, Any, Any, Any,                           // mov eax, [damage]
        0xE8, Any, Any, Any, Any,
        0xC7, 0x07, 0x5C, 0x5C, 0x66, 0x52,                 // "\\fR"
        0xC7, 0x47, 0x04, 0x66, 0x3E, 0x20, 0x29,           // "f> )"
        0xC6, 0x47, 0x08, 0x20,                             // " "
        0x83, 0xC7, 0x09,
        0xA1, Any, Any, Any, Any,                           // mov eax, [total]
        0xE8, Any, Any, Any, Any,
        0xC6, 0x07, 0x00,

        0x6A, 0x00, 0x6A, 0x00, 0x6A, 0x01,
        0x68, 0x00, 0xF8, 0x00, 0x00,
        0x68, Any, Any, Any, Any,
        0xFF, 0x35, Any, Any, Any, Any,
        0xB8, 0xB0, 0xB7, 0x42, 0x00,
        0xFF, 0xD0,
        0x83, 0xC4, 0x18,
    ];

    /// <summary>Puts back what the client will expect, whether anything was drawn or not.</summary>
    private static int[] Leave =>
    [
        0x8B, 0x25, Any, Any, Any, Any,                     // mov esp, [stack_save]
        0x61, 0x9D,                                         // popad; popfd
    ];

    /// <summary>
    /// How many calls in the cave land on the formatter.
    /// </summary>
    /// <remarks>
    /// Counted by where they resolve to rather than by the opcode alone. <c>0xE8</c> turns
    /// up inside other instructions — <c>shr eax, 8</c> is <c>C1 E8 08</c> — but one that
    /// resolves to exactly the formatter is a call to it and nothing else.
    /// </remarks>
    private static int CallsToTheFormatter(byte[] code, DamageCave.Entries entries)
    {
        var found = 0;

        for (var at = 0; at + 5 <= entries.Formatter; at++)
        {
            if (code[at] == 0xE8 && at + 5 + BitConverter.ToInt32(code, at + 1) == entries.Formatter)
            {
                found++;
            }
        }

        return found;
    }

    private static byte[] Block(Func<DamageCave.Entries, int> start, int length)
    {
        var (code, entries) = DamageCave.Build(Cave, Tick);
        var from = start(entries);

        return code[from..(from + length)];
    }
}

/// <summary>Matching machine code against a pattern with holes in it.</summary>
internal static class PatternAssertions
{
    /// <summary>
    /// Asserts that bytes match a pattern, where a negative entry stands for any byte.
    /// </summary>
    /// <remarks>
    /// The holes are the four-byte fields holding addresses that depend on where the cave
    /// landed. Everything else is the instruction stream and is pinned exactly.
    /// </remarks>
    public static void ShouldMatch(this byte[] actual, int[] pattern)
    {
        actual.Length.ShouldBe(pattern.Length);

        for (var at = 0; at < pattern.Length; at++)
        {
            if (pattern[at] >= 0)
            {
                actual[at].ShouldBe((byte)pattern[at], $"byte {at} of the block");
            }
        }
    }
}
