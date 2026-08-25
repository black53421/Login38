using Login38.Aux.Notifications;
using Login38.Aux.Toggles;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers the detour that catches the two packets the client does not draw.
/// </summary>
/// <remarks>
/// Pinned byte for byte against an independent transcription. This sits on the branch every
/// unhandled opcode takes, inside the client's packet dispatcher, on whatever thread is
/// receiving.
/// </remarks>
public sealed class NotificationHookCaveTests
{
    /// <summary>Somewhere for the cave to be.</summary>
    private static readonly GameAddress Cave = new(0x0300_0000);

    [Fact]
    public void SavesEverythingBeforeItLooksAtAnything()
    {
        byte[] expected =
        [
            0x60,                                             // pushad
            0x9C,                                             // pushfd
            0xFC,                                             // cld
            0x81, 0xBD, 0x0C, 0x9F, 0xFF, 0xFF, 0xBE, 0x00, 0x00, 0x00,   // cmp [ebp-0x60F4], 190
            0x0F, 0x84,                                       // je keep
        ];

        NotificationHookCave.Build(Cave)[..15].ShouldBe(expected);
    }

    [Fact]
    public void LooksForBothOfThePacketsItShows()
    {
        var code = NotificationHookCave.Build(Cave);

        code[19..29].ShouldBe([0x81, 0xBD, 0x0C, 0x9F, 0xFF, 0xFF, 0xC0, 0x00, 0x00, 0x00]);
        code[29..31].ShouldBe([0x0F, 0x85]);                  // jne done
    }

    [Fact]
    public void ClaimsItsSlotWithALockedExchange()
    {
        byte[] expected =
        [
            0xB8, 0x01, 0x00, 0x00, 0x00,         // mov eax, 1
            0xF0, 0x0F, 0xC1, 0x05, 0x00, 0x01, 0x00, 0x03,   // lock xadd [tail], eax
            0x89, 0xC3,                           // mov ebx, eax     keep the sequence
            0x83, 0xE0, 0x0F,                     // and eax, 0x0F    round the ring
            0x6B, 0xC0, 0x50,                     // imul eax, eax, 80
            0x05, 0x00, 0x02, 0x00, 0x03,         // add eax, ring
        ];

        NotificationHookCave.Build(Cave)[35..61].ShouldBe(expected);
    }

    [Fact]
    public void KeepsTheOpcodeAndThenThePacket()
    {
        byte[] expected =
        [
            0x8B, 0x8D, 0x0C, 0x9F, 0xFF, 0xFF,   // mov ecx, [ebp-0x60F4]
            0x88, 0x08,                           // mov [eax], cl
            0x8B, 0x75, 0x08,                     // mov esi, [ebp+8]
            0x85, 0xF6,                           // test esi, esi
            0x74, 0x0D,                           // jz done
            0x8D, 0x78, 0x04,                     // lea edi, [eax+4]
            0xB9, 0x48, 0x00, 0x00, 0x00,         // mov ecx, 72
            0xF3, 0xA4,                           // rep movsb
            0x89, 0x58, 0x4C,                     // mov [eax+76], ebx    the sequence, last
        ];

        NotificationHookCave.Build(Cave)[61..89].ShouldBe(expected);
    }

    // A rep that runs backwards writes off the front of the ring. Nothing says which way the
    // direction flag is pointing inside the client's dispatcher, and the reference does not
    // clear it.
    [Fact]
    public void ClearsTheDirectionFlagFirst() =>
        NotificationHookCave.Build(Cave)[2].ShouldBe((byte)0xFC);

    // The reference copies from the packet pointer with no check at all.
    [Fact]
    public void DoesNotFollowAPacketPointerOfNothing()
    {
        var code = NotificationHookCave.Build(Cave);

        code[72..74].ShouldBe([0x85, 0xF6]);                  // test esi, esi
        (76 + code[75]).ShouldBe(89);                         // straight to the exit
    }

    [Fact]
    public void PutsEverythingBackAndCarriesOnToWhereTheBranchWasGoing()
    {
        var code = NotificationHookCave.Build(Cave);

        code[89..91].ShouldBe([0x9D, 0x61]);                  // popfd; popad
        code[91].ShouldBe((byte)0xE9);
        (Cave.Value + 96 + (uint)BitConverter.ToInt32(code, 92)).ShouldBe(0x0054_1596u);
        code.Length.ShouldBe(96);
    }

    // Both ways of not keeping a packet reach the same exit, with the registers restored.
    [Fact]
    public void LeavesTheSameWayHoweverItLeaves()
    {
        var code = NotificationHookCave.Build(Cave);

        (19 + BitConverter.ToInt32(code, 15)).ShouldBe(35);   // je keep
        (35 + BitConverter.ToInt32(code, 31)).ShouldBe(89);   // jne done
    }

    [Fact]
    public void FitsInWhatIsReservedForIt() =>
        NotificationHookCave.Build(Cave).Length.ShouldBeLessThan(NotificationHookCave.CodeSize);

    // ---- the branch it takes over ----------------------------------------------------

    // Only the displacement moves. The condition stays exactly as the client wrote it, which
    // is what keeps every opcode the client does handle out of this entirely.
    [Fact]
    public void KeepsTheClientsOwnConditionAndOnlyMovesWhereItGoes()
    {
        var patch = NotificationHookCave.Redirect(Cave);

        patch.Length.ShouldBe(NotificationHookCave.Site.Length);
        patch[..2].ShouldBe([0x0F, 0x87]);                    // still ja
        (NotificationHookCave.Site.Address.Value + 6 + (uint)BitConverter.ToInt32(patch, 2))
            .ShouldBe(Cave.Value);
    }

    [Fact]
    public void ComesFromWhereTheClientSendsUnhandledOpcodes()
    {
        var site = NotificationHookCave.Site;

        site.Address.ShouldBe(new GameAddress(0x0053_938E));
        site.Length.ShouldBe(6);
        (site.Address.Value + 6).ShouldBe(site.Resume.Value);
        (site.Address.Value + 6 + (uint)BitConverter.ToInt32(site.Stock, 2)).ShouldBe(0x0054_1596u);
    }

    [Fact]
    public void RefusesABranchThatIsNotTheOneItWasWrittenAgainst() =>
        HookSite.Classify([0x0F, 0x87, 0, 0, 0, 0], NotificationHookCave.Site.Stock, null)
            .ShouldBe(SiteState.Foreign);

    // ---- the ring --------------------------------------------------------------------

    [Fact]
    public void LaysTheRingOutWithoutOverlappingTheCodeOrTheCounter()
    {
        NotificationHookCave.CodeOffset.ShouldBeLessThan(NotificationHookCave.TailOffset);
        (NotificationHookCave.CodeOffset + (uint)NotificationHookCave.CodeSize)
            .ShouldBeLessThanOrEqualTo(NotificationHookCave.TailOffset);
        (NotificationHookCave.TailOffset + 4u).ShouldBeLessThanOrEqualTo(NotificationHookCave.RingOffset);
        (NotificationHookCave.RingOffset + (uint)(NotificationHookCave.RingLength * NotificationHookCave.SlotSize))
            .ShouldBeLessThanOrEqualTo((uint)NotificationHookCave.CaveSize);
    }

    // The longest thing this shows is a pickup: two bytes of sprite, sixty-four of name and
    // a terminator.
    [Fact]
    public void HasRoomForTheLongestPacketItShows() =>
        NotificationHookCave.PayloadBytes.ShouldBeGreaterThanOrEqualTo(2 + PacketBox.LongestName + 1);

    // A power of two, because the slot is chosen with a mask rather than a division.
    [Fact]
    public void HasARingLengthTheShellcodeCanMaskWith() =>
        (NotificationHookCave.RingLength & (NotificationHookCave.RingLength - 1)).ShouldBe(0);
}
