using Login38.Core.Icons;
using Login38.Encoder.ViewModels;
using Shouldly;

namespace Login38.Encoder.Tests.ViewModels;

/// <summary>
/// Covers building the package of animated item icons.
/// </summary>
/// <remarks>
/// The round trip is what matters: what is packed has to be readable by the same tool, or
/// an operator who adds one icon a month later has to start again from their PNGs.
/// </remarks>
public sealed class IconsViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-icons-" + Guid.NewGuid().ToString("N"));

    private readonly StubPrompts _prompts = new();

    public IconsViewModelTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private IconsViewModel Icons() => new(_prompts);

    /// <summary>Writes some frames and hands them to the next "choose frames".</summary>
    private void Offer(params byte[][] frames)
    {
        var paths = new List<string>();

        for (var i = 0; i < frames.Length; i++)
        {
            var path = Path.Combine(_directory, $"frame-{Guid.NewGuid():N}-{i}.png");

            File.WriteAllBytes(path, frames[i]);
            paths.Add(path);
        }

        _prompts.Files = paths;
    }

    private IconsViewModel WithOneEntry(ushort icon = 42, ushort frame = 80, uint rest = 500)
    {
        var icons = Icons();

        Offer([1, 1, 1], [2, 2, 2]);
        icons.PickFrames();
        icons.Icon = icon;
        icons.FrameMilliseconds = frame;
        icons.RestMilliseconds = rest;
        icons.AddEntry();

        return icons;
    }

    [Fact]
    public void AddsAnEntryFromTheChosenFrames()
    {
        var icons = WithOneEntry();

        icons.Entries.Count.ShouldBe(1);
        icons.Entries[0].Icon.ShouldBe((ushort)42);
        icons.Entries[0].FrameMilliseconds.ShouldBe((ushort)80);
        icons.Entries[0].RestMilliseconds.ShouldBe(500u);
        icons.Entries[0].Frames.Count.ShouldBe(2);
    }

    [Fact]
    public void AddsNothingUntilFramesAreChosen()
    {
        var icons = Icons();

        icons.AddEntryCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void ForgetsTheChosenFramesOnceTheyAreUsed()
    {
        var icons = WithOneEntry();

        icons.AddEntryCommand.CanExecute(null).ShouldBeFalse();
        icons.PendingSummary.ShouldBe("尚未選擇幀");
    }

    // The client scans this table and stops at the first match, so two entries for one
    // icon means the second is never reached.
    [Fact]
    public void ReplacesAnEntryForAnIconThatAlreadyHasOne()
    {
        var icons = WithOneEntry(icon: 42, frame: 80);

        Offer([9]);
        icons.PickFrames();
        icons.Icon = 42;
        icons.FrameMilliseconds = 120;
        icons.AddEntry();

        icons.Entries.Count.ShouldBe(1);
        icons.Entries[0].FrameMilliseconds.ShouldBe((ushort)120);
        icons.Entries[0].Frames.Count.ShouldBe(1);
    }

    [Fact]
    public void KeepsTheEntriesInIconOrder()
    {
        var icons = WithOneEntry(icon: 90);

        Offer([1]);
        icons.PickFrames();
        icons.Icon = 10;
        icons.AddEntry();

        icons.Entries.Select(e => e.Icon).ShouldBe([(ushort)10, 90]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void RefusesAnIconOutsideWhatTheFormatHolds(double icon)
    {
        var icons = Icons();

        Offer([1]);
        icons.PickFrames();
        icons.Icon = icon;
        icons.AddEntry();

        icons.Entries.ShouldBeEmpty();
        icons.Report.ShouldContain("gfxid");
    }

    [Fact]
    public void RefusesAFrameShownForNoTime()
    {
        var icons = Icons();

        Offer([1]);
        icons.PickFrames();
        icons.Icon = 42;
        icons.FrameMilliseconds = 0;
        icons.AddEntry();

        icons.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesMoreFramesThanTheTableHolds()
    {
        var icons = Icons();

        Offer([.. Enumerable.Range(0, IconAnimation.MaxFrames + 1).Select(_ => new byte[] { 1 })]);
        icons.PickFrames();
        icons.Icon = 42;
        icons.AddEntry();

        icons.Entries.ShouldBeEmpty();
        icons.Report.ShouldContain(IconAnimation.MaxFrames.ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    [Fact]
    public void RemovesTheSelectedEntryAndSelectsWhatTookItsPlace()
    {
        var icons = WithOneEntry(icon: 10);

        Offer([1]);
        icons.PickFrames();
        icons.Icon = 20;
        icons.AddEntry();

        icons.Selected = icons.Entries[0];
        icons.RemoveEntry();

        icons.Entries.Select(e => e.Icon).ShouldBe([(ushort)20]);
        icons.Selected.ShouldNotBeNull();
    }

    [Fact]
    public void PacksNothingWhenThereIsNothingToPack()
    {
        var icons = Icons();

        icons.Pack();

        icons.Report.ShouldContain("沒有條目可以打包");
    }

    [Fact]
    public void WritesThePackageAndItsIndex()
    {
        var icons = WithOneEntry();
        icons.PackageName = "555";
        _prompts.Folder = _directory;

        icons.Pack();

        File.Exists(Path.Combine(_directory, "555.pak")).ShouldBeTrue();
        File.Exists(Path.Combine(_directory, "555.idx")).ShouldBeTrue();
    }

    // What is packed has to be readable by the same tool, or an operator who adds one icon
    // a month later has to start again from their PNGs.
    [Fact]
    public void ReadsBackWhatItPacked()
    {
        var icons = WithOneEntry(icon: 42, frame: 80, rest: 500);
        icons.PackageName = "555";
        icons.FirstImageId = 31_000;
        _prompts.Folder = _directory;
        icons.Pack();

        var again = Icons();
        _prompts.File = Path.Combine(_directory, "555.pak");
        again.OpenPackage();

        again.Entries.Count.ShouldBe(1);
        again.Entries[0].Icon.ShouldBe((ushort)42);
        again.Entries[0].FrameMilliseconds.ShouldBe((ushort)80);
        again.Entries[0].RestMilliseconds.ShouldBe(500u);
        again.Entries[0].Frames.Count.ShouldBe(2);
        again.Entries[0].Frames[0].ShouldBe([(byte)1, 1, 1]);
        again.Entries[0].Frames[1].ShouldBe([(byte)2, 2, 2]);
        again.PackageName.ShouldBe("555");
        again.FirstImageId.ShouldBe(31_000);
    }

    [Fact]
    public void SaysSoWhenThereIsNoIndexBesideThePackage()
    {
        var package = Path.Combine(_directory, "lonely.pak");
        File.WriteAllBytes(package, [1, 2, 3, 4]);

        var icons = Icons();
        _prompts.File = package;
        icons.OpenPackage();

        icons.Report.ShouldContain("lonely.idx");
        icons.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void SaysSoWhenThePackageHasNoManifest()
    {
        var (package, index) = SpritePackage.Build([("nothing.png", [1, 2, 3])]);

        File.WriteAllBytes(Path.Combine(_directory, "odd.pak"), package);
        File.WriteAllBytes(Path.Combine(_directory, "odd.idx"), index);

        var icons = Icons();
        _prompts.File = Path.Combine(_directory, "odd.pak");
        icons.OpenPackage();

        icons.Report.ShouldContain(DynamicIconManifest.FileName);
        icons.Entries.ShouldBeEmpty();
    }

    // The reference abandoned the whole package on the first missing picture, so one
    // damaged entry cost the operator the other forty.
    [Fact]
    public void ReadsTheEntriesItCanAndSaysWhichItCouldNot()
    {
        var manifest = DynamicIconManifest.ToXml(
        [
            new IconAnimation(10, 80, 0, [31_000]),
            new IconAnimation(20, 80, 0, [31_001]),
        ]);

        var (package, index) = SpritePackage.Build(
        [
            (DynamicIconManifest.FileName, System.Text.Encoding.UTF8.GetBytes(manifest)),
            ("31000.png", [7, 7]),
        ]);

        File.WriteAllBytes(Path.Combine(_directory, "gappy.pak"), package);
        File.WriteAllBytes(Path.Combine(_directory, "gappy.idx"), index);

        var icons = Icons();
        _prompts.File = Path.Combine(_directory, "gappy.pak");
        icons.OpenPackage();

        icons.Entries.Select(e => e.Icon).ShouldBe([(ushort)10]);
        icons.Report.ShouldContain("20");
    }
}
