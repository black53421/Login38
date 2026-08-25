using System.Globalization;
using Login38.Interop;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class HitPointExpansionPatchTests
{
    private static readonly GameAddress MaxHitPoints = new(0x00C3_1E90);
    private static readonly GameAddress MaxManaPoints = new(0x00C3_1E8C);

    private const string ExpectedSetter1 =
        "8B C1 8B 54 24 04 89 15 90 1E C3 00 8B 54 24 08 89 15 8C 1E C3 00 66 8B 54 24 0C 66 89 " +
        "50 0E C2 0C 00 CC CC CC CC CC CC CC CC CC CC CC CC CC CC";

    private const string ExpectedSetter2 =
        "55 8B EC 51 89 4D FC 8B 45 FC 8A 4D 08 88 48 10 8A 4D 0C 88 48 11 8B 4D 10 89 0D 90 1E " +
        "C3 00 8B 4D 14 89 0D 8C 1E C3 00 66 8B 4D 18 66 89 48 0E 0F BE 4D 1C 89 48 14 0F BE 4D " +
        "20 89 48 18 0F BE 4D 24 89 48 1C 0F BE 4D 28 89 48 20 0F BE 4D 2C 89 48 24 0F BE 4D 30 " +
        "89 48 28 8B E5 5D C2 2C 00 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC " +
        "CC CC CC CC";

    private const string ExpectedReadDword =
        "55 8B EC 51 8B 45 08 8B 08 8B 11 90 89 55 FC 90 8B 45 08 8B 08 83 C1 04 8B 55 08 89 0A " +
        "8B 45 FC 90 8B E5 5D C3";

    // ---- the code this installs -----------------------------------------------------------

    [Fact]
    public void Setter1MatchesTheReferenceByteForByte() =>
        HitPointExpansionPatch.BuildSetter1().ShouldBe(Parse(ExpectedSetter1));

    [Fact]
    public void Setter2MatchesTheReferenceByteForByte() =>
        HitPointExpansionPatch.BuildSetter2().ShouldBe(Parse(ExpectedSetter2));

    [Fact]
    public void DwordPacketReaderMatchesTheReferenceByteForByte() =>
        HitPointExpansionPatch.ReadDwordCode.ToArray().ShouldBe(Parse(ExpectedReadDword));

    // The whole point of the replacement reader: it has to advance the packet cursor by
    // the number of bytes it consumed, or every field after this one is read from the
    // wrong offset.
    [Fact]
    public void DwordPacketReaderAdvancesTheCursorByFour()
    {
        var code = HitPointExpansionPatch.ReadDwordCode.ToArray();

        IndexOf(code, [0x83, 0xC1, 0x04]).ShouldBeGreaterThanOrEqualTo(0);  // add ecx, 4
        IndexOf(code, [0x83, 0xC1, 0x02]).ShouldBe(-1);                     // never add ecx, 2
    }

    // It replaces a function the client calls; the caller cleans the stack, so a bare ret
    // is what balances.
    [Fact]
    public void DwordPacketReaderReturnsWithoutPoppingArguments() =>
        HitPointExpansionPatch.ReadDwordCode[^1].ShouldBe((byte)0xC3);

    [Fact]
    public void GettersReturnTheirGlobalOutright()
    {
        HitPointExpansionPatch.BuildGetter(MaxHitPoints)
            .ShouldBe(Parse("A1 90 1E C3 00 C3 90 90 90 90 90 90 90 90 90 90 90 90"));

        HitPointExpansionPatch.BuildGetter(MaxManaPoints)
            .ShouldBe(Parse("A1 8C 1E C3 00 C3 90 90 90 90 90 90 90 90 90 90 90 90"));
    }

    // These overwrite functions in the middle of a run of them. Anything longer would
    // clip the next one.
    [Fact]
    public void EveryBlockFitsTheSpaceItOverwrites()
    {
        HitPointExpansionPatch.BuildSetter1().Length.ShouldBe(48);
        HitPointExpansionPatch.BuildSetter2().Length.ShouldBe(120);
        HitPointExpansionPatch.BuildGetter(MaxHitPoints).Length.ShouldBe(18);
    }

    // ---- stack locals ------------------------------------------------------------------------

    [Theory]
    // movzx eax, word [ebp-0x10] → mov eax, dword [ebp-0x10]
    [InlineData("0F B7 45 F0", "8B 45 F0 90")]
    // movsx ecx, word [ebp-0x10]
    [InlineData("0F BF 4D F0", "8B 4D F0 90")]
    // mov word [ebp-0x10], edx → mov dword [ebp-0x10], edx
    [InlineData("66 89 55 F0", "89 55 F0 90")]
    // mov bx, word [ebp-0x10]
    [InlineData("66 8B 5D F0", "8B 5D F0 90")]
    // cmp with the operand-size prefix, both directions
    [InlineData("66 3B 75 F0", "3B 75 F0 90")]
    [InlineData("66 39 7D F0", "39 7D F0 90")]
    public void WidensNarrowAccessesToAStackLocal(string instruction, string expected) =>
        HitPointExpansionPatch.WidenLocalAccess(Parse(instruction), 0xF0).ShouldBe(Parse(expected));

    [Theory]
    [InlineData("0F B7 45 E8")]  // a different local
    [InlineData("8B 45 F0 90")]  // already widened
    [InlineData("0F B7 05 F0")]  // [address], not [ebp+disp8]
    [InlineData("66 A1 45 F0")]  // not one of the forms handled
    public void LeavesEverythingElseAlone(string instruction) =>
        HitPointExpansionPatch.WidenLocalAccess(Parse(instruction), 0xF0).ShouldBeNull();

    // Nothing may move: the replacement occupies exactly the bytes it replaced, and the
    // instruction after it has to stay where the surrounding jumps expect.
    [Theory]
    [InlineData("0F B7 45 F0")]
    [InlineData("66 89 55 F0")]
    public void KeepsAStackLocalAccessTheSameLength(string instruction) =>
        HitPointExpansionPatch.WidenLocalAccess(Parse(instruction), 0xF0)!.Length.ShouldBe(4);

    // ---- globals ----------------------------------------------------------------------------

    [Theory]
    // movzx eax, word [maxHp] → mov eax, dword [maxHp]
    [InlineData("0F B7 05 90 1E C3 00 00", "8B 05 90 1E C3 00 90")]
    // movsx ecx, word [maxHp]
    [InlineData("0F BF 0D 90 1E C3 00 00", "8B 0D 90 1E C3 00 90")]
    // mov word [maxHp], edx → mov dword [maxHp], edx
    [InlineData("66 89 15 90 1E C3 00 00", "89 15 90 1E C3 00 90")]
    public void WidensNarrowAccessesToAGlobal(string instruction, string expected) =>
        HitPointExpansionPatch.WidenGlobalAccess(Parse(instruction), MaxHitPoints).ShouldBe(Parse(expected));

    // mov word [maxHp], ax has no ModR/M byte, so the address sits one byte earlier and
    // the whole instruction is six bytes. A seventh byte here would land on whatever
    // follows.
    [Fact]
    public void WidensTheAccumulatorFormOfAGlobalWrite() =>
        HitPointExpansionPatch.WidenGlobalAccess(Parse("66 A3 90 1E C3 00 00"), MaxHitPoints)
            .ShouldBe(Parse("A3 90 1E C3 00 90"));

    [Theory]
    [InlineData("0F B7 05 8C 1E C3 00 00")]  // a different global
    [InlineData("8B 05 90 1E C3 00 90 00")]  // already widened
    [InlineData("0F B6 05 90 1E C3 00 00")]  // a byte read, not a word read
    [InlineData("66 8B 05 90 1E C3 00 00")]  // mov ax, [global] is left alone
    [InlineData("00 00 05 90 1E C3 00 00")]  // the address as data, not an instruction
    public void LeavesEverythingElseAloneForGlobals(string instruction) =>
        HitPointExpansionPatch.WidenGlobalAccess(Parse(instruction), MaxHitPoints).ShouldBeNull();

    [Theory]
    [InlineData("0F B7 05 90 1E C3 00 00", 7)]
    [InlineData("66 89 15 90 1E C3 00 00", 7)]
    [InlineData("66 A3 90 1E C3 00 00", 6)]
    public void KeepsAGlobalAccessTheSameLength(string instruction, int length) =>
        HitPointExpansionPatch.WidenGlobalAccess(Parse(instruction), MaxHitPoints)!.Length.ShouldBe(length);

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
