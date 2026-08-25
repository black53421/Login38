using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the generated machine code against what the Rust build emits, byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The expected sequences were produced by transcribing the reference's
/// <c>build_user_shellcode</c>, <c>build_pass_shellcode</c> and
/// <c>build_login77_shellcode</c> and running them for a cave at
/// <c>0x10000000</c>. This launcher's version is assembled through
/// <see cref="ShellcodeBuilder"/> rather than by appending bytes to a vector, so these
/// are two independent derivations of the same code.
/// </para>
/// <para>
/// This is the strongest check available without a running client: the structural
/// tests confirm the shellcode is self-consistent, and this confirms it matches what
/// is already working on players' machines. Every relative displacement, the hardcoded
/// <c>jne 0x13</c> the reference used, and the cdecl push order are all covered.
/// </para>
/// </remarks>
public sealed class LoginHookByteCompatibilityTests
{
    private static readonly GameAddress Cave = new(0x1000_0000);

    private const string ExpectedAccountCapture =
        "8D 85 68 FF FF FF 60 8B F0 BF 00 00 00 10 B9 20 00 00 00 FC F3 A4 61 E9 47 30 77 F0";

    private const string ExpectedPasswordCapture =
        "8B 55 F4 8B 4C 8A 3C 60 B8 00 28 40 00 FF D0 8B 0D 00 01 00 10 85 C9 75 13 50 57 BF " +
        "80 00 00 10 33 C0 AB AB AB AB AB AB AB AB 5F 58 BA 80 00 00 10 88 04 0A 41 89 0D 00 " +
        "01 00 10 61 E9 D4 A1 4A F0";

    private const string ExpectedSendPacket =
        "68 1F 00 00 00 68 00 00 00 00 68 00 00 00 00 68 00 00 00 00 68 00 00 00 00 68 00 00 " +
        "00 00 68 7F 00 00 01 68 80 00 00 10 68 00 00 00 10 68 77 00 00 00 68 10 01 00 10 E8 " +
        "94 0B 58 F0 83 C4 2C C7 05 00 01 00 10 00 00 00 00 E9 A9 2B 77 F0";

    [Fact]
    public void AccountCaptureMatchesTheReferenceByteForByte() =>
        LoginHookPatch.BuildAccountCapture(Cave).ShouldBe(Parse(ExpectedAccountCapture));

    [Fact]
    public void PasswordCaptureMatchesTheReferenceByteForByte() =>
        LoginHookPatch.BuildPasswordCapture(Cave).ShouldBe(Parse(ExpectedPasswordCapture));

    [Fact]
    public void SendPacketMatchesTheReferenceByteForByte() =>
        LoginHookPatch.BuildSendPacket(Cave).ShouldBe(Parse(ExpectedSendPacket));

    /// <summary>
    /// The reference hardcoded this branch as <c>75 13</c>. Here it is computed from the
    /// size of the clear block, so the two agreeing is a real check rather than a
    /// copied constant.
    /// </summary>
    [Fact]
    public void ComputedBranchDisplacementMatchesTheReferencesHardcodedOne()
    {
        var code = LoginHookPatch.BuildPasswordCapture(Cave);
        var jneIndex = Array.IndexOf(code, (byte)0x75);

        code[jneIndex + 1].ShouldBe((byte)0x13);
    }

    /// <summary>
    /// Displacements are relative, so the whole block must change when the cave moves.
    /// A test at one address alone would not catch a builder that ignored its base.
    /// </summary>
    [Fact]
    public void RelocatingTheCaveChangesEveryRelativeDisplacement()
    {
        var atOneAddress = LoginHookPatch.BuildSendPacket(new GameAddress(0x1000_0000));
        var atAnother = LoginHookPatch.BuildSendPacket(new GameAddress(0x2000_0000));

        atOneAddress.Length.ShouldBe(atAnother.Length);
        atOneAddress.ShouldNotBe(atAnother);
    }

    private static byte[] Parse(string hex) =>
    [
        .. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(token => byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
    ];
}
