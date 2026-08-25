using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the copied item status handler and the packet router in front of it.
/// </summary>
/// <remarks>
/// <para>
/// The handler is not assembled — it is a copy of the client's own, altered in place — so
/// the input is a stand-in with the structure the transform expects and recognisable
/// relative calls. That is enough to pin every edit, because the transform reads nothing
/// else about its input.
/// </para>
/// <para>
/// The expected bytes come from transcribing the reference's
/// <c>transform_long_status_copy</c> and <c>build_dispatcher_hook_cave</c> and running them
/// over the same stand-in.
/// </para>
/// </remarks>
public sealed class LongItemStatusPatchTests
{
    private static readonly GameAddress HandlerCave = new(0x0A00_0000);

    private static readonly GameAddress RouterCave = new(0x0A00_0000);

    private static readonly GameAddress HandlerTarget = new(0x0B00_0000);

    private static readonly GameAddress Format = new(0x0C00_0000);

    private static readonly GameAddress Buffer = new(0x0D00_0000);

    /// <summary>Where the original lives, which is what the relocation is measured from.</summary>
    private static readonly GameAddress StatusHandler = new(0x0052_8A00);

    private static readonly GameAddress StatusDownstream = new(0x0052_8B18);

    private static readonly GameAddress DispatcherContinuation = new(0x0054_4A26);

    private const int CopyLength = 0x118;

    private const int FormatImmediate = 0x2F;

    private const int BufferLea = 0xB8;

    private static readonly int[] LengthReads = [0xAE, 0xC2, 0xDA, 0xF7];

    private static readonly int[] Calls = [0x3A, 0x5F, 0x72, 0x88, 0x9E, 0xD1, 0xEE, 0x113];

    private const string ExpectedHandler =
        "55 18 1F 26 2D 34 3B 42 49 50 57 5E 65 6C 73 7A 81 88 8F 96 9D A4 AB B2 B9 C0 C7 CE D5 DC E3 EA " +
        "F1 F8 FF 06 0D 14 1B 22 29 30 37 3E 45 4C 68 00 00 00 0C 76 7D 84 8B 92 99 A0 E8 3A 9A 52 F6 CA " +
        "D1 D8 DF E6 ED F4 FB 02 09 10 17 1E 25 2C 33 3A 41 48 4F 56 5D 64 6B 72 79 80 87 8E 95 9C A3 E8 " +
        "5F 9A 52 F6 CD D4 DB E2 E9 F0 F7 FE 05 0C 13 1A 21 28 E8 72 9A 52 F6 52 59 60 67 6E 75 7C 83 8A " +
        "91 98 9F A6 AD B4 BB C2 E8 88 9A 52 F6 EC F3 FA 01 08 0F 16 1D 24 2B 32 39 40 47 4E 55 5C E8 9E " +
        "9A 52 F6 86 8D 94 9B A2 A9 B0 B7 BE C5 CC B7 DA E1 E8 EF F6 FD 04 0B 12 B9 00 00 00 0D 90 43 4A " +
        "51 58 B7 66 6D 74 7B 82 89 90 97 9E A5 AC B3 BA C1 E8 D1 9A 52 F6 EB F2 F9 00 B7 0E 15 1C 23 2A " +
        "31 38 3F 46 4D 54 5B 62 69 70 77 7E 85 8C E8 EE 9A 52 F6 B6 BD C4 CB B7 D9 E0 E7 EE F5 FC 03 0A " +
        "11 18 1F 26 2D 34 3B 42 49 50 57 5E 65 6C 73 7A 81 88 8F E8 13 9B 52 F6 E9 FB 89 52 F6";

    private const string ExpectedRouter =
        "8B 44 24 04 8A 08 80 F9 F1 74 05 80 F9 F2 75 0A 50 E8 EA FF FF 00 83 C4 04 C3 55 8B EC 83 EC 1C " +
        "E9 01 4A 54 F6";

    [Fact]
    public void HandlerMatchesTheReferenceByteForByte() =>
        LongItemStatusPatch.BuildHandler(Source(), HandlerCave, Format, Buffer)
            .ShouldBe(Parse(ExpectedHandler));

    [Fact]
    public void RouterMatchesTheReferenceByteForByte() =>
        LongItemStatusPatch.BuildRouter(RouterCave, HandlerTarget).ShouldBe(Parse(ExpectedRouter));

    // The one change that widens the wire format. Everything else is a consequence of it.
    [Fact]
    public void PointsTheParserAtTheTwoByteDescriptor() =>
        BitConverter.ToUInt32(Handler(), FormatImmediate).ShouldBe(Format.Value);

    // Six bytes of lea become five of mov, so the sixth has to be something — and it has to
    // be something, because shifting the tail up would move every relative jump in the copy.
    [Fact]
    public void ReplacesTheStackBufferWithoutMovingAnythingAfterIt()
    {
        var code = Handler();

        code[BufferLea].ShouldBe((byte)0xB9);                        // mov ecx, imm32
        BitConverter.ToUInt32(code, BufferLea + 1).ShouldBe(Buffer.Value);
        code[BufferLea + 5].ShouldBe((byte)0x90);                    // nop
        code.Length.ShouldBe(CopyLength + 5);
    }

    // Four reads of the length, all of which have to widen together. Leaving one as a byte
    // read truncates the length at that step only, which parses most of the packet correctly.
    [Fact]
    public void WidensEveryReadOfTheLength()
    {
        var code = Handler();

        foreach (var offset in LengthReads)
        {
            code[offset].ShouldBe((byte)0xB7, $"the length read at 0x{offset:X} is still a byte read");
        }
    }

    // Only the four. A B6 elsewhere in the copy is part of some other instruction, and
    // rewriting it changes what that instruction does.
    [Fact]
    public void WidensNothingElse()
    {
        var source = Source();
        var code = Handler();

        for (var i = 0; i < CopyLength; i++)
        {
            if (source[i] == 0xB6 && !LengthReads.Contains(i))
            {
                code[i].ShouldBe((byte)0xB6, $"the byte at 0x{i:X} was rewritten and is not a length read");
            }
        }
    }

    // The whole reason the calls are touched at all: a copy runs somewhere else, so every
    // displacement in it is wrong by exactly how far it moved.
    [Fact]
    public void KeepsEveryCallPointingWhereItDid()
    {
        var source = Source();
        var code = Handler();

        foreach (var offset in Calls)
        {
            code[offset].ShouldBe((byte)0xE8);

            var original = StatusHandler + (offset + 5 + BitConverter.ToInt32(source, offset + 1));
            var relocated = HandlerCave + (offset + 5 + BitConverter.ToInt32(code, offset + 1));

            relocated.ShouldBe(original, $"the call at 0x{offset:X} no longer reaches its target");
        }
    }

    // The tail is what makes this a copy of the parsing rather than a reimplementation of
    // the handler: from here on it is the client's own code, unmodified.
    [Fact]
    public void ReturnsIntoTheOriginalHandler()
    {
        var code = Handler();

        code[CopyLength].ShouldBe((byte)0xE9);
        (HandlerCave + (CopyLength + 5 + BitConverter.ToInt32(code, CopyLength + 1)))
            .ShouldBe(StatusDownstream);
    }

    [Fact]
    public void HandlerFitsTheCaveItIsWrittenInto() => Handler().Length.ShouldBeLessThanOrEqualTo(0x140);

    [Fact]
    public void RouterFitsTheCaveItIsWrittenInto() => Router().Length.ShouldBeLessThanOrEqualTo(0x40);

    // The router runs before the dispatcher has done anything, so the packet is still where
    // the caller put it.
    [Fact]
    public void ReadsTheOpcodeOutOfTheIncomingPacket() =>
        Router()[..6].ShouldBe([0x8B, 0x44, 0x24, 0x04, 0x8A, 0x08]);

    [Theory]
    [InlineData(0xF1)]
    [InlineData(0xF2)]
    public void ClaimsBothLongStatusOpcodes(byte opcode) =>
        BytePattern.Format(Router()).ShouldContain($"80 F9 {opcode:X2}");

    // Two comparisons, one body. The first branch has to land on the body and the second has
    // to skip all of it — hand-counted displacements that a changed body would silently break.
    [Fact]
    public void BothOpcodesReachTheSameHandlerCall()
    {
        var code = Router();

        var claimed = 9 + 2 + (sbyte)code[10];    // je, measured from after its displacement
        var pushPacket = 16;

        claimed.ShouldBe(pushPacket);
        code[pushPacket].ShouldBe((byte)0x50);    // push eax
        code[pushPacket + 1].ShouldBe((byte)0xE8);

        (RouterCave + (pushPacket + 6 + BitConverter.ToInt32(code, pushPacket + 2)))
            .ShouldBe(HandlerTarget);
    }

    // A claimed packet must not fall through into the replayed prologue as well.
    [Fact]
    public void ReturnsAfterHandlingRatherThanContinuing()
    {
        var code = Router();

        code[22..26].ShouldBe([0x83, 0xC4, 0x04, 0xC3]);   // add esp, 4; ret
    }

    // Every other packet has to come out of this indistinguishable from never having been
    // here: the prologue the jump overwrote, then the instruction after it.
    [Fact]
    public void LeavesEveryOtherPacketAlone()
    {
        var code = Router();

        var notOurs = 14 + 2 + (sbyte)code[15];
        notOurs.ShouldBe(26);

        code[26..32].ShouldBe([0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x1C]);
        code[32].ShouldBe((byte)0xE9);
        (RouterCave + (32 + 5 + BitConverter.ToInt32(code, 33))).ShouldBe(DispatcherContinuation);
    }

    [Fact]
    public void AcceptsTheHandlerItExpects() => LongItemStatusPatch.IsDecrypted(Source()).ShouldBeTrue();

    // Encrypted bytes are arbitrary, and arbitrary bytes must not be transformed into a cave
    // and jumped to. Every byte the transform rewrites is checked first.
    [Fact]
    public void RejectsAShortRead() =>
        LongItemStatusPatch.IsDecrypted(Source().AsSpan(0, CopyLength - 1)).ShouldBeFalse();

    [Theory]
    [InlineData(0x00)]     // the prologue
    [InlineData(0x2E)]     // the descriptor push
    [InlineData(0xB8)]     // the buffer lea
    [InlineData(0xAE)]     // a length read
    [InlineData(0xF7)]     // the last length read
    [InlineData(0x3A)]     // the first call
    [InlineData(0x113)]    // the last call
    public void RejectsAnythingThatIsNotTheHandler(int offset)
    {
        var damaged = Source();
        damaged[offset] ^= 0xFF;

        LongItemStatusPatch.IsDecrypted(damaged).ShouldBeFalse();
    }

    private static byte[] Handler() => LongItemStatusPatch.BuildHandler(Source(), HandlerCave, Format, Buffer);

    private static byte[] Router() => LongItemStatusPatch.BuildRouter(RouterCave, HandlerTarget);

    /// <summary>
    /// A stand-in for the live handler: filler everywhere, with the structure the transform
    /// depends on at the offsets it depends on.
    /// </summary>
    private static byte[] Source()
    {
        var raw = new byte[CopyLength];

        for (var i = 0; i < raw.Length; i++)
        {
            raw[i] = (byte)((i * 7) + 0x11);
        }

        raw[0] = 0x55;
        raw[0x2E] = 0x68;
        BitConverter.GetBytes(0x008D_4B8Cu).CopyTo(raw, FormatImmediate);       // "dsdc"
        new byte[] { 0x8D, 0x8D, 0xE0, 0xFB, 0xFF, 0xFF }.CopyTo(raw, BufferLea);  // lea ecx, [ebp-0x420]

        foreach (var offset in LengthReads)
        {
            raw[offset] = 0xB6;
        }

        foreach (var offset in Calls)
        {
            raw[offset] = 0xE8;
            BitConverter.GetBytes(0x1000 + offset).CopyTo(raw, offset + 1);
        }

        return raw;
    }

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
