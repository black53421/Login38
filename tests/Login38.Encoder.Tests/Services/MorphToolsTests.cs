using System.Text;
using Login38.Core.Morph;
using Login38.Core.Text;
using Login38.Encoder.Services;
using Shouldly;

namespace Login38.Encoder.Tests.Services;

/// <summary>
/// Covers the operator's side of the morph table.
/// </summary>
/// <remarks>
/// One text file feeds two different things — the launcher, which reads a packed copy, and
/// the server, which needs the animation timings. Both are exercised against the readers
/// that consume them rather than against a recorded blob.
/// </remarks>
public sealed class MorphToolsTests : IDisposable
{
    private const string Table = "#100 80 name\n110.framerate(30)\n4.attack(1 1,9.0:5)\n";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-morph-" + Guid.NewGuid().ToString("N"));

    public MorphToolsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);

        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void PacksATableTheLauncherWillLoad()
    {
        var source = Write("TW13081901.txt", Table);
        var output = MorphTools.OutputFor(_directory, source);

        MorphTools.Encode(source, output);

        MorphTools.IsOurs(output).ShouldBeTrue();
    }

    // The launcher unpacks it, so what comes back out has to be what went in — behind the
    // marker byte the format puts in front, which is what the client's own loader skips.
    [Fact]
    public void PacksSomethingThatUnpacksToTheSameTable()
    {
        var source = Write("TW13081901.txt", Table);
        var output = MorphTools.OutputFor(_directory, source);

        MorphTools.Encode(source, output);

        Encoding.UTF8.GetString(MorphPackage.Load(output))
            .ShouldBe((char)MorphPackage.Marker + Table);
    }

    [Fact]
    public void PacksWithoutDeflatingWhenAskedNotTo()
    {
        var source = Write("TW13081901.txt", Table);
        var output = MorphTools.OutputFor(_directory, source);

        MorphTools.Encode(source, output, compress: false);

        Encoding.UTF8.GetString(MorphPackage.Load(output))
            .ShouldBe((char)MorphPackage.Marker + Table);
    }

    // Packing a package again produces something that decrypts to ciphertext, and the
    // launcher rejects it a long way from here.
    [Fact]
    public void RefusesToPackSomethingAlreadyPacked()
    {
        var source = Write("TW13081901.pak", Table);

        Should.Throw<MorphPackageException>(
            () => MorphTools.Encode(source, Path.Combine(_directory, "out.pak")));
    }

    [Fact]
    public void ReportsWhatItPacked()
    {
        var source = Write("TW13081901.txt", Table);
        var report = MorphTools.Encode(source, MorphTools.OutputFor(_directory, source));

        report.TableBytes.ShouldBe(Table.Length);
        report.PackageBytes.ShouldBeGreaterThan(0);
        report.Output.ShouldEndWith("TW13081901.pak");
    }

    [Fact]
    public void NamesThePackageAfterTheTable() =>
        MorphTools.OutputFor(_directory, @"C:\somewhere\else\TW13081901.txt")
            .ShouldBe(Path.Combine(_directory, "TW13081901.pak"));

    [Fact]
    public void ListsOnlyThePackagesItMade()
    {
        var source = Write("TW13081901.txt", Table);

        MorphTools.Encode(source, MorphTools.OutputFor(_directory, source));
        File.WriteAllBytes(Path.Combine(_directory, "somebody-elses.pak"), new byte[256]);

        MorphTools.Packages(_directory).ShouldBe(["TW13081901.pak"]);
    }

    [Fact]
    public void ListsNothingForADirectoryThatIsNotThere() =>
        MorphTools.Packages(Path.Combine(_directory, "nowhere")).ShouldBeEmpty();

    [Fact]
    public void SaysNoToAFileThatIsNotOneOfOurs()
    {
        var path = Path.Combine(_directory, "plain.pak");

        File.WriteAllText(path, Table);

        MorphTools.IsOurs(path).ShouldBeFalse();
    }

    [Fact]
    public void WritesTheServersAnimationTable()
    {
        var source = Write("TW13081901.txt", Table);
        var output = Path.Combine(_directory, MorphTools.SprActionSqlName);

        MorphTools.WriteSprActionSql(LegacyTextCodec.Auto, source, output).ShouldBe(1);

        File.ReadAllText(output)
            .ShouldBe("INSERT INTO `spr_action` VALUES ('100', '4', '5', '30');\n");
    }

    // The tables operators keep are decades old and are not all UTF-8.
    [Fact]
    public void ReadsATableSavedInTheClientsOwnCodePage()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var source = Path.Combine(_directory, "TW13081901.txt");

        File.WriteAllBytes(source, Encoding.GetEncoding(950).GetBytes("#100 80 [測試]\n4.attack(1 1,9.0:5)\n"));

        MorphTools.WriteSprActionSql(
            LegacyTextCodec.Auto, source, Path.Combine(_directory, MorphTools.SprActionSqlName))
            .ShouldBe(1);
    }

    [Fact]
    public void RefusesATableWithNoSpriteInIt()
    {
        var source = Write("TW13081901.txt", "nothing useful here\n");

        Should.Throw<FormatException>(() => MorphTools.WriteSprActionSql(
            LegacyTextCodec.Auto, source, Path.Combine(_directory, MorphTools.SprActionSqlName)));
    }
}
