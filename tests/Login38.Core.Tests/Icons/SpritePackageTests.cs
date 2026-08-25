using System.Text;
using Login38.Core.Icons;
using Shouldly;

namespace Login38.Core.Tests.Icons;

/// <summary>
/// Covers the package format the icon frames are shipped in.
/// </summary>
/// <remarks>
/// The launcher writes these and reads them back with the same code, so the round trip is
/// most of the proof. The rest is what happens to a package that was edited by hand or
/// arrived truncated, which is the only way one goes wrong in practice.
/// </remarks>
public sealed class SpritePackageTests
{
    [Fact]
    public void ReadsBackWhatItPacked()
    {
        var (package, index) = SpritePackage.Build(
        [
            (DynamicIconManifest.FileName, Encoding.ASCII.GetBytes("<dynamicicons/>")),
            ("30001.png", [1, 2, 3, 4, 5]),
        ]);

        SpritePackage.Read(package, index, "30001.png").ToArray().ShouldBe([1, 2, 3, 4, 5]);
        Encoding.ASCII.GetString(SpritePackage.Read(package, index, DynamicIconManifest.FileName).Span)
            .ShouldBe("<dynamicicons/>");
    }

    [Fact]
    public void PacksFilesEndToEndWithNothingBetweenThem()
    {
        var (package, _) = SpritePackage.Build([("a.png", [1, 2]), ("b.png", [3])]);

        package.ShouldBe([1, 2, 3]);
    }

    // The client's own packages are written by tools that disagree about case.
    [Fact]
    public void FindsAFileWhateverCaseItWasAskedFor()
    {
        var (_, index) = SpritePackage.Build([("Frame.PNG", [7])]);

        SpritePackage.Find(index, "frame.png").ShouldNotBeNull();
    }

    [Fact]
    public void ReportsAFileThatIsNotThere()
    {
        var (_, index) = SpritePackage.Build([("a.png", [1])]);

        SpritePackage.Find(index, "b.png").ShouldBeNull();
    }

    [Fact]
    public void NamesTheFileItCouldNotFind()
    {
        var (package, index) = SpritePackage.Build([("a.png", [1])]);

        Should.Throw<DynamicIconException>(() => SpritePackage.Read(package, index, "30002.png"))
            .Message.ShouldContain("30002.png");
    }

    // A hand-edited index can point anywhere. Reading it as given would be an out-of-range
    // read reported from somewhere with no idea what a package is.
    [Fact]
    public void RefusesAnEntryThatPointsPastThePackage()
    {
        var (package, index) = SpritePackage.Build([("a.png", [1, 2, 3])]);

        // The size field of the only entry.
        index[^4] = 0xFF;

        Should.Throw<DynamicIconException>(() => SpritePackage.Read(package, index, "a.png"))
            .Message.ShouldContain("past its end");
    }

    [Fact]
    public void ReadsATruncatedIndexAsHavingNothingInIt()
    {
        var (_, index) = SpritePackage.Build([("a.png", [1])]);

        SpritePackage.Find(index.AsSpan(0, index.Length - 4), "a.png").ShouldBeNull();
        SpritePackage.Find([], "a.png").ShouldBeNull();
    }

    // Twenty bytes, and the name is what the index is for — a name that does not fit cannot
    // be silently shortened without making the file unfindable.
    [Fact]
    public void RefusesANameTooLongForTheIndex() =>
        Should.Throw<ArgumentException>(() =>
            SpritePackage.Build([("this_name_is_far_too_long.png", [0])]));

    [Fact]
    public void PacksNothingAsAnEmptyPackage()
    {
        var (package, index) = SpritePackage.Build([]);

        package.ShouldBeEmpty();
        index.ShouldBe([0, 0, 0, 0]);
    }
}
