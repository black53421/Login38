using Login38.Core.Servers;
using Login38.Core.Text;
using Login38.Encoder.Services;
using Login38.Encoder.ViewModels;
using Shouldly;

namespace Login38.Encoder.Tests.ViewModels;

/// <summary>
/// Covers what the launcher is told to do to the client.
/// </summary>
/// <remarks>
/// Every switch here is a patch the launcher applies or does not, so a value that comes
/// back different from the one that went in is a feature an operator switched on and did
/// not get.
/// </remarks>
public sealed class AuxViewModelTests
{
    [Fact]
    public void CarriesEverySwitchThroughARoundTrip()
    {
        var aux = new AuxConfig
        {
            PacketEncrypt = true,
            AntiCheatBasic = true,
            TransformFile = true,
            MovePacketNoEncrypt = true,
            LhxAuxEnabled = false,
            HpMpLimitEnabled = false,
            AcMrLimitEnabled = false,
            InventoryLimitEnabled = false,
            InventoryLimitValue = 300,
            EquipUiEnabled = false,
            ImgLimitEnabled = false,
            ImgLimitValue = 120_000,
            DynamicDialogEnabled = false,
            DynamicIconEnabled = true,
            DynamicIconPakName = "555",
            TransformFileName = "halloween",
            PickupToastEnabled = false,
            ExpDriftEnabled = false,
            OwnerCompanionPassThrough = false,
            OtherCompanionPassThrough = true,
            RangeSkillDamageExtension = true,
            PacketSpyStartupEnabled = false,
            InternalBotEnabled = true,
            MultiInstance = true,
            MultiInstanceLimit = 4,
            TextEncoding = TextEncodingMode.Gbk,
        };

        var model = new AuxViewModel();
        model.Load(aux);

        var back = model.ToConfig();

        back.PacketEncrypt.ShouldBeTrue();
        back.AntiCheatBasic.ShouldBeTrue();
        back.TransformFile.ShouldBeTrue();
        back.MovePacketNoEncrypt.ShouldBeTrue();
        back.LhxAuxEnabled.ShouldBeFalse();
        back.HpMpLimitEnabled.ShouldBeFalse();
        back.AcMrLimitEnabled.ShouldBeFalse();
        back.InventoryLimitEnabled.ShouldBeFalse();
        back.InventoryLimitValue.ShouldBe(300u);
        back.EquipUiEnabled.ShouldBeFalse();
        back.ImgLimitEnabled.ShouldBeFalse();
        back.ImgLimitValue.ShouldBe(120_000u);
        back.DynamicDialogEnabled.ShouldBeFalse();
        back.DynamicIconEnabled.ShouldBeTrue();
        back.DynamicIconPakName.ShouldBe("555");
        back.TransformFileName.ShouldBe("halloween");
        back.PickupToastEnabled.ShouldBeFalse();
        back.ExpDriftEnabled.ShouldBeFalse();
        back.OwnerCompanionPassThrough.ShouldBeFalse();
        back.OtherCompanionPassThrough.ShouldBeTrue();
        back.RangeSkillDamageExtension.ShouldBeTrue();
        back.PacketSpyStartupEnabled.ShouldBeFalse();
        back.InternalBotEnabled.ShouldBeTrue();
        back.MultiInstance.ShouldBeTrue();
        back.MultiInstanceLimit.ShouldBe(4u);
        back.TextEncoding.ShouldBe(TextEncodingMode.Gbk);
    }

    // The reference offered two code pages and mapped guessing onto one of them, so an
    // operator who had chosen it lost that choice the next time anything was saved.
    [Theory]
    [InlineData(TextEncodingMode.Auto)]
    [InlineData(TextEncodingMode.Big5)]
    [InlineData(TextEncodingMode.Gbk)]
    public void KeepsWhicheverWayOfReadingTextWasChosen(TextEncodingMode mode)
    {
        var model = new AuxViewModel();
        model.Load(new AuxConfig { TextEncoding = mode });

        model.Encodings.ShouldContain(mode);
        model.ToConfig().TextEncoding.ShouldBe(mode);
    }

    // The reference read these back with a parse that fell to the default when it failed,
    // so an inventory limit typed as `25o` silently became 255.
    [Theory]
    [InlineData(0, 1u)]
    [InlineData(300, 300u)]
    [InlineData(5000, 999u)]
    public void BoundsTheBagsSize(double given, uint expected)
    {
        var model = new AuxViewModel { InventorySize = given };

        model.ToConfig().InventoryLimitValue.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 6295u)]
    [InlineData(50_000, 50_000u)]
    [InlineData(9_000_000, 500_000u)]
    public void BoundsTheImageLimit(double given, uint expected)
    {
        var model = new AuxViewModel { ImageSize = given };

        model.ToConfig().ImgLimitValue.ShouldBe(expected);
    }

    [Theory]
    [InlineData(-5, 0u)]
    [InlineData(4, 4u)]
    [InlineData(999, 32u)]
    public void BoundsHowManyCopiesMayRun(double given, uint expected)
    {
        var model = new AuxViewModel { CopyLimit = given };

        model.ToConfig().MultiInstanceLimit.ShouldBe(expected);
    }

    // The launcher opens this by name, and an empty one would have it look for `.pak`.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NeverWritesAnEmptyPackageName(string given)
    {
        var model = new AuxViewModel { IconPackageName = given };

        model.ToConfig().DynamicIconPakName.ShouldBe("123");
    }

    [Fact]
    public void TrimsThePackageName()
    {
        var model = new AuxViewModel { IconPackageName = "  555  " };

        model.ToConfig().DynamicIconPakName.ShouldBe("555");
    }

    // It is in the file format and the launcher clears it on both load and save, so a
    // switch for it would be a switch that does nothing — which is what the reference had.
    [Fact]
    public void StartupPacketSpyDisablesRangeDamageProtocol()
    {
        var model = new AuxViewModel { RangeSkillDamageExtension = true };

        model.PacketSpyStartup = true;

        model.PacketSpyStartup.ShouldBeTrue();
        model.RangeSkillDamageExtension.ShouldBeFalse();
    }

    [Fact]
    public void RangeDamageProtocolDisablesStartupPacketSpy()
    {
        var model = new AuxViewModel { PacketSpyStartup = true };

        model.RangeSkillDamageExtension = true;

        model.RangeSkillDamageExtension.ShouldBeTrue();
        model.PacketSpyStartup.ShouldBeFalse();
    }

    [Fact]
    public void NeverTurnsOnTheProtectionTheLauncherDoesNotImplement() =>
        new AuxViewModel().ToConfig().AntiCheatAdvanced.ShouldBeFalse();

    // The reference drew a dropdown of the packed tables and saved the choice nowhere, so
    // an operator with two of them always got the one named after the client.
    [Fact]
    public void OffersThePackedTablesBesideTheEncoder()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"aux-morph-{Guid.NewGuid():N}")).FullName;

        try
        {
            foreach (var name in new[] { "TW13081901", "halloween" })
            {
                MorphTools.Encode(
                    Write(directory, name + ".txt", "1234\tcloak\n"),
                    Path.Combine(directory, name + ".pak"));
            }

            var aux = new AuxViewModel();

            aux.RescanMorphTables(directory);

            aux.MorphTables.ShouldBe(["halloween", "TW13081901"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A table an operator names may not be one they built here, and clearing the box
    // would quietly move the launcher back to the client-named one.
    [Fact]
    public void KeepsANamedTableThatIsNotInTheList()
    {
        var aux = new AuxViewModel { MorphTableName = "somewhere-else" };

        aux.RescanMorphTables(Path.Combine(Path.GetTempPath(), $"gone-{Guid.NewGuid():N}"));

        aux.MorphTables.ShouldBeEmpty();
        aux.MorphTableName.ShouldBe("somewhere-else");
    }

    [Fact]
    public void TrimsTheNameOnTheWayOut() =>
        new AuxViewModel { MorphTableName = "  halloween  " }
            .ToConfig().TransformFileName.ShouldBe("halloween");

    private static string Write(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);

        File.WriteAllText(path, content);

        return path;
    }

    // A directory with a packed table in it and a list with nothing in it looks, to an
    // operator, exactly like a tool that cannot see the file. It has to say which it is.
    [Fact]
    public void SaysSoWhenNothingHasBeenPackedYet()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"aux-empty-{Guid.NewGuid():N}")).FullName;

        try
        {
            var aux = new AuxViewModel();

            aux.RescanMorphTables(directory);

            aux.MorphTables.ShouldBeEmpty();
            aux.MorphTablesNoted.ShouldBeTrue();
            aux.MorphTablesNote.ShouldContain("還沒有");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Hiding a .pak that is plainly in the directory is the same thing, to whoever put it
    // there, as not looking at all.
    [Fact]
    public void NamesThePackagesItWillNotOffer()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"aux-foreign-{Guid.NewGuid():N}")).FullName;

        try
        {
            MorphTools.Encode(
                Write(directory, "ours.txt", "1234\tcloak\n"),
                Path.Combine(directory, "ours.pak"));

            File.WriteAllBytes(Path.Combine(directory, "somebody-elses.pak"), [1, 2, 3, 4]);

            var aux = new AuxViewModel();

            aux.RescanMorphTables(directory);

            aux.MorphTables.ShouldBe(["ours"]);
            aux.MorphTablesNoted.ShouldBeTrue();
            aux.MorphTablesNote.ShouldContain("somebody-elses.pak");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaysNothingWhenEveryPackageIsOurs()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"aux-clean-{Guid.NewGuid():N}")).FullName;

        try
        {
            MorphTools.Encode(
                Write(directory, "ours.txt", "1234\tcloak\n"),
                Path.Combine(directory, "ours.pak"));

            var aux = new AuxViewModel();

            aux.RescanMorphTables(directory);

            aux.MorphTablesNoted.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
