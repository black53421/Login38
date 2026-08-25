using Login38.Core.Servers;
using Login38.Encoder.Services;
using Login38.Encoder.ViewModels;
using Shouldly;

namespace Login38.Encoder.Tests.ViewModels;

/// <summary>Covers what the launcher itself looks like and links to.</summary>
public sealed class LauncherViewModelTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "login38-launcher-vm-" + Guid.NewGuid().ToString("N"));

    private readonly StubPrompts _prompts = new();

    public LauncherViewModelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LauncherViewModel Page() => new(_root, _prompts);

    private void Install(string name)
    {
        var directory = SkinCatalog.PathFor(_root, name);

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, SkinCatalog.IndexName), "<html></html>");
    }

    [Fact]
    public void CarriesEverySettingThroughARoundTrip()
    {
        Install("classic");

        var page = Page();
        page.Load(new LauncherConfig
        {
            ActiveSkin = "classic",
            AnnouncementEnabled = false,
            AnnouncementUrl = "https://a.invalid/",
            ListUpdateEnabled = true,
            ListUpdateUrl = "https://b.invalid/",
            AutoUpdateEnabled = true,
            AutoUpdateUrl = "https://c.invalid/",
            OfficialUrl = "https://d.invalid/",
            CustomerServiceUrl = "https://e.invalid/",
        });

        var back = page.ToConfig();

        back.ActiveSkin.ShouldBe("classic");
        back.AnnouncementEnabled.ShouldBeFalse();
        back.AnnouncementUrl.ShouldBe("https://a.invalid/");
        back.ListUpdateEnabled.ShouldBeTrue();
        back.ListUpdateUrl.ShouldBe("https://b.invalid/");
        back.AutoUpdateEnabled.ShouldBeTrue();
        back.AutoUpdateUrl.ShouldBe("https://c.invalid/");
        back.OfficialUrl.ShouldBe("https://d.invalid/");
        back.CustomerServiceUrl.ShouldBe("https://e.invalid/");
    }

    [Fact]
    public void AlwaysOffersSomethingToChoose() =>
        Page().Skins.ShouldBe([SkinCatalog.Default]);

    // A look installed since this window opened cannot be shown until it is looked for.
    [Fact]
    public void FindsALookInstalledWhileItWasOpen()
    {
        var page = Page();

        Install("classic");
        page.Rescan();

        page.Skins.ShouldContain("classic");
    }

    [Fact]
    public void KeepsTheChosenLookAcrossARescan()
    {
        Install("classic");
        Install("dark");

        var page = Page();
        page.Skin = "dark";
        page.Rescan();

        page.Skin.ShouldBe("dark");
    }

    [Fact]
    public void FallsBackWhenTheChosenLookIsGone()
    {
        Install("classic");

        var page = Page();
        page.Skin = "classic";

        Directory.Delete(SkinCatalog.PathFor(_root, "classic"), recursive: true);
        page.Rescan();

        page.Skin.ShouldBe(SkinCatalog.Default);
    }

    // A settings file naming a look nobody has installed must not leave the box blank.
    [Fact]
    public void ShowsSomethingForALookThatIsNotInstalled()
    {
        var page = Page();

        page.Load(new LauncherConfig { ActiveSkin = "somebody-elses" });

        page.Skin.ShouldBe(SkinCatalog.Default);
    }

    [Fact]
    public void AppliesNothingUntilAnImageIsChosen()
    {
        var page = Page();

        page.ApplyBackgroundCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void PutsTheChosenImageInAsTheBackground()
    {
        Install("classic");

        var image = Path.Combine(_root, "wallpaper.png");
        File.WriteAllBytes(image, [1, 2, 3]);

        var page = Page();
        page.Skin = "classic";
        _prompts.File = image;
        page.BrowseBackground();

        page.ApplyBackgroundCommand.CanExecute(null).ShouldBeTrue();
        page.ApplyBackground();

        File.ReadAllBytes(Path.Combine(SkinCatalog.PathFor(_root, "classic"), SkinCatalog.BackgroundName))
            .ShouldBe([(byte)1, 2, 3]);
        _prompts.Said.ShouldNotBeEmpty();
    }

    [Fact]
    public void SaysSoWhenTheImageIsNotThere()
    {
        Install("classic");

        var page = Page();
        page.Skin = "classic";
        page.Background = Path.Combine(_root, "nowhere.png");

        page.ApplyBackground();

        _prompts.Said[0].ShouldContain("背景圖套用失敗");
    }

    [Fact]
    public void TrimsTheLinksItWritesOut()
    {
        var page = Page();
        page.OfficialUrl = "  https://d.invalid/  ";

        page.ToConfig().OfficialUrl.ShouldBe("https://d.invalid/");
    }
}
