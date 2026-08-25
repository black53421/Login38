using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// Covers the text form of a pattern, which several patches build at runtime.
/// </summary>
public sealed class BytePatternFormatTests
{
    [Fact]
    public void FormatsBytesAsPatternText() =>
        BytePattern.Format([0x97, 0x18, 0x00, 0x00]).ShouldBe("97 18 00 00");

    [Fact]
    public void FormatsASingleByteWithoutASeparator() =>
        BytePattern.Format([0xEB]).ShouldBe("EB");

    [Fact]
    public void FormatsNothingAsAnEmptyString() =>
        BytePattern.Format([]).ShouldBe(string.Empty);

    // The patches that substitute a value into a signature depend on this: the formatted
    // text has to be something Parse reads back as the same bytes.
    [Theory]
    [InlineData(6295u)]
    [InlineData(100_000u)]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void RoundTripsThroughParse(uint value)
    {
        var bytes = BitConverter.GetBytes(value);

        BytePattern.Parse(BytePattern.Format(bytes)).Matches(bytes).ShouldBeTrue();
    }
}
