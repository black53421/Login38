using Login38.Core.Icons;
using Shouldly;

namespace Login38.Core.Tests.Icons;

/// <summary>
/// Covers the clock every animated icon runs on.
/// </summary>
/// <remarks>
/// This is the specification for the machine code that does the same arithmetic inside the
/// game. Nothing at runtime calls it — its value is that the two agree, and that the game
/// side has something to be checked against.
/// </remarks>
public sealed class IconAnimationTests
{
    // Three frames of 100ms, then 200ms of the client's own icon.
    private static readonly IconAnimation Sample = new(1, 100, 200, [10, 11, 12]);

    [Fact]
    public void MeasuresACycleAsTheAnimationPlusTheRest() => Sample.CycleMilliseconds.ShouldBe(500u);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(250, 2)]
    public void HoldsEachFrameForItsOwnTime(uint milliseconds, int frame) =>
        Sample.FrameAt(milliseconds).ShouldBe(frame);

    [Theory]
    [InlineData(300)]
    [InlineData(499)]
    public void LeavesTheClientsOwnIconAloneWhileResting(uint milliseconds) =>
        Sample.FrameAt(milliseconds).ShouldBeNull();

    // A function of the clock alone, so every copy of the same icon on screen is on the same
    // frame without anything having to keep them together.
    [Fact]
    public void RepeatsEveryCycle()
    {
        Sample.FrameAt(500).ShouldBe(0);
        Sample.FrameAt(800).ShouldBeNull();
        Sample.FrameAt(1_000_500).ShouldBe(Sample.FrameAt(500));
    }

    // The game's clock wraps after about forty-nine days. The arithmetic is a remainder, so
    // the wrap costs one shortened cycle rather than anything worse.
    [Fact]
    public void KeepsWorkingWhereTheGamesClockWraps() =>
        Sample.FrameAt(uint.MaxValue).ShouldBe((int?)((uint.MaxValue % 500) / 100));

    // No rest at all is a legitimate setting: an icon that never stops moving.
    [Fact]
    public void PlaysContinuouslyWithNoRest()
    {
        var continuous = new IconAnimation(1, 50, 0, [1, 2]);

        continuous.FrameAt(0).ShouldBe(0);
        continuous.FrameAt(50).ShouldBe(1);
        continuous.FrameAt(100).ShouldBe(0);
    }

    // Refused when the manifest is read, and guarded again in the game — because in the game
    // it is a division by zero, which ends the client rather than the animation.
    [Fact]
    public void DrawsNothingWhenACycleWouldBeZero() =>
        new IconAnimation(1, 0, 0, [1]).FrameAt(0).ShouldBeNull();
}
