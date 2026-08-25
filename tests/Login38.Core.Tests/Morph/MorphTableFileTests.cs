using System.Text;
using Login38.Core.Morph;
using Shouldly;

namespace Login38.Core.Tests.Morph;

/// <summary>
/// Covers which morph table gets picked, and when the feature turns itself on.
/// </summary>
/// <remarks>
/// Run against real files in a temporary directory: the choice depends on a package being
/// valid, and validity is the tag rather than the extension — which no stub of the file
/// system would model correctly.
/// </remarks>
public sealed class MorphTableFileTests : IDisposable
{
    private const string Executable = "TW13081901.bin";

    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"morph-{Guid.NewGuid():N}")).FullName;

    private string Package => Path.Combine(_directory, "TW13081901.pak");

    private string Text => Path.Combine(_directory, "TW13081901.txt");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void FindsNothingInAnEmptyDirectory() =>
        MorphTableFile.Locate(_directory, Executable).ShouldBeNull();

    [Fact]
    public void FindsAPackageBesideTheClient()
    {
        File.WriteAllBytes(Package, MorphPackage.Encrypt("1234\tcloak\n"u8));

        MorphTableFile.Locate(_directory, Executable).ShouldBe(new MorphTableSource(Package, IsPackage: true));
    }

    [Fact]
    public void FallsBackToTheTextTable()
    {
        File.WriteAllText(Text, "1234\tcloak\n");

        MorphTableFile.Locate(_directory, Executable).ShouldBe(new MorphTableSource(Text, IsPackage: false));
    }

    // The published package is what players get, so it wins over the working file an
    // operator happens to have left in the directory.
    [Fact]
    public void PrefersThePackageOverTheText()
    {
        File.WriteAllBytes(Package, MorphPackage.Encrypt("1234\tcloak\n"u8));
        File.WriteAllText(Text, "9999\tsomething else\n");

        MorphTableFile.Locate(_directory, Executable)!.Value.IsPackage.ShouldBeTrue();
    }

    // A stale or third-party package must not shadow the text file. Passing over it leaves
    // the operator with something that works while they rebuild.
    [Fact]
    public void PassesOverAPackageItWillNotAccept()
    {
        var forged = MorphPackage.Encrypt("1234\tcloak\n"u8);
        forged[^1] ^= 0xFF;

        File.WriteAllBytes(Package, forged);
        File.WriteAllText(Text, "9999\tworking copy\n");

        MorphTableFile.Locate(_directory, Executable).ShouldBe(new MorphTableSource(Text, IsPackage: false));
    }

    [Fact]
    public void FindsNothingWhenOnlyABadPackageIsThere()
    {
        File.WriteAllBytes(Package, Encoding.ASCII.GetBytes("not a package at all"));

        MorphTableFile.Locate(_directory, Executable).ShouldBeNull();
    }

    // Building a package is a deliberate act. Requiring a separate switch on top of it is
    // a support call waiting to happen.
    [Fact]
    public void APublishedPackageTurnsTheFeatureOn() =>
        MorphTableFile.ShouldHook(requested: false, new MorphTableSource("x.pak", IsPackage: true))
            .ShouldBeTrue();

    // A text file is what sits in the directory while someone edits it, so it waits to be
    // asked for.
    [Fact]
    public void ATextTableWaitsToBeAskedFor()
    {
        MorphTableFile.ShouldHook(requested: false, new MorphTableSource("x.txt", IsPackage: false))
            .ShouldBeFalse();

        MorphTableFile.ShouldHook(requested: true, new MorphTableSource("x.txt", IsPackage: false))
            .ShouldBeTrue();
    }

    // Asking for the feature with no table to serve is a request that cannot be met, and
    // hooking the client to hand it nothing is worse than not hooking it.
    [Fact]
    public void DoesNothingWithNoTableAtAll() =>
        MorphTableFile.ShouldHook(requested: true, null).ShouldBeFalse();

    // An operator with more than one table names the one they want. The reference had a
    // dropdown of them and saved the choice nowhere, so it always loaded the same file.
    [Fact]
    public void TakesTheTableTheOperatorNamed()
    {
        Pack("TW13081901.pak");
        Pack("halloween.pak");

        var found = MorphTableFile.Locate(_directory, "TW13081901.bin", "halloween");

        found.ShouldNotBeNull();
        Path.GetFileName(found!.Value.Path).ShouldBe("halloween.pak");
        found.Value.IsPackage.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackToTheClientsOwnNameWhenNothingWasChosen(string? chosen)
    {
        Pack("TW13081901.pak");
        Pack("halloween.pak");

        var found = MorphTableFile.Locate(_directory, "TW13081901.bin", chosen);

        Path.GetFileName(found!.Value.Path).ShouldBe("TW13081901.pak");
    }

    // The name is taken as given, extension and all, so an operator who types the file
    // name they see in the folder gets the file they meant.
    [Fact]
    public void IgnoresAnExtensionOnWhatWasChosen()
    {
        Pack("halloween.pak");

        MorphTableFile.Locate(_directory, "TW13081901.bin", "halloween.pak").ShouldNotBeNull();
    }

    // Silently loading a different table would be indistinguishable from loading the one
    // that was asked for, and the operator would have no way to tell.
    [Fact]
    public void FindsNothingWhenTheNamedTableIsNotThere()
    {
        Pack("TW13081901.pak");

        MorphTableFile.Locate(_directory, "TW13081901.bin", "halloween").ShouldBeNull();
    }

    // A server list is a file an operator hands around. A name in it must not be able to
    // reach outside the game's own directory.
    [Theory]
    [InlineData("..\\halloween")]
    [InlineData("sub/halloween")]
    [InlineData("C:\\elsewhere\\halloween")]
    public void TakesOnlyTheNameOutOfWhatWasChosen(string chosen)
    {
        Pack("halloween.pak");

        var found = MorphTableFile.Locate(_directory, "TW13081901.bin", chosen);

        found.ShouldNotBeNull();
        Path.GetDirectoryName(found!.Value.Path).ShouldBe(_directory);
    }

    [Fact]
    public void TakesANamedTextTableWhenThereIsNoPackage()
    {
        File.WriteAllText(Path.Combine(_directory, "halloween.txt"), "0\t0\t0\n");

        var found = MorphTableFile.Locate(_directory, "TW13081901.bin", "halloween");

        found.ShouldNotBeNull();
        found!.Value.IsPackage.ShouldBeFalse();
    }

    private void Pack(string name) =>
        File.WriteAllBytes(
            Path.Combine(_directory, name), MorphPackage.Encrypt("0\t0\t0\n"u8.ToArray()));
}
