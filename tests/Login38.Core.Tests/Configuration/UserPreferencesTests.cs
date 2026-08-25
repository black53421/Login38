using Login38.Core.Configuration;
using Shouldly;

namespace Login38.Core.Tests.Configuration;

public sealed class UserPreferencesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"login38-prefs-{Guid.NewGuid():N}.ini");

    [Fact]
    public void DefaultsToWindowedAt800x600()
    {
        var preferences = new UserPreferences();

        preferences.Windowed.ShouldBeTrue();
        preferences.WindowMode.ShouldBe(WindowMode.Size800x600);
    }

    [Fact]
    public void RoundTripsThroughTheFile()
    {
        new UserPreferences { Windowed = false, WindowMode = WindowMode.Size1600x1200 }
            .TrySave(_path).ShouldBeTrue();

        var loaded = UserPreferences.Load(_path);

        loaded.Windowed.ShouldBeFalse();
        loaded.WindowMode.ShouldBe(WindowMode.Size1600x1200);
    }

    // Older readers do not recognise bool.ToString()'s "True".
    [Fact]
    public void WritesLowerCaseBooleans()
    {
        new UserPreferences { Windowed = true }.TrySave(_path);

        File.ReadAllText(_path).ShouldContain("windowed=true");
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var loaded = UserPreferences.Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.ini"));

        loaded.Windowed.ShouldBeTrue();
        loaded.WindowMode.ShouldBe(WindowMode.Size800x600);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("nonsense", false)]
    public void AcceptsEveryBooleanSpellingItWrites(string value, bool expected)
    {
        File.WriteAllText(_path, $"[Settings]\nwindowed={value}\n");

        UserPreferences.Load(_path).Windowed.ShouldBe(expected);
    }

    // The four sizes are not a continuum, so an out-of-range value falls back rather
    // than clamping to the nearest.
    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("8")]
    [InlineData("255")]
    [InlineData("not a number")]
    public void OutOfRangeWindowModeKeepsTheDefault(string value)
    {
        File.WriteAllText(_path, $"[Settings]\nwindow_mode={value}\n");

        UserPreferences.Load(_path).WindowMode.ShouldBe(WindowMode.Size800x600);
    }

    [Fact]
    public void MalformedFileStillYieldsUsablePreferences()
    {
        File.WriteAllText(_path, "garbage\n; comment\n[Settings]\nwindowed=false\nstray line\n");

        UserPreferences.Load(_path).Windowed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(WindowMode.Size400x300, 400, 300)]
    [InlineData(WindowMode.Size800x600, 800, 600)]
    [InlineData(WindowMode.Size1200x900, 1200, 900)]
    [InlineData(WindowMode.Size1600x1200, 1600, 1200)]
    public void ResolutionMatchesTheMode(WindowMode mode, int width, int height) =>
        mode.ToResolution().ShouldBe(new Resolution(width, height));

    // The enum's numeric values are the on-disk representation.
    [Theory]
    [InlineData(4, WindowMode.Size400x300)]
    [InlineData(5, WindowMode.Size800x600)]
    [InlineData(6, WindowMode.Size1200x900)]
    [InlineData(7, WindowMode.Size1600x1200)]
    public void RawValuesMapToModes(byte raw, WindowMode expected)
    {
        raw.ToWindowMode().ShouldBe(expected);
        ((byte)expected).ShouldBe(raw);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
