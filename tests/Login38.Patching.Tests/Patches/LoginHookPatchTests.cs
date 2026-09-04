using System.Buffers.Binary;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Checks the generated machine code without a game to inject it into.
/// </summary>
/// <remarks>
/// These are the assertions worth having: a wrong argument order sends the password as
/// the account name, and a wrong jump displacement transfers control into arbitrary
/// memory. Both are silent until they crash a live client.
/// </remarks>
public sealed class LoginHookPatchTests
{
    private static readonly GameAddress Cave = new(0x1000_0000);

    private const uint AccountBufferAddress = 0x1000_0000;
    private const uint PasswordBufferAddress = 0x1000_0080;
    private const uint PasswordPositionAddress = 0x1000_0100;
    private const uint FormatStringAddress = 0x1000_0110;
    private const uint SendCodeAddress = 0x1000_0280;

    private const uint SendPacketData = 0x0058_0E50;
    private const uint LoginOpcode = 0xD2;
    private const uint LoginAction = 0x06;

    // The order these are pushed decides which string the server reads as the account.
    // Getting it backwards sends the password as the login name, in plain text.
    [Fact]
    public void SendPacketPushesTheL1jTwLoginArguments()
    {
        var arguments = ReadCdeclArguments(LoginHookPatch.BuildSendPacket(Cave));

        arguments[0].ShouldBe(FormatStringAddress);
        arguments[1].ShouldBe(LoginOpcode);
        arguments[2].ShouldBe(LoginAction);
        arguments[3].ShouldBe(AccountBufferAddress);
        arguments[4].ShouldBe(PasswordBufferAddress);
    }

    [Fact]
    public void SendPacketPassesFiveArguments() =>
        ReadCdeclArguments(LoginHookPatch.BuildSendPacket(Cave)).Length.ShouldBe(5);

    [Fact]
    public void SendPacketCallsTheClientsSendRoutine()
    {
        var code = LoginHookPatch.BuildSendPacket(Cave);
        var callIndex = Array.IndexOf(code, (byte)0xE8);

        callIndex.ShouldBeGreaterThan(0);
        ResolveRel32(SendCodeAddress, code, callIndex).ShouldBe(SendPacketData);
    }

    // Without this reset a second login attempt appends to the first password.
    [Fact]
    public void SendPacketResetsThePasswordWritePosition()
    {
        var code = LoginHookPatch.BuildSendPacket(Cave);

        // mov dword ptr [position], 0
        var expected = new byte[] { 0xC7, 0x05 }
            .Concat(BitConverter.GetBytes(PasswordPositionAddress))
            .Concat(BitConverter.GetBytes(0u))
            .ToArray();

        IndexOfSequence(code, expected).ShouldBeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(0x0077_2E77u)] // send hook returns past the client's own packet build
    public void SendPacketReturnsToTheClient(uint expected) =>
        FinalJumpTarget(SendCodeAddress, LoginHookPatch.BuildSendPacket(Cave)).ShouldBe(expected);

    [Fact]
    public void AccountCaptureReturnsToTheInstructionAfterTheHook() =>
        FinalJumpTarget(0x1000_0120, LoginHookPatch.BuildAccountCapture(Cave)).ShouldBe(0x0077_3183u);

    [Fact]
    public void PasswordCaptureReturnsToTheInstructionAfterTheHook() =>
        FinalJumpTarget(0x1000_0180, LoginHookPatch.BuildPasswordCapture(Cave)).ShouldBe(0x004A_A395u);

    // The hook replaces six bytes, so it has to replay what it displaced before doing
    // anything else, or eax never points at the typed account.
    [Fact]
    public void AccountCaptureReplaysTheDisplacedInstruction() =>
        LoginHookPatch.BuildAccountCapture(Cave)[..6]
            .ShouldBe([0x8D, 0x85, 0x68, 0xFF, 0xFF, 0xFF]);

    [Fact]
    public void PasswordCaptureReplaysTheDisplacedInstructions() =>
        LoginHookPatch.BuildPasswordCapture(Cave)[..7]
            .ShouldBe([0x8B, 0x55, 0xF4, 0x8B, 0x4C, 0x8A, 0x3C]);

    [Fact]
    public void AccountCaptureCopiesIntoTheAccountBuffer()
    {
        var code = LoginHookPatch.BuildAccountCapture(Cave);

        // mov edi, accountBuffer
        IndexOfSequence(code, [0xBF, .. BitConverter.GetBytes(AccountBufferAddress)])
            .ShouldBeGreaterThanOrEqualTo(0);

        // rep movsb
        IndexOfSequence(code, [0xF3, 0xA4]).ShouldBeGreaterThanOrEqualTo(0);
    }

    // The clear must be reachable only when the write position is zero; a jne that
    // skipped the wrong distance would either never clear or clear every keystroke.
    [Fact]
    public void PasswordCaptureSkipsTheBufferClearWhenAlreadyTyping()
    {
        var code = LoginHookPatch.BuildPasswordCapture(Cave);
        var jneIndex = IndexOfSequence(code, [0x85, 0xC9, 0x75]) + 2;

        jneIndex.ShouldBeGreaterThan(1);
        var displacement = code[jneIndex + 1];

        // Landing spot must be the "mov edx, passwordBuffer" that follows the clear.
        var target = jneIndex + 2 + displacement;
        code[target].ShouldBe((byte)0xBA);
        BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(target + 1)).ShouldBe(PasswordBufferAddress);
    }

    [Fact]
    public void EveryBlockFitsItsSlotInTheCave()
    {
        LoginHookPatch.BuildAccountCapture(Cave).Length.ShouldBeLessThanOrEqualTo(0x60);
        LoginHookPatch.BuildPasswordCapture(Cave).Length.ShouldBeLessThanOrEqualTo(0x100);
        LoginHookPatch.BuildSendPacket(Cave).Length.ShouldBeLessThanOrEqualTo(0x180);
    }

    /// <summary>Reads the <c>push imm32</c> run before the call, in call order.</summary>
    private static uint[] ReadCdeclArguments(byte[] code)
    {
        var pushed = new List<uint>();
        var offset = 0;

        while (offset < code.Length && code[offset] == 0x68)
        {
            pushed.Add(BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + 1)));
            offset += 5;
        }

        code[offset].ShouldBe((byte)0xE8, "the pushes should be followed immediately by the call");

        // cdecl pushes right to left, so the call's first argument is the last push.
        pushed.Reverse();
        return [.. pushed];
    }

    private static uint FinalJumpTarget(uint baseAddress, byte[] code)
    {
        code[^5].ShouldBe((byte)0xE9, "the block should end with a near jump");
        return ResolveRel32(baseAddress, code, code.Length - 5);
    }

    private static uint ResolveRel32(uint baseAddress, byte[] code, int opcodeIndex)
    {
        var displacement = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(opcodeIndex + 1));
        return unchecked((uint)(baseAddress + opcodeIndex + 5 + displacement));
    }

    private static int IndexOfSequence(byte[] haystack, byte[] needle)
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
}
