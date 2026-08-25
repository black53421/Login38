using Login38.Aux.Actions;
using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Actions;

/// <summary>
/// Covers the recorder that catches what the client sends.
/// </summary>
/// <remarks>
/// Pinned byte for byte against an independent transcription. This runs inside the client's
/// own network path, on whatever thread got there, for every packet the game sends.
/// </remarks>
public sealed class PacketSpyCaveTests
{
    /// <summary>Somewhere for the cave to be.</summary>
    private static readonly GameAddress Cave = new(0x0300_0000);

    [Fact]
    public void ClaimsASlotBeforeItWritesToIt()
    {
        byte[] expected =
        [
            0x60,                                 // pushad
            0x9C,                                 // pushfd
            0xFC,                                 // cld
            0xB8, 0x01, 0x00, 0x00, 0x00,         // mov eax, 1
            0xF0, 0x0F, 0xC1, 0x05, 0x00, 0x20, 0x00, 0x03,   // lock xadd [ring index], eax
            0x89, 0xC3,                           // mov ebx, eax     keep the sequence
            0x83, 0xE0, 0x3F,                     // and eax, 0x3F    round the ring
            0xC1, 0xE0, 0x07,                     // shl eax, 7       times the entry size
            0x05, 0x00, 0x00, 0x00, 0x03,         // add eax, buffer
            0x89, 0xC7,                           // mov edi, eax
        ];

        PacketSpyCave.Build(Cave)[..expected.Length].ShouldBe(expected);
    }

    // The reference increments the index after writing, so two threads inside the client's
    // network code compute the same slot, write over each other and both increment.
    [Fact]
    public void UsesALockedExchangeRatherThanAnIncrement()
    {
        var code = PacketSpyCave.Build(Cave);

        code.AsSpan(8, 4).ToArray().ShouldBe([0xF0, 0x0F, 0xC1, 0x05]);   // lock xadd

        for (var at = 0; at + 3 <= code.Length; at++)
        {
            code.AsSpan(at, 3).SequenceEqual<byte>([0xF0, 0xFF, 0x05])
                .ShouldBeFalse($"a locked increment at {at} would be claiming the slot too late");
        }
    }

    // Nothing says which way the direction flag is pointing inside somebody else's
    // function, and a rep movsd that runs backwards writes off the front of the ring.
    [Fact]
    public void ClearsTheDirectionFlagBeforeCopyingAnything()
    {
        var code = PacketSpyCave.Build(Cave);

        code[2].ShouldBe((byte)0xFC);
        Array.IndexOf(code, (byte)0xFC).ShouldBeLessThan(Array.IndexOf(code, (byte)0xF3));
    }

    [Fact]
    public void CopiesTheReturnAddressAndTheArgumentsOffTheStack()
    {
        byte[] expected =
        [
            0x8D, 0x74, 0x24, 0x24,               // lea esi, [esp+0x24]   past pushad and pushfd
            0xB9, 0x10, 0x00, 0x00, 0x00,         // mov ecx, 16
            0xF3, 0xA5,                           // rep movsd
        ];

        PacketSpyCave.Build(Cave)[31..42].ShouldBe(expected);
    }

    [Fact]
    public void FollowsTheFormatOnlyWhereItPointsIntoTheClientsOwnImage()
    {
        byte[] expected =
        [
            0x8B, 0x74, 0x24, 0x28,               // mov esi, [esp+0x28]   the format
            0x85, 0xF6,                           // test esi, esi
            0x74, 0x19,                           // jz skip
            0x81, 0xFE, 0x00, 0x00, 0x40, 0x00,   // cmp esi, 0x00400000
            0x72, 0x11,                           // jb skip
            0x81, 0xFE, 0x00, 0x00, 0x00, 0x10,   // cmp esi, 0x10000000
            0x73, 0x09,                           // jae skip
            0xB9, 0x08, 0x00, 0x00, 0x00,         // mov ecx, 8
            0xF3, 0xA5,                           // rep movsd
            0xEB, 0x03,                           // jmp past
            0x83, 0xC7, 0x20,                     // skip: add edi, 32
        ];

        PacketSpyCave.Build(Cave)[42..78].ShouldBe(expected);
    }

    // All three ways of not having a format leave edi where a copied one would have.
    [Fact]
    public void LeavesTheEntryTheSameLengthWhetherItFollowedTheFormatOrNot()
    {
        var code = PacketSpyCave.Build(Cave);

        (50 + code[49]).ShouldBe(75);
        (58 + code[57]).ShouldBe(75);
        (66 + code[65]).ShouldBe(75);
        (75 + code[74]).ShouldBe(78);
    }

    [Fact]
    public void WritesTheSequenceAndThenTheMagic()
    {
        byte[] expected =
        [
            0x83, 0xC7, 0x18,                     // add edi, 24    to the entry's trailer
            0x89, 0x1F,                           // mov [edi], ebx        the sequence
            0xC7, 0x47, 0x04, 0xCE, 0xFA, 0xED, 0xFE,   // mov [edi+4], magic
        ];

        PacketSpyCave.Build(Cave)[78..90].ShouldBe(expected);
    }

    // The trailer arithmetic has to land exactly on the two fields the reader looks at.
    [Fact]
    public void LandsOnTheFieldsTheReaderReads()
    {
        var afterHeader = PacketSpyCave.HeadOffset;
        var afterFormat = afterHeader + PacketSpyCave.HeadBytes;

        (afterFormat + PacketSpyCave.Build(Cave)[80]).ShouldBe(PacketSpyCave.SequenceOffset);
        (PacketSpyCave.SequenceOffset + 4).ShouldBe(PacketSpyCave.MagicOffset);
        PacketSpyCave.MagicOffset.ShouldBe(PacketSpyCave.EntrySize - 4);
    }

    [Fact]
    public void PutsTheStackBackAndReplaysThePrologue()
    {
        var code = PacketSpyCave.Build(Cave);

        code[90..92].ShouldBe([0x9D, 0x61]);                  // popfd; popad
        code[92..100].ShouldBe(PacketSpyCave.Site.Stock);
        code[100].ShouldBe((byte)0xE9);
        (Cave.Value + PacketSpyCave.CodeOffset + 105 + (uint)BitConverter.ToInt32(code, 101))
            .ShouldBe(PacketSpyCave.Site.Resume.Value);
        code.Length.ShouldBe(105);
    }

    [Fact]
    public void FitsInTheCaveAfterTheRing() =>
        (PacketSpyCave.CodeOffset + PacketSpyCave.Build(Cave).Length)
            .ShouldBeLessThan(PacketSpyCave.CaveSize);

    // The ring, the index and the code have to be laid out so none of them runs into
    // another: the shellcode writes into the first two from inside the client.
    [Fact]
    public void LaysTheRingOutWithoutOverlappingAnything()
    {
        (PacketSpyCave.RingLength * PacketSpyCave.EntrySize).ShouldBe((int)PacketSpyCave.IndexOffset);
        PacketSpyCave.CodeOffset.ShouldBeGreaterThan(PacketSpyCave.IndexOffset + 4);
        PacketSpyCave.Empty().Length.ShouldBe(PacketSpyCave.CaveSize);
    }

    // Eight bytes, so what the detour replays is three whole instructions rather than one
    // and a half.
    [Fact]
    public void DisplacesTheWholePrologue()
    {
        var site = PacketSpyCave.Site;

        site.Length.ShouldBe(8);
        site.Address.ShouldBe(new GameAddress(0x0058_0E50));
        (site.Address.Value + 8).ShouldBe(site.Resume.Value);
    }

    [Fact]
    public void RefusesAPrologueThatIsNotTheOneItWasWrittenAgainst() =>
        HookSite.Classify([0x55, 0x8B, 0xEC, 0x51], PacketSpyCave.Site.Stock, null)
            .ShouldBe(SiteState.Foreign);
}
