using System.Text;
using Login38.Core.Configuration;
using Login38.Core.Cryptography;
using Login38.Core.Servers;
using Login38.Core.Text;
using Login38.Encoder.Services;
using Shouldly;

namespace Login38.Encoder.Tests.Services;

/// <summary>
/// Covers the two files the encoder writes.
/// </summary>
/// <remarks>
/// These are what an operator ships. Getting them wrong does not show up here — it shows
/// up as a player who cannot see a server, so the round trip is pinned rather than trusted.
/// </remarks>
public sealed class EncoderFilesTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-encoder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private EncoderFiles Files() => new(LegacyTextCodec.Auto, _directory);

    private static ServerInfo Server(string name, string address = "1.2.3.4", int port = 7001) =>
        new() { Name = name, IpAddress = address, Port = port, InUse = true };

    [Fact]
    public void ReadsBackWhatItWrote()
    {
        var files = Files();

        files.Save(new ListFile
        {
            Servers = [Server("第一台", "10.0.0.1", 2000), Server("第二台")],
            Aux = new AuxConfig { PacketEncrypt = true, InventoryLimitValue = 300 },
            Launcher = new LauncherConfig { OfficialUrl = "https://example.invalid/" },
        });

        var loaded = files.Load();

        loaded.Problems.ShouldBeEmpty();
        loaded.File.Servers.Select(s => s.Name).ShouldBe(["第一台", "第二台"]);
        loaded.File.Servers[0].IpAddress.ShouldBe("10.0.0.1");
        loaded.File.Servers[0].Port.ShouldBe(2000);
        loaded.File.Aux.PacketEncrypt.ShouldBeTrue();
        loaded.File.Aux.InventoryLimitValue.ShouldBe(300u);
        loaded.File.Launcher.OfficialUrl.ShouldBe("https://example.invalid/");
    }

    [Fact]
    public void WritesBothFiles()
    {
        Files().Save(new ListFile { Servers = [Server("A")] });

        File.Exists(Path.Combine(_directory, EncoderFiles.ServerListName)).ShouldBeTrue();
        File.Exists(Path.Combine(_directory, EncoderFiles.ConfigName)).ShouldBeTrue();
    }

    // The client reads list.txt too, and it does not know what a byte order mark is: three
    // bytes in front of `[list]` and it finds no section at all.
    [Fact]
    public void WritesTheServerListWithNoByteOrderMark()
    {
        Files().Save(new ListFile { Servers = [Server("A")] });

        File.ReadAllBytes(Path.Combine(_directory, EncoderFiles.ServerListName))
            .Take(3).ShouldNotBe([(byte)0xEF, 0xBB, 0xBF]);
    }

    [Fact]
    public void EncryptsTheSettingsFile()
    {
        Files().Save(new ListFile { Servers = [Server("A")], Launcher = new LauncherConfig { OfficialUrl = "https://secret.invalid/" } });

        var raw = File.ReadAllText(Path.Combine(_directory, EncoderFiles.ConfigName));

        raw.ShouldStartWith(ConfigCipher.EncryptedTextPrefix);
        raw.ShouldNotContain("secret.invalid");
    }

    // A slot the operator left blank is not a server; the format says so by the name.
    [Fact]
    public void LeavesOutTheSlotsWithNoName()
    {
        Files().Save(new ListFile { Servers = [Server("A"), new ServerInfo(), Server("B")] });

        Files().Load().File.Servers.Select(s => s.Name).ShouldBe(["A", "B"]);
    }

    [Fact]
    public void StartsFromDefaultsWhenThereIsNothingToRead()
    {
        var loaded = Files().Load();

        loaded.Problems.ShouldBeEmpty();
        loaded.File.Servers.ShouldBeEmpty();
        loaded.File.Launcher.ActiveSkin.ShouldBe("default");
    }

    // An unreadable file is a reason to say so, not a reason to refuse to start — the next
    // thing the operator does is write a good one over it.
    [Fact]
    public void SaysWhatItCouldNotReadAndCarriesOn()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, EncoderFiles.ConfigName), "ENC1:not base64 at all");

        var loaded = Files().Load();

        loaded.Problems.ShouldNotBeEmpty();
        loaded.Problems[0].ShouldContain(EncoderFiles.ConfigName);
        loaded.File.ShouldNotBeNull();
    }

    // An older installation kept the servers in the settings file as well.
    [Fact]
    public void TakesTheServersOutOfTheSettingsWhenThereIsNoListOfItsOwn()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, EncoderFiles.ConfigName),
            ConfigCipher.EncryptText(
                ListFileCodec.BuildServerList([Server("舊的")]) + ListFileCodec.BuildConfig(new ListFile())));

        Files().Load().File.Servers.Select(s => s.Name).ShouldBe(["舊的"]);
    }

    [Fact]
    public void PrefersTheServerListOverTheOlderCopyInTheSettings()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, EncoderFiles.ServerListName),
            ListFileCodec.BuildServerList([Server("新的")]));
        File.WriteAllText(
            Path.Combine(_directory, EncoderFiles.ConfigName),
            ConfigCipher.EncryptText(
                ListFileCodec.BuildServerList([Server("舊的")]) + ListFileCodec.BuildConfig(new ListFile())));

        Files().Load().File.Servers.Select(s => s.Name).ShouldBe(["新的"]);
    }

    // Operators hand-edit these and hand them around. The reference read them as UTF-8 and
    // treated anything else as unreadable.
    [Fact]
    public void ReadsAServerListSavedInTheClientsOwnCodePage()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(_directory);

        File.WriteAllBytes(
            Path.Combine(_directory, EncoderFiles.ServerListName),
            Encoding.GetEncoding(950).GetBytes(ListFileCodec.BuildServerList([Server("測試台")])));

        Files().Load().File.Servers.Select(s => s.Name).ShouldBe(["測試台"]);
    }

    [Fact]
    public void WritesTheKeyFileTheServerReads()
    {
        var path = Files().WritePackProperties(new Rsa32Key(17, 2753, 3233));
        var content = File.ReadAllText(path);

        content.ShouldContain("RSA_KEY_E=17");
        content.ShouldContain("RSA_KEY_D=2753");
        content.ShouldContain("RSA_KEY_N=3233");
        Path.GetFileName(path).ShouldBe(EncoderFiles.PackPropertiesName);
    }

    [Fact]
    public void RefusesMoreSlotsThanTheFormatHolds() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Files().Save(new ListFile
        {
            Servers = [.. Enumerable.Range(0, ListFile.MaxServers + 1).Select(i => Server("S" + i))],
        }));
}
