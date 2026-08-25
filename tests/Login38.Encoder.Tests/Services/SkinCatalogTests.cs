using Login38.Encoder.Services;
using Shouldly;

namespace Login38.Encoder.Tests.Services;

/// <summary>Covers the looks an operator can give the launcher.</summary>
public sealed class SkinCatalogTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "login38-skins-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Install(string name, bool complete = true)
    {
        var directory = SkinCatalog.PathFor(_root, name);

        Directory.CreateDirectory(directory);

        if (complete)
        {
            File.WriteAllText(Path.Combine(directory, SkinCatalog.IndexName), "<html></html>");
        }
    }

    [Fact]
    public void ListsWhatIsInstalled()
    {
        Install("classic");
        Install("dark");

        SkinCatalog.Scan(_root).ShouldBe(["classic", "dark"]);
    }

    // A directory with nothing to open is a look that shows nothing.
    [Fact]
    public void LeavesOutADirectoryWithNoPageInIt()
    {
        Install("classic");
        Install("empty", complete: false);

        SkinCatalog.Scan(_root).ShouldBe(["classic"]);
    }

    // A combo box with nothing in it cannot be chosen from.
    [Fact]
    public void AlwaysOffersAtLeastOne() =>
        SkinCatalog.Scan(_root).ShouldBe([SkinCatalog.Default]);

    [Fact]
    public void OffersTheDefaultWhenEveryDirectoryIsEmpty()
    {
        Install("empty", complete: false);

        SkinCatalog.Scan(_root).ShouldBe([SkinCatalog.Default]);
    }

    [Fact]
    public void PutsAnImageInAsTheBackground()
    {
        Install("classic");

        var image = Path.Combine(_root, "wallpaper.png");
        File.WriteAllBytes(image, [1, 2, 3]);

        var target = SkinCatalog.ApplyBackground(_root, "classic", image);

        target.ShouldEndWith(SkinCatalog.BackgroundName);
        File.ReadAllBytes(target).ShouldBe([(byte)1, 2, 3]);
    }

    [Fact]
    public void ReplacesABackgroundThatIsAlreadyThere()
    {
        Install("classic");
        File.WriteAllBytes(Path.Combine(SkinCatalog.PathFor(_root, "classic"), SkinCatalog.BackgroundName), [9]);

        var image = Path.Combine(_root, "wallpaper.png");
        File.WriteAllBytes(image, [1, 2, 3]);

        File.ReadAllBytes(SkinCatalog.ApplyBackground(_root, "classic", image)).ShouldBe([(byte)1, 2, 3]);
    }

    [Fact]
    public void RefusesToApplyToALookThatIsNotInstalled()
    {
        var image = Path.Combine(_root, "wallpaper.png");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(image, [1]);

        Should.Throw<DirectoryNotFoundException>(
            () => SkinCatalog.ApplyBackground(_root, "nothing", image));
    }
}
