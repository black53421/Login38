using Login38.Core.Updates;
using Shouldly;

namespace Login38.Core.Tests.Updates;

public sealed class UpdateManifestTests
{
    [Fact]
    public void ParsesVersionAndPackages()
    {
        var manifest = UpdateManifest.Parse(
            "[Update]\nversion=3\n1=a.zip\n2=b.zip\n3=c.zip\n", "http://x/y/Update.ini");

        manifest.ShouldNotBeNull();
        manifest.ServerVersion.ShouldBe(3u);
        manifest.Packages.ShouldBe(
        [
            new UpdatePackage(1, "a.zip"),
            new UpdatePackage(2, "b.zip"),
            new UpdatePackage(3, "c.zip"),
        ]);
    }

    // Operators hand-edit these files, so order is not guaranteed and a version can
    // appear twice. Ascending order, first occurrence wins.
    [Fact]
    public void SortsAndDeduplicates()
    {
        var manifest = UpdateManifest.Parse(
            "[Update]\n3=c.zip\nversion=3\n1=a.zip\n2=b.zip\n2=B.zip\n", "");

        manifest.ShouldNotBeNull();
        manifest.Packages.Select(p => p.Version).ShouldBe([1u, 2u, 3u]);
        manifest.Packages[1].Entry.ShouldBe("b.zip");
    }

    // Without a version there is nothing to compare against; skipping beats guessing.
    [Fact]
    public void ReturnsNullWithoutAVersionKey() =>
        UpdateManifest.Parse("1=a.zip\n", "").ShouldBeNull();

    [Theory]
    [InlineData("version")]
    [InlineData("Version")]
    [InlineData("VERSION")]
    [InlineData("ver")]
    [InlineData("Ver")]
    public void AcceptsEitherSpellingOfTheVersionKey(string key) =>
        UpdateManifest.Parse($"{key}=7\n", "")!.ServerVersion.ShouldBe(7u);

    [Fact]
    public void IgnoresEntriesWithNoValue() =>
        UpdateManifest.Parse("version=2\n1=\n2=b.zip\n", "")!
            .Packages.ShouldHaveSingleItem().Entry.ShouldBe("b.zip");

    [Fact]
    public void IgnoresCommentsAndUnknownKeys()
    {
        var manifest = UpdateManifest.Parse(
            "; comment\n# comment\n[Update]\nversion=1\nnotanumber=x.zip\n1=a.zip\n", "");

        manifest.ShouldNotBeNull();
        manifest.Packages.ShouldHaveSingleItem().Entry.ShouldBe("a.zip");
    }

    [Theory]
    [InlineData("http://x/y/Update.ini", "http://x/y/")]
    [InlineData("http://x/Update.ini", "http://x/")]
    [InlineData("Update.ini", "")]
    public void BaseUrlKeepsEverythingThroughTheLastSlash(string url, string expected) =>
        UpdateManifest.BaseUrlOf(url).ShouldBe(expected);

    [Theory]
    [InlineData("a.zip", "http://x/y/a.zip")]
    [InlineData("http://other/foo.zip", "http://other/foo.zip")]
    [InlineData("https://other/foo.zip", "https://other/foo.zip")]
    [InlineData("HTTPS://other/foo.zip", "HTTPS://other/foo.zip")]
    public void ResolvesRelativeEntriesAndPassesAbsoluteOnesThrough(string entry, string expected) =>
        UpdateManifest.ResolveUrl("http://x/y/", entry).ShouldBe(expected);

    [Fact]
    public void ResolvesAPackageAgainstItsOwnSource()
    {
        var manifest = UpdateManifest.Parse("version=1\n1=a.zip\n", "http://x/y/Update.ini");

        manifest!.ResolveUrl(manifest.Packages[0]).ShouldBe("http://x/y/a.zip");
    }

    // A client several versions behind applies every intervening step in order.
    [Fact]
    public void SelectsOnlyThePackagesStillNeeded()
    {
        var manifest = UpdateManifest.Parse(
            "version=4\n1=a.zip\n2=b.zip\n3=c.zip\n4=d.zip\n", "");

        manifest!.PackagesAfter(2).Select(p => p.Entry).ShouldBe(["c.zip", "d.zip"]);
    }

    [Fact]
    public void SelectsNothingWhenAlreadyCurrent()
    {
        var manifest = UpdateManifest.Parse("version=2\n1=a.zip\n2=b.zip\n", "");

        manifest!.PackagesAfter(2).ShouldBeEmpty();
    }

    // A package numbered beyond the declared version is not applied: the manifest's
    // own version is the authority on what "current" means.
    [Fact]
    public void IgnoresPackagesBeyondTheDeclaredVersion()
    {
        var manifest = UpdateManifest.Parse("version=2\n1=a.zip\n2=b.zip\n9=future.zip\n", "");

        manifest!.PackagesAfter(0).Select(p => p.Entry).ShouldBe(["a.zip", "b.zip"]);
    }
}
