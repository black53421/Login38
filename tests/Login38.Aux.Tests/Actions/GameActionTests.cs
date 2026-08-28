using Login38.Aux.Actions;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Actions;

/// <summary>
/// Covers the code the helper writes into the game to do one thing.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these runs on a thread inside a live client with a character standing in
/// it. There is no way to try one and see: it either does what the player would have done
/// or it disconnects the account, and a wrong stack adjustment does the latter silently
/// several actions later. So the bytes are pinned.
/// </para>
/// <para>
/// The expectations are transcribed from the reference's own builders. Two of them —
/// <c>cds</c> and <c>ccs</c>, the ones carrying a string — come out identical, which is
/// what says the shared emitter here resolves its self-relative addresses the way the
/// reference's hand-written ones did. The rest differ in two deliberate ways, each with
/// its own test below: the four-byte form of the self-relative load, and pushing every
/// argument as a full dword.
/// </para>
/// </remarks>
public sealed class GameActionTests
{
    private const uint Packed = 42;

    // Two arbitrary values; what matters is that they appear where the format says.
    private const uint ItemId = 0x1111;
    private const uint Count = 500;

    [Fact]
    public void CallsTheClientsUseItemWithTheItemsEntry() =>
        Calls.OneArgument(new GameAddress(0x004B3EE0), 0x12345678).ShouldBe(
        [
            0x60, 0x68, 0x78, 0x56, 0x34, 0x12, 0xB8, 0xE0, 0x3E, 0x4B, 0x00, 0xFF,
            0xD0, 0x83, 0xC4, 0x04, 0x61, 0xC3,
        ]);

    // The reference counted this one out in its own comment, so it is worth keeping the
    // count: pushad, push, mov, call, add, popad, ret.
    [Fact]
    public void FitsACallInEighteenBytes() =>
        Calls.OneArgument(new GameAddress(0x004B3EE0), 0).Length.ShouldBe(18);

    [Fact]
    public void SendsAnOrdinaryItemUse() =>
        PacketCall.Send(
            "cdc"u8,
            PacketArgument.Number(0xA4),
            PacketArgument.Number(ItemId),
            PacketArgument.Number(0)).ShouldBe(
        [
            0x60, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x5E, 0x68, 0x00, 0x00, 0x00, 0x00,
            0x68, 0x11, 0x11, 0x00, 0x00, 0x68, 0xA4, 0x00, 0x00, 0x00, 0x8D, 0x86,
            0x23, 0x00, 0x00, 0x00, 0x50, 0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0,
            0x83, 0xC4, 0x10, 0x61, 0xC3, 0x63, 0x64, 0x63, 0x00,
        ]);

    // The order was got wrong once and the server read the weapon as the thing being used
    // up, which unequipped it. Source first, target second.
    [Fact]
    public void SendsOneItemUsedOnAnotherSourceFirst() =>
        PacketCall.Send(
            "cdd"u8,
            PacketArgument.Number(0xA4),
            PacketArgument.Number(0x2222),
            PacketArgument.Number(0x3333)).ShouldBe(
        [
            0x60, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x5E, 0x68, 0x33, 0x33, 0x00, 0x00,
            0x68, 0x22, 0x22, 0x00, 0x00, 0x68, 0xA4, 0x00, 0x00, 0x00, 0x8D, 0x86,
            0x23, 0x00, 0x00, 0x00, 0x50, 0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0,
            0x83, 0xC4, 0x10, 0x61, 0xC3, 0x63, 0x64, 0x64, 0x00,
        ]);

    [Fact]
    public void SendsAnItemBeingThrownAway() =>
        PacketCall.Send(
            "cdd"u8,
            PacketArgument.Number(0x8A),
            PacketArgument.Number(0x4444),
            PacketArgument.Number(Count)).ShouldBe(
        [
            0x60, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x5E, 0x68, 0xF4, 0x01, 0x00, 0x00,
            0x68, 0x44, 0x44, 0x00, 0x00, 0x68, 0x8A, 0x00, 0x00, 0x00, 0x8D, 0x86,
            0x23, 0x00, 0x00, 0x00, 0x50, 0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0,
            0x83, 0xC4, 0x10, 0x61, 0xC3, 0x63, 0x64, 0x64, 0x00,
        ]);

    // Five arguments, so twenty bytes to take back off — a variadic C function leaves that
    // to whoever called it.
    [Fact]
    public void SendsATeleportScroll() =>
        PacketCall.Send(
            "cdhhh"u8,
            PacketArgument.Number(0xA4),
            PacketArgument.Number(0x5555),
            PacketArgument.Number(4),
            PacketArgument.Number(0),
            PacketArgument.Number(0)).ShouldBe(
        [
            0x60, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x5E, 0x68, 0x00, 0x00, 0x00, 0x00,
            0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x04, 0x00, 0x00, 0x00, 0x68, 0x55,
            0x55, 0x00, 0x00, 0x68, 0xA4, 0x00, 0x00, 0x00, 0x8D, 0x86, 0x2D, 0x00,
            0x00, 0x00, 0x50, 0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0, 0x83, 0xC4,
            0x18, 0x61, 0xC3, 0x63, 0x64, 0x68, 0x68, 0x68, 0x00,
        ]);

    [Fact]
    public void SendsAPacketWithNothingInItButItsOpcode() =>
        PacketCall.Send("c"u8, PacketArgument.Number(0x34)).ShouldBe(
        [
            0x60, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x5E, 0x68, 0x34, 0x00, 0x00, 0x00,
            0x8D, 0x86, 0x19, 0x00, 0x00, 0x00, 0x50, 0xB8, 0x50, 0x0E, 0x58, 0x00,
            0xFF, 0xD0, 0x83, 0xC4, 0x08, 0x61, 0xC3, 0x63, 0x00,
        ]);

    // Identical to the reference's own bytes, including both self-relative displacements.
    [Fact]
    public void SendsAScrollWithSomethingToTurnInto() =>
        PacketCall.Send(
            "cds"u8,
            PacketArgument.Number(0xA4),
            PacketArgument.Number(0x6666),
            PacketArgument.Text8("wolf"u8)).ShouldBe(
        [
            0x60, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x5E, 0x8D, 0x86, 0x29, 0x00, 0x00,
            0x00, 0x50, 0x68, 0x66, 0x66, 0x00, 0x00, 0x68, 0xA4, 0x00, 0x00, 0x00,
            0x8D, 0x86, 0x25, 0x00, 0x00, 0x00, 0x50, 0xB8, 0x50, 0x0E, 0x58, 0x00,
            0xFF, 0xD0, 0x83, 0xC4, 0x10, 0x61, 0xC3, 0x63, 0x64, 0x73, 0x00, 0x77,
            0x6F, 0x6C, 0x66, 0x00,
        ]);

    // Likewise identical.
    [Fact]
    public void SendsSomethingToSay() =>
        PacketCall.Send(
            "ccs"u8,
            PacketArgument.Number(0x88),
            PacketArgument.Number((byte)ChatChannel.Shout),
            PacketArgument.Text8("hi"u8)).ShouldBe(
        [
            0x60, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x5E, 0x8D, 0x86, 0x29, 0x00, 0x00,
            0x00, 0x50, 0x68, 0x02, 0x00, 0x00, 0x00, 0x68, 0x88, 0x00, 0x00, 0x00,
            0x8D, 0x86, 0x25, 0x00, 0x00, 0x00, 0x50, 0xB8, 0x50, 0x0E, 0x58, 0x00,
            0xFF, 0xD0, 0x83, 0xC4, 0x10, 0x61, 0xC3, 0x63, 0x63, 0x73, 0x00, 0x68,
            0x69, 0x00,
        ]);

    // The format string first, then the arguments' own strings in order, so the bytes read
    // the way the call does.
    [Fact]
    public void CarriesTheFormatBeforeTheStringsItNames() =>
        PacketCall.Send(
            "ccs"u8, PacketArgument.Number(0x88), PacketArgument.Number(0),
            PacketArgument.Text8("hi"u8))[^7..]
        .ShouldBe("ccs\0hi\0"u8.ToArray());

    // A message the player typed can be longer than a signed byte. The reference reached
    // its strings with a one-byte displacement guarded by an assertion release drops, so
    // a long enough message would have loaded an address 128 bytes short of the text.
    [Fact]
    public void ReachesAStringThatIsFurtherAwayThanAByteCanCount()
    {
        var message = new byte[400];
        Array.Fill(message, (byte)'a');

        var code = PacketCall.Send(
            "ccs"u8, PacketArgument.Number(0x88), PacketArgument.Number(0),
            PacketArgument.Text8(message));

        // lea eax, [esi + disp32] for the message, which sits after the format string.
        code[7..9].ShouldBe(new byte[] { 0x8D, 0x86 });
        BitConverter.ToInt32(code, 9).ShouldBe(code.Length - message.Length - 1 - 6);
    }

    [Fact]
    public void RefusesAStringWhereThereIsNowhereToPutIt() =>
        Should.Throw<ArgumentException>(() => PacketCall.Send(
            new GameAddress(0x008EF028), PacketArgument.Text8("no"u8)));

    // The client's own "cccd" is used rather than an embedded copy, so this one needs no
    // self-relative machinery at all.
    [Fact]
    public void CastsOnThePlayerByReadingTheirOwnId() =>
        SkillCast.Build(Packed, SkillTarget.Self).ShouldBe(
        [
            0x60, 0xFF, 0x35, 0xB4, 0xF4, 0xAB, 0x00, 0x68, 0x02, 0x00, 0x00, 0x00,
            0x68, 0x05, 0x00, 0x00, 0x00, 0x68, 0x06, 0x00, 0x00, 0x00, 0x68, 0x28,
            0xF0, 0x8E, 0x00, 0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0, 0x83, 0xC4,
            0x14, 0x61, 0xC3,
        ]);

    [Fact]
    public void CastsOnAnItemByNamingItInThePacket() =>
        SkillCast.Build(Packed, SkillTarget.Item(0x7777)).ShouldBe(
        [
            0x60, 0x68, 0x77, 0x77, 0x00, 0x00, 0x68, 0x02, 0x00, 0x00, 0x00, 0x68,
            0x05, 0x00, 0x00, 0x00, 0x68, 0x06, 0x00, 0x00, 0x00, 0x68, 0x28, 0xF0,
            0x8E, 0x00, 0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0, 0x83, 0xC4, 0x14,
            0x61, 0xC3,
        ]);

    // A skill id is split across two packet fields, low three bits and the rest.
    [Fact]
    public void SplitsTheSkillIdTheWayThePacketDoes()
    {
        SkillCast.High(Packed).ShouldBe(5u);
        SkillCast.Low(Packed).ShouldBe(2u);
        ((SkillCast.High(Packed) << 3) | SkillCast.Low(Packed)).ShouldBe(Packed);
    }

    // The delay the client would have worked out, then the packet the client would have
    // sent, then the client's own routine for stamping the cooldown. Its entry point is not
    // called at all, because the third thing it does is arm the cursor.
    [Fact]
    public void CastsAtSomethingInTheWorldWithoutArmingTheCursor() =>
        SkillCast.Build(Packed, SkillTarget.Entity(0x8888)).ShouldBe(
        [
            0x60,
            0x6A, 0x13,                                 // push 0x13
            0x8B, 0x0D, 0xB8, 0xD2, 0xC2, 0x00,         // mov ecx, [g_local_player]
            0xB8, 0x30, 0xE5, 0x5A, 0x00, 0xFF, 0xD0,   // call FUN_005AE530
            0x0F, 0xBF, 0x15, 0x84, 0xD6, 0x96, 0x00,   // movsx edx, word [delays + 42*2]
            0x03, 0xC2,                                 // add eax, edx
            0xA3, 0x10, 0x13, 0xC3, 0x00,               // mov [castDelay], eax
            0x68, 0x88, 0x88, 0x00, 0x00,               // push objectId
            0x68, 0x02, 0x00, 0x00, 0x00,               // push low
            0x68, 0x05, 0x00, 0x00, 0x00,               // push high
            0x68, 0x06, 0x00, 0x00, 0x00,               // push opcode
            0x68, 0x28, 0xF0, 0x8E, 0x00,               // push "cccd"
            0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0,   // call FUN_00580E50
            0x83, 0xC4, 0x14,
            0xB8, 0x10, 0xBB, 0x73, 0x00, 0xFF, 0xD0,   // call FUN_0073BB10
            0x61, 0xC3,
        ]);

    // The skill's own icon, when the book record can be trusted. Before the cast, which is
    // the order the client itself does it in.
    [Fact]
    public void CoolsTheSkillsOwnIconWhenTheRecordIsKnown()
    {
        var code = SkillCast.Build(Packed, SkillTarget.Entity(0x8888), new GameAddress(0x0BADF00D));

        code.AsSpan().IndexOf<byte>(
        [
            0xC6, 0x05, 0xC9, 0xF0, 0xAD, 0x0B, 0x00,   // mov byte [record + 0xBC], 0
            0xB9, 0x0D, 0xF0, 0xAD, 0x0B,               // mov ecx, record
            0xB8, 0x50, 0xB9, 0x73, 0x00, 0xFF, 0xD0,   // call FUN_0073B950
        ]).ShouldBeGreaterThan(-1);

        // And the cast still goes out, which is the part that matters.
        code.AsSpan().IndexOf<byte>([0xB8, 0x50, 0x0E, 0x58, 0x00, 0xFF, 0xD0]).ShouldBeGreaterThan(-1);
    }

    // A record read before a level, a relog or a character change points at memory the
    // client has given back. Writing into it would be a fault rather than a wrong icon.
    [Fact]
    public void LeavesTheIconAloneWithoutARecord() =>
        SkillCast.Build(Packed, SkillTarget.Entity(0x8888))
            .AsSpan()
            .IndexOf<byte>([0xB8, 0x50, 0xB9, 0x73, 0x00, 0xFF, 0xD0])
            .ShouldBe(-1);

    // The whole reason it is assembled rather than dispatched: none of the words the client
    // reads to decide what the player's next click means are written.
    [Fact]
    public void LeavesTheCursorAndTheTargetGlobalsAloneWhenItCasts()
    {
        var code = SkillCast.Build(Packed, SkillTarget.Entity(0x8888));

        foreach (var global in new uint[] { 0x00C31304, 0x00C3131C, 0x00C31308, 0x0097C910 })
        {
            code.AsSpan().IndexOf(BitConverter.GetBytes(global)).ShouldBe(-1);
        }
    }

    [Fact]
    public void LeavesTheClientsOwnTargetAloneWhenNoneIsNamed() =>
        SkillCast.Build(Packed, SkillTarget.Whatever).ShouldBe(
        [
            0x60, 0xA1, 0x10, 0xC9, 0x97, 0x00, 0x50, 0x6A, 0x01, 0x68, 0x2A, 0x00,
            0x00, 0x00, 0x8B, 0x0D, 0x24, 0x13, 0xC3, 0x00, 0xB8, 0xE0, 0xEC, 0x73,
            0x00, 0xFF, 0xD0, 0x58, 0xA3, 0x10, 0xC9, 0x97, 0x00, 0x61, 0xC3,
        ]);

    // The client writes its target global as the mouse moves and reads it on the player's
    // next click. Leaving the helper's value there aims the player's next cast at whatever
    // the helper was aiming at.
    [Fact]
    public void PutsTheClientsTargetBackAfterCasting()
    {
        var code = SkillCast.Build(Packed, SkillTarget.Whatever);

        // mov eax, [target] first, push it, and pop it back into [target] last.
        code[1].ShouldBe((byte)0xA1);
        code[6].ShouldBe((byte)0x50);
        code[^8].ShouldBe((byte)0x58);
        code[^7].ShouldBe((byte)0xA3);
        code[2..6].ShouldBe(code[^6..^2]);
    }

    // Every one of these ends up on a thread the client did not make, so it has to leave
    // the registers exactly as it found them. Carrying no strings, these end at the ret.
    [Theory]
    [MemberData(nameof(ActionsCarryingNothing))]
    public void SavesAndRestoresEveryRegister(byte[] code)
    {
        code[0].ShouldBe((byte)0x60);
        code[^2..].ShouldBe(new byte[] { 0x61, 0xC3 });
    }

    // And so do the ones that carry a string, with the string after the ret.
    [Fact]
    public void EndsBeforeTheStringsItCarries()
    {
        var code = PacketCall.Send(
            "ccs"u8, PacketArgument.Number(0x88), PacketArgument.Number(0),
            PacketArgument.Text8("hi"u8));

        code[0].ShouldBe((byte)0x60);
        code[^9..^7].ShouldBe(new byte[] { 0x61, 0xC3 });
    }

    public static TheoryData<byte[]> ActionsCarryingNothing() =>
    [
        Calls.OneArgument(new GameAddress(0x004B3EE0), ItemId),
        SkillCast.Build(Packed, SkillTarget.Self),
        SkillCast.Build(Packed, SkillTarget.Item(ItemId)),
        SkillCast.Build(Packed, SkillTarget.Entity(ItemId)),
        SkillCast.Build(Packed, SkillTarget.Whatever),
    ];
}
