using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Pins the code this patch installs against what the Rust build emits, byte for byte.
/// </summary>
/// <remarks>
/// The expected sequences were produced by transcribing the reference's
/// <c>build_charselect_f1_setter</c>, <c>build_charselect_f2_setter</c>,
/// <c>build_charselect_ac_display_hook</c> and <c>build_charsynack1_output_hook</c> and
/// running them for a cave at <c>0x20000000</c>. Nothing else can check these: they run
/// inside a client this test cannot start, and a wrong <c>ret</c> size or a wrong stack
/// displacement corrupts the caller's frame rather than failing visibly.
/// </remarks>
public sealed class ArmourResistanceExpansionPatchTests
{
    private static readonly GameAddress Cave = new(0x2000_0000);

    private static readonly ArmourResistanceExpansionPatch.Storage Storage =
        new(Cave, Cave + 4, Cave + 8);

    private const string ExpectedSetter1 =
        "55 8B EC 51 89 4D FC 8B 45 FC 8B 4D 08 89 0D 90 1E C3 00 66 89 48 0A 8B 4D 0C 89 0D 8C " +
        "1E C3 00 66 89 48 0C 8B 4D 10 89 0D 04 00 00 20 66 8B 4D 10 66 89 48 0E 8B E5 5D C2 0C " +
        "00";

    private const string ExpectedSetter2 =
        "55 8B EC 51 89 4D FC 8B 45 FC 8A 4D 08 88 48 10 8A 4D 0C 88 48 11 8B 4D 10 89 0D 90 1E " +
        "C3 00 66 89 48 0A 8B 4D 14 89 0D 8C 1E C3 00 66 89 48 0C 8B 4D 18 89 0D 04 00 00 20 66 " +
        "8B 4D 18 66 89 48 0E 0F BE 4D 1C 89 48 14 0F BE 4D 20 89 48 18 0F BE 4D 24 89 48 1C 0F " +
        "BE 4D 28 89 48 20 0F BE 4D 2C 89 48 24 0F BE 4D 30 89 48 28 8B E5 5D C2 2C 00";

    private const string ExpectedArmourDisplayHook = "8B 0D 04 00 00 20 51 E9 70 B5 76 E0";

    private const string ExpectedCharacterAckOutputHook =
        "8D 45 FE 50 68 04 00 00 20 8D 55 F4 52 8D 45 F8 50 E9 4F 38 54 E0";

    [Fact]
    public void Setter1MatchesTheReferenceByteForByte() =>
        ArmourResistanceExpansionPatch.BuildSetter1(Storage).ShouldBe(Parse(ExpectedSetter1));

    [Fact]
    public void Setter2MatchesTheReferenceByteForByte() =>
        ArmourResistanceExpansionPatch.BuildSetter2(Storage).ShouldBe(Parse(ExpectedSetter2));

    [Fact]
    public void ArmourDisplayHookMatchesTheReferenceByteForByte() =>
        ArmourResistanceExpansionPatch.BuildArmourDisplayHook(Storage, Cave + 0xC0)
            .ShouldBe(Parse(ExpectedArmourDisplayHook));

    [Fact]
    public void CharacterAckOutputHookMatchesTheReferenceByteForByte() =>
        ArmourResistanceExpansionPatch.BuildCharacterAckOutputHook(Storage, Cave + 0x140)
            .ShouldBe(Parse(ExpectedCharacterAckOutputHook));

    // These replace functions the client calls with a fixed argument count. A ret that
    // pops the wrong number of bytes leaves the caller's stack skewed, which shows up
    // somewhere else entirely.
    [Theory]
    [InlineData(0x0Cu)]
    public void Setter1PopsItsArguments(uint bytesPopped)
    {
        var code = ArmourResistanceExpansionPatch.BuildSetter1(Storage);

        code[^3].ShouldBe((byte)0xC2);
        BitConverter.ToUInt16(code, code.Length - 2).ShouldBe((ushort)bytesPopped);
    }

    [Theory]
    [InlineData(0x2Cu)]
    public void Setter2PopsItsArguments(uint bytesPopped)
    {
        var code = ArmourResistanceExpansionPatch.BuildSetter2(Storage);

        code[^3].ShouldBe((byte)0xC2);
        BitConverter.ToUInt16(code, code.Length - 2).ShouldBe((ushort)bytesPopped);
    }

    // Both hooks displace instructions and have to resume where the client expects.
    [Fact]
    public void ArmourDisplayHookReturnsIntoTheClient()
    {
        var at = Cave + 0xC0;
        var code = ArmourResistanceExpansionPatch.BuildArmourDisplayHook(Storage, at);

        ResumePoint(at, code).ShouldBe(new GameAddress(0x0076_B63C));
    }

    [Fact]
    public void CharacterAckOutputHookReturnsIntoTheClient()
    {
        var at = Cave + 0x140;
        var code = ArmourResistanceExpansionPatch.BuildCharacterAckOutputHook(Storage, at);

        ResumePoint(at, code).ShouldBe(new GameAddress(0x0054_39A5));
    }

    // The setters keep writing the client's own narrow fields as well as the wide ones,
    // so anything this patch did not redirect still reads a consistent value.
    [Fact]
    public void Setter1KeepsTheLegacyNarrowSlotsInStep()
    {
        var code = ArmourResistanceExpansionPatch.BuildSetter1(Storage);

        IndexOf(code, [0x66, 0x89, 0x48, 0x0A]).ShouldBeGreaterThanOrEqualTo(0); // max HP word
        IndexOf(code, [0x66, 0x89, 0x48, 0x0C]).ShouldBeGreaterThanOrEqualTo(0); // max MP word
        IndexOf(code, [0x66, 0x89, 0x48, 0x0E]).ShouldBeGreaterThanOrEqualTo(0); // AC word
    }

    // Relocating the cave has to change every absolute address and every displacement; a
    // builder that ignored its storage would still pass the fixed-address assertions.
    [Fact]
    public void RelocatingTheCaveChangesTheCodeItProduces()
    {
        var elsewhere = new ArmourResistanceExpansionPatch.Storage(
            new GameAddress(0x3000_0000), new GameAddress(0x3000_0004), new GameAddress(0x3000_0008));

        ArmourResistanceExpansionPatch.BuildSetter2(elsewhere)
            .ShouldNotBe(ArmourResistanceExpansionPatch.BuildSetter2(Storage));

        ArmourResistanceExpansionPatch.BuildArmourDisplayHook(elsewhere, Cave + 0xC0)
            .ShouldNotBe(ArmourResistanceExpansionPatch.BuildArmourDisplayHook(Storage, Cave + 0xC0));
    }

    // The cave is 0x180 bytes carved into fixed slots, and a block that outgrew its slot
    // would silently overwrite the next one.
    [Theory]
    [InlineData(0x20, 0xC0)]   // setter 2
    [InlineData(0xC0, 0x100)]  // AC display hook
    [InlineData(0x100, 0x140)] // setter 1
    [InlineData(0x140, 0x180)] // character ack output hook
    public void EveryBlockFitsItsSlot(int start, int next)
    {
        var at = Cave + start;

        var length = start switch
        {
            0x20 => ArmourResistanceExpansionPatch.BuildSetter2(Storage).Length,
            0xC0 => ArmourResistanceExpansionPatch.BuildArmourDisplayHook(Storage, at).Length,
            0x100 => ArmourResistanceExpansionPatch.BuildSetter1(Storage).Length,
            _ => ArmourResistanceExpansionPatch.BuildCharacterAckOutputHook(Storage, at).Length,
        };

        length.ShouldBeLessThanOrEqualTo(next - start);
    }

    private static GameAddress ResumePoint(GameAddress at, byte[] code)
    {
        code[^5].ShouldBe((byte)0xE9, "the block should end with a near jump");
        var displacement = BitConverter.ToInt32(code, code.Length - 4);
        return new GameAddress(unchecked((uint)(at.Value + code.Length + displacement)));
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
}
