using Login38.Core.Configuration;
using Login38.Core.Servers;
using Login38.Core.Text;
using Shouldly;

namespace Login38.Core.Tests.Servers;

public sealed class ListFileCodecTests
{
    [Fact]
    public void ServerRoundTripsThroughTheEncryptedListFormat()
    {
        var original = new ServerInfo("測試伺服器", "192.168.1.50", 2000)
        {
            Key = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            Encrypt = true,
            UseHelper = true,
            UseMorphFile = true,
            MorphFileName = "morph.pak",
            RandomKey = true,
            RsaE = 628_624_543,
            RsaD = 1_424_206_015,
            RsaN = 2_001_201_551,
        };

        var parsed = ListFileCodec.ParseServers(ListFileCodec.BuildServerList([original])).ShouldHaveSingleItem();

        parsed.Name.ShouldBe(original.Name);
        parsed.IpAddress.ShouldBe(original.IpAddress);
        parsed.Port.ShouldBe(original.Port);
        parsed.InUse.ShouldBeTrue();
        parsed.Key.ShouldBe(original.Key);
        parsed.Encrypt.ShouldBeTrue();
        parsed.UseHelper.ShouldBeTrue();
        parsed.UseMorphFile.ShouldBeTrue();
        parsed.MorphFileName.ShouldBe(original.MorphFileName);
        parsed.RandomKey.ShouldBeTrue();
        parsed.RsaE.ShouldBe(original.RsaE);
        parsed.RsaD.ShouldBe(original.RsaD);
        parsed.RsaN.ShouldBe(original.RsaN);
    }

    [Fact]
    public void SerializedServerIsExactlyTheStructureSize() =>
        new ServerInfo("name", "127.0.0.1", 2000).ToBytes().Length.ShouldBe(ServerInfo.SerializedSize);

    // The reserved fix[16] tail must stay zeroed; the client reads the whole structure.
    [Fact]
    public void SerializedServerLeavesTheReservedTailZeroed() =>
        new ServerInfo("name", "127.0.0.1", 2000).ToBytes()[0xC5..].ShouldAllBe(b => b == 0);

    [Fact]
    public void OverlongNameIsTruncatedWithRoomForTheTerminator()
    {
        var name = new string('字', 64);

        var parsed = ServerInfo.Parse(new ServerInfo(name, "127.0.0.1", 2000).ToBytes());

        parsed.Name.Length.ShouldBe(31);
    }

    [Fact]
    public void RejectsMoreServersThanTheFormatAllows()
    {
        var servers = Enumerable.Range(0, ListFile.MaxServers + 1)
            .Select(i => new ServerInfo($"s{i}", "127.0.0.1", 2000))
            .ToList();

        Should.Throw<ArgumentOutOfRangeException>(() => ListFileCodec.BuildServerList(servers));
    }

    [Fact]
    public void ConfigRoundTripsEveryField()
    {
        var original = new ListFile
        {
            Aux = new AuxConfig
            {
                PacketEncrypt = true,
                AntiCheatBasic = true,
                TransformFile = true,
                MultiInstance = true,
                MultiInstanceLimit = 4,
                MovePacketNoEncrypt = true,
                LhxAuxEnabled = false,
                HpMpLimitEnabled = false,
                AcMrLimitEnabled = false,
                InventoryLimitEnabled = false,
                EquipUiEnabled = false,
                ImgLimitEnabled = false,
                DynamicDialogEnabled = false,
                DynamicIconEnabled = true,
                PickupToastEnabled = false,
                ExpDriftEnabled = false,
                OwnerCompanionPassThrough = false,
                OtherCompanionPassThrough = true,
                InventoryLimitValue = 500,
                ImgLimitValue = 123_456,
                DynamicIconPakName = "999",
                TransformFileName = "halloween",
                TextEncoding = TextEncodingMode.Gbk,
            },
            Launcher = new LauncherConfig
            {
                ActiveSkin = "custom",
                AnnouncementEnabled = false,
                AnnouncementUrl = "https://example.test/news?a=1&b=2",
                ListUpdateEnabled = true,
                ListUpdateUrl = "https://example.test/list.txt",
                AutoUpdateEnabled = true,
                AutoUpdateUrl = "https://example.test/update.ini",
                OfficialUrl = "https://example.test/",
                CustomerServiceUrl = "https://example.test/help",
            },
        };

        var parsed = ListFileCodec.Parse(ListFileCodec.BuildConfig(original));

        parsed.Aux.ShouldBeEquivalentTo(original.Aux);
        parsed.Launcher.ShouldBeEquivalentTo(original.Launcher);
    }

    // A URL query string contains '='; splitting on the last one would corrupt it.
    [Fact]
    public void ValueContainingEqualsSurvivesParsing()
    {
        const string url = "https://example.test/news?a=1&b=2";

        ListFileCodec.Parse($"[launcher]\nannouncement_url={url}\n").Launcher.AnnouncementUrl.ShouldBe(url);
    }

    [Fact]
    public void UnimplementedAdvancedAntiCheatIsForcedOff() =>
        ListFileCodec.Parse("[aux]\nanti_cheat_advanced=true\n").Aux.AntiCheatAdvanced.ShouldBeFalse();

    [Theory]
    [InlineData("0", 1u)]      // below the floor
    [InlineData("9999", 999u)] // above the ceiling
    [InlineData("500", 500u)]  // in range
    public void OutOfRangeLimitsAreClamped(string value, uint expected) =>
        ListFileCodec.Parse($"[aux]\ninventory_limit_value={value}\n")
            .Aux.InventoryLimitValue.ShouldBe(expected);

    // Garbage falls back to the default rather than to the nearest bound: a typo should
    // not silently become a valid extreme.
    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData("-5")]
    public void UnparseableLimitsFallBackToTheDefault(string value) =>
        ListFileCodec.Parse($"[aux]\ninventory_limit_value={value}\n")
            .Aux.InventoryLimitValue.ShouldBe(AuxConfig.InventoryLimitBounds.Default);

    [Fact]
    public void OlderConfigGetsSafeCompanionCollisionDefaults()
    {
        var aux = ListFileCodec.Parse("[aux]\npacket_encrypt=true\n").Aux;

        aux.OwnerCompanionPassThrough.ShouldBeTrue();
        aux.OtherCompanionPassThrough.ShouldBeFalse();
    }

    [Fact]
    public void EmptyIconPakNameKeepsTheDefault() =>
        ListFileCodec.Parse("[aux]\ndynamic_icon_pak_name=\n")
            .Aux.DynamicIconPakName.ShouldBe(new AuxConfig().DynamicIconPakName);

    // Unlike the icon pak, empty here says something: the table named after the client.
    // An operator who clears the box has to get that rather than keep whatever was there.
    [Fact]
    public void EmptyMorphTableNameMeansTheClientsOwn() =>
        ListFileCodec.Parse("[aux]\ntransform_file_name=\n")
            .Aux.TransformFileName.ShouldBeEmpty();

    // A list written before this setting existed reads as "the client's own", which is
    // where the launcher has always looked.
    [Fact]
    public void AnOlderListNamesNoMorphTable() =>
        ListFileCodec.Parse("[aux]\ntransform_file=1\n").Aux.TransformFileName.ShouldBeEmpty();

    [Fact]
    public void MalformedInputIsSkippedRatherThanRejected()
    {
        const string content = """
            ; a comment
            # another comment
            a line with no separator

            [unknown-section]
            whatever=1

            [aux]
            packet_encrypt=true
            totally_unknown_key=value
            """;

        var parsed = ListFileCodec.Parse(content);

        parsed.Aux.PacketEncrypt.ShouldBeTrue();
    }

    [Fact]
    public void UndecodableServerEntryIsSkipped()
    {
        var content = "[list]\nServerData0=not-base64!!\nServerData1=" +
                      ListFileCodec.BuildServerList([new ServerInfo("ok", "127.0.0.1", 2000)])
                          .Split('=', 2)[1].Trim();

        ListFileCodec.ParseServers(content).ShouldHaveSingleItem().Name.ShouldBe("ok");
    }

    [Fact]
    public void EncryptedConfigTextRoundTrips()
    {
        const string plaintext = "[aux]\npacket_encrypt=true\n";

        var encrypted = ConfigCipher.EncryptText(plaintext);

        encrypted.ShouldStartWith(ConfigCipher.EncryptedTextPrefix);
        ConfigCipher.DecryptText(encrypted).ShouldBe(plaintext);
    }

    // Config files written before encryption was introduced must still load.
    [Fact]
    public void PlainIniPassesThroughDecryption()
    {
        const string plaintext = "[aux]\npacket_encrypt=true\n";

        ConfigCipher.DecryptText(plaintext).ShouldBe(plaintext);
    }

    [Fact]
    public void UnrecognisableConfigTextIsRejected() =>
        Should.Throw<FormatException>(() => ConfigCipher.DecryptText("garbage"));

    [Fact]
    public void CipherLeavesATrailingPartialBlockXorOnly()
    {
        // 20 bytes: one full AES block plus a 4-byte tail.
        var data = new byte[20];
        Random.Shared.NextBytes(data);
        var original = data.ToArray();

        ConfigCipher.Encrypt(ConfigCipher.FileKey, data);
        ConfigCipher.Decrypt(ConfigCipher.FileKey, data);

        data.ShouldBe(original);
    }
}
