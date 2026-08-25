using Login38.Core.Servers;
using Login38.Core.Text;
using Login38.Encoder.Services;
using Login38.Encoder.ViewModels;
using Shouldly;

namespace Login38.Encoder.Tests.ViewModels;

/// <summary>
/// Covers the operator's tool as a whole: what it shows, and what it writes.
/// </summary>
/// <remarks>
/// The reference could only be exercised by opening it and clicking, which is why its
/// slot handling was wrong for as long as it was. Here the same paths are ordinary calls.
/// </remarks>
public sealed class EncoderViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-encoder-vm-" + Guid.NewGuid().ToString("N"));

    private readonly StubPrompts _prompts = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private EncoderFiles Files() => new(LegacyTextCodec.Auto, _directory);

    private EncoderViewModel Tool() => new(Files(), LegacyTextCodec.Auto, _prompts);

    [Fact]
    public void OffersEverySlotTheFormatHolds() =>
        Tool().Servers.Count.ShouldBe(ListFile.MaxServers);

    [Fact]
    public void StartsWithOneSlotFilledIn()
    {
        var tool = Tool();

        tool.Servers[0].IsUsed.ShouldBeTrue();
        tool.Servers.Skip(1).ShouldAllBe(s => !s.IsUsed);
    }

    [Fact]
    public void WritesWhatWasTypedIn()
    {
        var tool = Tool();

        tool.Servers[0].Name = "第一台";
        tool.Servers[0].Address = "10.0.0.1";
        tool.Servers[0].Port = 2000;
        tool.Aux.PacketEncrypt = true;
        tool.Launcher.OfficialUrl = "https://example.invalid/";
        tool.Save();

        var written = Files().Load().File;

        written.Servers.Select(s => s.Name).ShouldBe(["第一台"]);
        written.Servers[0].IpAddress.ShouldBe("10.0.0.1");
        written.Servers[0].Port.ShouldBe(2000);
        written.Aux.PacketEncrypt.ShouldBeTrue();
        written.Launcher.OfficialUrl.ShouldBe("https://example.invalid/");
    }

    [Fact]
    public void ShowsWhatWasWrittenWhenItIsReadAgain()
    {
        var first = Tool();
        first.Servers[0].Name = "第一台";
        first.Servers[2].Name = "第三台";
        first.Save();

        var second = Tool();

        second.Servers[0].Name.ShouldBe("第一台");
        second.Servers[1].Name.ShouldBe("第三台");
    }

    // The reference wrote whichever slot was selected, merged it into the file on disk by
    // position, and then dropped the unnamed slots on the way out — so a gap moved
    // everything after it up one and the slot the operator edited was not the slot they
    // got. Every slot goes out at once here, so what is on screen is what is written.
    [Fact]
    public void WritesEverySlotAtOnce()
    {
        var tool = Tool();

        tool.Servers[0].Name = "A";
        tool.Servers[1].Name = "B";
        tool.Servers[2].Name = "C";
        tool.Save();

        Files().Load().File.Servers.Select(s => s.Name).ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void LeavesOutTheSlotsWithNoName()
    {
        var tool = Tool();

        tool.Servers[0].Name = "A";
        tool.Servers[3].Name = "D";
        tool.Save();

        Files().Load().File.Servers.Select(s => s.Name).ShouldBe(["A", "D"]);
    }

    [Fact]
    public void WritesNothingWhenNoSlotHasAName()
    {
        var tool = Tool();

        foreach (var slot in tool.Servers)
        {
            slot.Clear();
        }

        tool.Save();

        tool.Report.ShouldContain("沒有任何槽位填了名稱");
        File.Exists(Path.Combine(_directory, EncoderFiles.ServerListName)).ShouldBeFalse();
    }

    [Fact]
    public void ThrowsAwayUnsavedChangesWhenReloaded()
    {
        var tool = Tool();
        tool.Servers[0].Name = "第一台";
        tool.Save();

        tool.Servers[0].Name = "改壞了";
        tool.Reload();

        tool.Servers[0].Name.ShouldBe("第一台");
    }

    [Fact]
    public void EmptiesTheSlotsThatAreNotInTheFile()
    {
        var tool = Tool();
        tool.Servers[0].Name = "只有一台";
        tool.Servers[1].Name = "還有一台";
        tool.Save();

        tool.Servers[1].Name = string.Empty;
        tool.Save();
        tool.Reload();

        tool.Servers[1].IsUsed.ShouldBeFalse();
    }

    // One key for the whole list. The format carries a copy in each slot and the client
    // reads it out of the row the player picked, so all of them have to agree.
    [Fact]
    public void MakesOneKeyForTheWholeList()
    {
        var tool = Tool();

        tool.GenerateKey();

        tool.RsaE.ShouldNotBeEmpty();
        tool.RsaD.ShouldNotBeEmpty();
        tool.RsaN.ShouldNotBeEmpty();
    }

    // The reference put the key in whichever slot was selected, so an operator with more
    // than one server had to remember to do it again for each, and a slot they forgot
    // bound a client to nothing.
    [Fact]
    public void StampsThatOneKeyOnEveryServerItWrites()
    {
        var tool = Tool();
        tool.Servers[0].Name = "第一台";
        tool.Servers[1].Name = "第二台";
        tool.Servers[3].Name = "第四台";

        tool.GenerateKey();
        tool.Save();

        var written = Files().Load().File.Servers;

        written.Count.ShouldBe(3);
        written.Select(s => s.RsaE).Distinct().Count().ShouldBe(1);
        written.Select(s => s.RsaD).Distinct().Count().ShouldBe(1);
        written.Select(s => s.RsaN).Distinct().Count().ShouldBe(1);
        written[0].RsaN.ShouldNotBe(0u);
    }

    [Fact]
    public void GivesTheServerItsHalfOfTheKey()
    {
        var tool = Tool();
        tool.GenerateKey();

        File.Exists(Path.Combine(_directory, EncoderFiles.PackPropertiesName)).ShouldBeTrue();
    }

    [Fact]
    public void KeepsTheKeyThroughASaveAndAReload()
    {
        var tool = Tool();
        tool.Servers[0].Name = "第一台";
        tool.GenerateKey();

        var made = tool.RsaN;

        tool.Save();
        tool.Reload();

        tool.RsaN.ShouldBe(made);
    }

    // The reference showed these but never read them back, so an operator restoring a key
    // from a backup could type it in and watch it be ignored.
    [Fact]
    public void KeepsAKeyThatWasTypedInByHand()
    {
        var tool = Tool();
        tool.Servers[0].Name = "第一台";
        tool.RsaE = "17";
        tool.RsaD = "2753";
        tool.RsaN = "3233";
        tool.Save();

        var written = Files().Load().File.Servers[0];

        written.RsaE.ShouldBe(17u);
        written.RsaD.ShouldBe(2753u);
        written.RsaN.ShouldBe(3233u);
    }

    // The key and the setting that turns it on are edited on different pages, and neither
    // means anything without the other. The launcher refuses a keyless server outright
    // once encryption is on, so an operator has to be told here rather than by a player.
    [Fact]
    public void SaysNothingWhenTheKeyAndTheSettingAgree()
    {
        var tool = Tool();

        tool.Aux.PacketEncrypt = false;

        tool.KeyWarned.ShouldBeFalse();

        tool.Aux.PacketEncrypt = true;
        tool.GenerateKey();

        tool.KeyWarned.ShouldBeFalse();
    }

    [Fact]
    public void SaysSoWhenThereIsAKeyThatNothingWillUse()
    {
        var tool = Tool();

        tool.Aux.PacketEncrypt = false;
        tool.GenerateKey();

        tool.KeyWarned.ShouldBeTrue();
        tool.KeyWarning.ShouldContain("封包加密");
    }

    [Fact]
    public void SaysSoWhenEncryptionIsOnAndThereIsNoKey()
    {
        var tool = Tool();

        tool.Aux.PacketEncrypt = true;

        tool.KeyWarned.ShouldBeTrue();
        tool.KeyWarning.ShouldContain("沒有金鑰");
    }

    // E is the client's own half and is not what the relay encrypts with, so a list
    // carrying only that one is a list with no key at all.
    [Fact]
    public void CountsAKeyByTheTwoHalvesThatDoTheWork()
    {
        var tool = Tool();

        tool.RsaE = "17";

        tool.HasKey.ShouldBeFalse();

        tool.RsaD = "2753";
        tool.RsaN = "3233";

        tool.HasKey.ShouldBeTrue();
    }

    [Fact]
    public void RepeatsTheWarningOnTheWayOut()
    {
        var tool = Tool();
        tool.Servers[0].Name = "第一台";
        tool.Aux.PacketEncrypt = true;

        tool.Save();

        tool.Report.ShouldContain("沒有金鑰");
    }

    // An older file the reference wrote may carry a key in one slot only.
    [Fact]
    public void PicksUpAKeyLeftInASingleSlotByAnOlderFile()
    {
        var files = Files();

        files.Save(new ListFile
        {
            Servers =
            [
                new ServerInfo { Name = "第一台", IpAddress = "127.0.0.1", Port = 7001, InUse = true },
                new ServerInfo
                {
                    Name = "第二台",
                    IpAddress = "127.0.0.1",
                    Port = 7002,
                    InUse = true,
                    RsaE = 17,
                    RsaD = 2753,
                    RsaN = 3233,
                },
            ],
        });

        var tool = Tool();

        tool.RsaE.ShouldBe("17");
        tool.RsaD.ShouldBe("2753");
        tool.RsaN.ShouldBe("3233");
    }

    // Zero is how the format says there is no key, and an empty box has to say the same
    // or an operator who clears the fields writes 0/0/0 and cannot tell why encryption
    // stopped working.
    [Fact]
    public void LeavesTheBoxesEmptyWhenTheListCarriesNoKey()
    {
        var tool = Tool();

        tool.RsaE.ShouldBeEmpty();
        tool.RsaD.ShouldBeEmpty();
        tool.RsaN.ShouldBeEmpty();
    }

    // What it wrote, not where. The operator is standing in the folder — the tool reads
    // and writes beside itself — so the path is not news, and it was on screen from the
    // moment the window opened whether anybody had done anything or not.
    [Fact]
    public void SaysWhatItWroteWithoutNamingTheFolder()
    {
        var tool = Tool();
        tool.Servers[0].Name = "A";
        tool.Save();

        tool.Report.ShouldContain(EncoderFiles.ServerListName);
        tool.Report.ShouldContain(EncoderFiles.ConfigName);
        tool.Report.ShouldNotContain(_directory);
    }

    // Silence, rather than a line counting the rows that are already on screen.
    [Fact]
    public void SaysNothingAboutALoadThatWentCleanly()
    {
        Tool().Report.ShouldBeEmpty();
    }

    // The list was built when the window opened, so a table packed since was invisible and
    // an operator who had just made one found an empty box.
    [Fact]
    public void OffersATableAsSoonAsItIsPacked()
    {
        var tool = Tool();

        tool.Aux.MorphTables.ShouldBeEmpty();

        tool.Morph.Source = Write("halloween.txt", "1234\tcloak\n");
        tool.Morph.Encode();

        tool.Aux.MorphTables.ShouldContain("halloween");
    }

    // A table dropped into the directory by hand, by somebody who did not pack it here.
    [Fact]
    public void FindsATableThatAppearedAfterTheWindowOpened()
    {
        var tool = Tool();

        MorphTools.Encode(
            Write("halloween.txt", "1234\tcloak\n"),
            Path.Combine(_directory, "halloween.pak"));

        tool.Aux.MorphTables.ShouldBeEmpty();

        tool.RescanMorphTables();

        tool.Aux.MorphTables.ShouldContain("halloween");
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);

        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, content);

        return path;
    }
}
