using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// Covers window discovery and the random title.
/// </summary>
/// <remarks>
/// The title generator is a pure function of a seed, so it is checked directly. Window
/// discovery is checked against this process, which in a console test host has no visible
/// window — which is itself the case worth pinning, because "not yet" has to be reported
/// as absence rather than as a failure.
/// </remarks>
public sealed class GameWindowTests
{
    [Fact]
    public void ReportsNoWindowForAProcessThatHasNone() =>
        GameWindow.Find(0xFFFF_FFF0).ShouldBeNull();

    [Fact]
    public async Task GivesUpWaitingRatherThanHangingForever()
    {
        var found = await GameWindow.WaitAsync(0xFFFF_FFF0, TimeSpan.FromMilliseconds(250));

        found.ShouldBeNull();
    }

    [Fact]
    public async Task StopsWaitingWhenAsked()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(
            () => GameWindow.WaitAsync(0xFFFF_FFF0, TimeSpan.FromMinutes(5), cancellation.Token));
    }

    // Long enough not to be guessable at a glance, short enough to stay a window title.
    [Theory]
    [InlineData(1ul)]
    [InlineData(42ul)]
    [InlineData(0xDEAD_BEEFul)]
    [InlineData(ulong.MaxValue)]
    public void ProducesATitleOfAWorkableLength(ulong seed) =>
        GameWindow.RandomTitle(seed).Length.ShouldBeInRange(8, 16);

    // Anything outside this risks a code page problem in a title the client did not write.
    [Fact]
    public void ProducesOnlyLettersAndDigits() =>
        GameWindow.RandomTitle(0x1234_5678).ShouldAllBe(c => char.IsAsciiLetterOrDigit(c));

    [Fact]
    public void IsDeterministicForASeed() =>
        GameWindow.RandomTitle(7).ShouldBe(GameWindow.RandomTitle(7));

    [Fact]
    public void DiffersBetweenSeeds() =>
        GameWindow.RandomTitle(7).ShouldNotBe(GameWindow.RandomTitle(8));

    // A zero state makes xorshift produce zero forever, which would give every launch the
    // same title — the one thing this is meant to avoid.
    [Fact]
    public void SurvivesASeedOfZero() =>
        GameWindow.RandomTitle(0).ShouldNotBeNullOrWhiteSpace();

    // Two copies launched together must not end up with the same title, or renaming them
    // has achieved nothing.
    [Fact]
    public void SeedsDifferBetweenProcesses() =>
        GameWindow.TitleSeed(1000).ShouldNotBe(GameWindow.TitleSeed(1001));
}
