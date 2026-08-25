using Login38.Interop;
using Shouldly;

namespace Login38.Interop.Tests;

public sealed class BytePatternTests
{
    [Fact]
    public void MatchesAnExactSignature() =>
        BytePattern.Parse("8B 45 EC").IndexIn([0x00, 0x8B, 0x45, 0xEC, 0xFF]).ShouldBe(1);

    [Theory]
    [InlineData("8B ?? EC")]
    [InlineData("8B ? EC")]
    public void WildcardMatchesAnyByte(string pattern) =>
        BytePattern.Parse(pattern).IndexIn([0x8B, 0x99, 0xEC]).ShouldBe(0);

    [Fact]
    public void ReturnsMinusOneWhenAbsent() =>
        BytePattern.Parse("DE AD BE EF").IndexIn([0x00, 0x11, 0x22]).ShouldBe(-1);

    // The fast path skips to the first fixed byte, so a leading wildcard is the case
    // most likely to be mishandled.
    [Fact]
    public void HandlesALeadingWildcard() =>
        BytePattern.Parse("?? ?? BE EF").IndexIn([0x00, 0x11, 0x22, 0x33, 0xBE, 0xEF]).ShouldBe(2);

    [Fact]
    public void MatchesAtTheVeryEndOfTheBuffer() =>
        BytePattern.Parse("BE EF").IndexIn([0x00, 0x11, 0xBE, 0xEF]).ShouldBe(2);

    [Fact]
    public void DoesNotMatchATruncatedTail() =>
        BytePattern.Parse("BE EF C0").IndexIn([0x00, 0xBE, 0xEF]).ShouldBe(-1);

    [Fact]
    public void FindsTheFirstOfSeveralMatches() =>
        BytePattern.Parse("AA BB").IndexIn([0xAA, 0xBB, 0xAA, 0xBB]).ShouldBe(0);

    // A near-miss sharing the first byte must not stop the search.
    [Fact]
    public void ResumesAfterAPartialMatch() =>
        BytePattern.Parse("AA BB").IndexIn([0xAA, 0xCC, 0xAA, 0xBB]).ShouldBe(2);

    [Fact]
    public void ReportsItsLengthIncludingWildcards() =>
        BytePattern.Parse("8B ?? ?? EC").Length.ShouldBe(4);

    [Theory]
    [InlineData("ZZ")]
    [InlineData("8B 4")]
    [InlineData("8B 123")]
    public void RejectsMalformedPatternText(string pattern) =>
        Should.Throw<FormatException>(() => BytePattern.Parse(pattern));

    [Fact]
    public void ExactBuildsAWildcardFreePattern()
    {
        var pattern = BytePattern.Exact([0x6A, 0x01]);

        pattern.Matches([0x6A, 0x01]).ShouldBeTrue();
        pattern.Matches([0x6A, 0x02]).ShouldBeFalse();
    }
}
