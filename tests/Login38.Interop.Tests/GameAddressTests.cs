using Login38.Interop;
using Shouldly;

namespace Login38.Interop.Tests;

public sealed class GameAddressTests
{
    [Fact]
    public void FormatsAsEightHexDigits() =>
        new GameAddress(0x004B_3EE0).ToString().ShouldBe("0x004B3EE0");

    [Theory]
    [InlineData("0x004B3EE0")]
    [InlineData("004B3EE0")]
    [InlineData("  0x004b3ee0  ")]
    public void ParsesWithOrWithoutPrefixOrCase(string text) =>
        GameAddress.FromHex(text).ShouldBe(new GameAddress(0x004B_3EE0));

    [Theory]
    [InlineData("not hex")]
    [InlineData("")]
    [InlineData("0x1_0000_0000")]
    public void RejectsInvalidText(string text) =>
        GameAddress.TryParseHex(text, out _).ShouldBeFalse();

    [Fact]
    public void SubtractionYieldsTheSignedDistanceUsedByRelativeJumps() =>
        (new GameAddress(0x0100_0000) - new GameAddress(0x0100_0005)).ShouldBe(-5);

    // Address arithmetic stands in for pointer arithmetic, which wraps.
    [Fact]
    public void AdditionWrapsAtTheTopOfTheAddressSpace() =>
        (new GameAddress(0xFFFF_FFFF) + 1).ShouldBe(GameAddress.Zero);

    [Fact]
    public void OrdersByNumericValue()
    {
        (new GameAddress(0x0040_0000) < new GameAddress(0x0050_0000)).ShouldBeTrue();
        (new GameAddress(0x0050_0000) >= new GameAddress(0x0050_0000)).ShouldBeTrue();
    }

    [Fact]
    public void ZeroIsNull()
    {
        GameAddress.Zero.IsNull.ShouldBeTrue();
        new GameAddress(1).IsNull.ShouldBeFalse();
    }
}
