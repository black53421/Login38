using Login38.Core.Compatibility;
using Shouldly;

namespace Login38.Core.Tests.Compatibility;

/// <summary>
/// Covers the merge, which is the part with anything to get wrong.
/// </summary>
/// <remarks>
/// This value is shared with the Properties dialog: the player may have set flags of
/// their own, and overwriting them would silently undo settings the launcher never knew
/// about.
/// </remarks>
public sealed class AppCompatibilityFlagsTests
{
    private const string DisableFullscreen = AppCompatibilityFlags.DisableFullscreenOptimizations;

    [Fact]
    public void AddsAFlagToAnEmptyValue() =>
        AppCompatibilityFlags.Merge(null, DisableFullscreen)
            .ShouldBe("~ DISABLEDXMAXIMIZEDWINDOWEDMODE");

    [Fact]
    public void KeepsFlagsThatAreAlreadyThere() =>
        AppCompatibilityFlags.Merge("~ HIGHDPIAWARE", DisableFullscreen)
            .ShouldBe("~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE");

    // The Properties dialog does not normalise what it writes, and Windows compares these
    // case-insensitively. Matching case-sensitively would add a duplicate every launch.
    [Fact]
    public void IsIdempotentRegardlessOfCase() =>
        AppCompatibilityFlags.Merge("~ disableDxMaximizedWindowedMode", DisableFullscreen)
            .ShouldBe("~ disableDxMaximizedWindowedMode");

    [Fact]
    public void AddsSeveralFlagsAtOnce() =>
        AppCompatibilityFlags.Merge(null, AppCompatibilityFlags.HighDpiAware, DisableFullscreen)
            .ShouldBe("~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE");

    // Windows ignores the whole entry without the leading marker, so it survives a value
    // that has lost it as well as one that never had it.
    [Theory]
    [InlineData("HIGHDPIAWARE")]
    [InlineData("  ~   HIGHDPIAWARE  ")]
    public void AlwaysProducesAMarkedValue(string existing) =>
        AppCompatibilityFlags.Merge(existing, DisableFullscreen)
            .ShouldBe("~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE");

    [Fact]
    public void ProducesJustTheMarkerWhenThereIsNothingToSet() =>
        AppCompatibilityFlags.Merge(null).ShouldBe("~");
}
