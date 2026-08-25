using Login38.Interop;
using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// The displacement arithmetic here is what a wrong byte turns into a jump to
/// arbitrary memory, so it is pinned against hand-computed expectations rather than
/// against the implementation's own output.
/// </summary>
public sealed class ShellcodeBuilderTests
{
    private static readonly GameAddress CaveBase = new(0x0100_0000);

    [Fact]
    public void JumpDisplacementIsMeasuredFromTheEndOfTheInstruction()
    {
        // jmp at 0x01000000, instruction ends at 0x01000005, target 0x01000105
        // => displacement 0x100.
        var code = new ShellcodeBuilder(CaveBase).JumpTo(new GameAddress(0x0100_0105)).Build();

        code.ShouldBe([0xE9, 0x00, 0x01, 0x00, 0x00]);
    }

    [Fact]
    public void BackwardJumpEncodesANegativeDisplacement()
    {
        // pushad occupies 0x01000000; the jump ends at 0x01000006, and jumping back to
        // 0x01000000 is a displacement of -6 => 0xFFFFFFFA.
        var code = new ShellcodeBuilder(CaveBase).PushAd().JumpTo(CaveBase).Build();

        code.ShouldBe([0x60, 0xE9, 0xFA, 0xFF, 0xFF, 0xFF]);
    }

    [Fact]
    public void JumpAccountsForBytesAlreadyEmitted()
    {
        // pushad (1 byte) then jmp: the jump sits at base+1 and ends at base+6.
        var target = new GameAddress(0x0100_0020);

        var code = new ShellcodeBuilder(CaveBase).PushAd().JumpTo(target).Build();

        var displacement = BitConverter.ToInt32(code, 2);
        (CaveBase.Value + 6 + (uint)displacement).ShouldBe(target.Value);
    }

    [Fact]
    public void ShortBranchResolvesToTheMarkedPosition()
    {
        var builder = new ShellcodeBuilder(CaveBase);
        var skip = builder.ShortJumpIfNotEqual();
        builder.PushAd().PushAd().PushAd();
        builder.MarkLabel(skip);
        builder.PopAd();

        var code = builder.Build();

        // jne rel8 at index 0..1; three pushad follow, so the displacement is 3.
        code[0].ShouldBe((byte)0x75);
        code[1].ShouldBe((byte)3);
        code[^1].ShouldBe((byte)0x61);
    }

    [Fact]
    public void ShortBranchToTheNextInstructionIsZero()
    {
        var builder = new ShellcodeBuilder(CaveBase);
        var skip = builder.ShortJumpIfNotEqual();
        builder.MarkLabel(skip);

        builder.Build()[1].ShouldBe((byte)0);
    }

    [Fact]
    public void ShortBranchBeyondRangeIsRejected()
    {
        var builder = new ShellcodeBuilder(CaveBase);
        var skip = builder.ShortJumpIfNotEqual();
        builder.Bytes(new byte[sbyte.MaxValue + 1]);

        Should.Throw<InvalidOperationException>(() => builder.MarkLabel(skip));
    }

    [Fact]
    public void MovEaxEmitsLittleEndianImmediate() =>
        new ShellcodeBuilder(CaveBase).MovEax(0xDEAD_BEEF).Build()
            .ShouldBe([0xB8, 0xEF, 0xBE, 0xAD, 0xDE]);

    [Fact]
    public void MovDwordPtrEmitsAbsoluteAddressThenImmediate() =>
        new ShellcodeBuilder(CaveBase).MovDwordPtr(new GameAddress(0x004B_3EE0), 0).Build()
            .ShouldBe([0xC7, 0x05, 0xE0, 0x3E, 0x4B, 0x00, 0x00, 0x00, 0x00, 0x00]);

    [Fact]
    public void CurrentAddressTracksEmittedLength()
    {
        var builder = new ShellcodeBuilder(CaveBase);

        builder.CurrentAddress.ShouldBe(CaveBase);
        builder.PushAd();
        builder.CurrentAddress.ShouldBe(CaveBase + 1);
    }
}
