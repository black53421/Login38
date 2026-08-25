using Login38.Core.Morph;
using Login38.Core.Text;
using Login38.Encoder.Services;
using Login38.Encoder.ViewModels;
using Shouldly;

namespace Login38.Encoder.Tests.ViewModels;

/// <summary>Covers the morph page: pack a table, and read the server's timings out of it.</summary>
public sealed class MorphViewModelTests : IDisposable
{
    private const string Table = "#100 80 name\n4.attack(1 1,9.0:5)\n";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-morph-vm-" + Guid.NewGuid().ToString("N"));

    private readonly StubPrompts _prompts = new();

    public MorphViewModelTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private MorphViewModel Page() => new(_directory, LegacyTextCodec.Auto, _prompts);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);

        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void DoesNothingUntilATableIsChosen()
    {
        var page = Page();

        page.EncodeCommand.CanExecute(null).ShouldBeFalse();
        page.GenerateSqlCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void TakesTheTableFromThePicker()
    {
        _prompts.File = Write("TW13081901.txt", Table);

        var page = Page();
        page.Browse();

        page.Source.ShouldBe(_prompts.File);
        page.EncodeCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void PacksTheTableBesideTheTool()
    {
        var page = Page();
        page.Source = Write("TW13081901.txt", Table);

        page.Encode();

        MorphTools.IsOurs(Path.Combine(_directory, "TW13081901.pak")).ShouldBeTrue();
        page.Report.ShouldContain("TW13081901.pak");
    }

    [Fact]
    public void SaysSoWhenTheTableIsNotThere()
    {
        var page = Page();
        page.Source = Path.Combine(_directory, "nowhere.txt");

        page.Encode();

        page.Report.ShouldContain("nowhere.txt");
        page.Report.ShouldContain("不存在");
    }

    [Fact]
    public void SaysSoWhenAskedToPackSomethingAlreadyPacked()
    {
        var page = Page();
        page.Source = Write("TW13081901.pak", Table);

        page.Encode();

        page.Report.ShouldContain("已經是打包過");
    }

    [Fact]
    public void WritesTheServersAnimationTable()
    {
        var page = Page();
        page.Source = Write("TW13081901.txt", Table);

        page.GenerateSql();

        File.ReadAllText(Path.Combine(_directory, MorphTools.SprActionSqlName))
            .ShouldBe("INSERT INTO `spr_action` VALUES ('100', '4', '5', '24');\n");
        page.Report.ShouldContain(MorphTools.SprActionSqlName);
    }

    [Fact]
    public void SaysSoWhenTheTableHoldsNoSprite()
    {
        var page = Page();
        page.Source = Write("TW13081901.txt", "nothing useful here\n");

        page.GenerateSql();

        page.Report.ShouldContain("寫不出來");
        File.Exists(Path.Combine(_directory, MorphTools.SprActionSqlName)).ShouldBeFalse();
    }

    // The launcher refuses anything it did not make, so an operator whose players cannot
    // log in needs a way to ask that question here rather than by reading a log.
    [Fact]
    public void SaysWhetherAPackedTableWillBeLoaded()
    {
        var source = Write("TW13081901.txt", Table);
        var packed = MorphTools.OutputFor(_directory, source);

        MorphTools.Encode(source, packed);

        var page = Page();
        _prompts.File = packed;
        page.Verify();

        page.Report.ShouldContain("會載入");
    }

    [Fact]
    public void SaysWhenAPackedTableWillBeRefused()
    {
        var page = Page();
        _prompts.File = Write("somebody-elses.pak", Table);

        page.Verify();

        page.Report.ShouldContain("會拒絕");
    }

    [Fact]
    public void SaysNothingWhenTheOperatorChangesTheirMind()
    {
        var page = Page();
        _prompts.File = null;

        page.Verify();

        page.Report.ShouldBeEmpty();
    }
}
