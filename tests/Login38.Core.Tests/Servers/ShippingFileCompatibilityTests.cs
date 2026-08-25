using Login38.Core.Configuration;
using Login38.Tests;
using Login38.Core.Servers;
using Shouldly;

namespace Login38.Core.Tests.Servers;

/// <summary>
/// Reads the real files from a shipping install.
/// </summary>
/// <remarks>
/// The round-trip tests only prove this implementation agrees with itself. These prove
/// it agrees with the Rust build whose output is already on players' machines — if the
/// XOR table, the key, the AES block rule or the 213-byte layout drifted by one byte,
/// these fail and the round-trip tests do not.
/// </remarks>
public sealed class ShippingFileCompatibilityTests
{
    private static string TestData(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [ShippingPackageFact]
    public void ParsesTheShippingServerList()
    {
        var servers = ListFileCodec.ParseServers(File.ReadAllText(TestData("list.txt")));

        servers.ShouldNotBeEmpty();

        foreach (var server in servers)
        {
            // Decrypting with the wrong table yields noise, which shows up here first:
            // a plausible port number and a non-empty printable name.
            server.Port.ShouldBeInRange(1, 65535);
            server.Name.ShouldNotBeNullOrWhiteSpace();
            server.IpAddress.ShouldNotBeNullOrWhiteSpace();
            server.Key.Length.ShouldBe(ServerInfo.KeyLength);
        }
    }

    [ShippingPackageFact]
    public void ReEncryptingTheShippingServerListReproducesItByteForByte()
    {
        var original = File.ReadAllText(TestData("list.txt"));

        var rebuilt = ListFileCodec.BuildServerList(ListFileCodec.ParseServers(original));

        Normalize(rebuilt).ShouldBe(Normalize(original));
    }

    [ShippingPackageFact]
    public void DecryptsTheShippingConfig()
    {
        var encrypted = File.ReadAllText(TestData("config.ini"));

        var plaintext = ConfigCipher.DecryptText(encrypted);

        plaintext.TrimStart().ShouldStartWith("[");
        plaintext.ShouldContain("[aux]");
    }

    [ShippingPackageFact]
    public void ParsesTheShippingConfigIntoKnownKeys()
    {
        var plaintext = ConfigCipher.DecryptText(File.ReadAllText(TestData("config.ini")));

        var parsed = ListFileCodec.Parse(plaintext);

        parsed.Aux.InventoryLimitValue.ShouldBeInRange(
            AuxConfig.InventoryLimitBounds.Min, AuxConfig.InventoryLimitBounds.Max);
        parsed.Aux.ImgLimitValue.ShouldBeInRange(
            AuxConfig.ImgLimitBounds.Min, AuxConfig.ImgLimitBounds.Max);
        parsed.Launcher.ActiveSkin.ShouldNotBeNullOrWhiteSpace();
    }

    [ShippingPackageFact]
    public void ShippingConfigSurvivesADecryptEditReEncryptCycle()
    {
        var plaintext = ConfigCipher.DecryptText(File.ReadAllText(TestData("config.ini")));
        var parsed = ListFileCodec.Parse(plaintext);

        var reparsed = ListFileCodec.Parse(
            ConfigCipher.DecryptText(ConfigCipher.EncryptText(ListFileCodec.BuildConfig(parsed))));

        reparsed.Aux.ShouldBeEquivalentTo(parsed.Aux);
        reparsed.Launcher.ShouldBeEquivalentTo(parsed.Launcher);
    }

    /// <summary>Line endings are not part of the format; the base64 payload is.</summary>
    private static string Normalize(string content) =>
        content.ReplaceLineEndings("\n").Trim();
}
