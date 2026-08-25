using System.IO;
using Login38.App.ViewModels.Helper;
using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.App.Tests.ViewModels;

/// <summary>
/// Covers the helper's settings window: what it shows, and what it publishes.
/// </summary>
/// <remarks>
/// Everything the window does is one of two things — read a settings object into controls,
/// or build one back out of them — so both directions are exercised here without a window.
/// The reference could only be exercised by opening it.
/// </remarks>
public sealed class HelperViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-helper-vm-" + Guid.NewGuid().ToString("N"));

    private readonly AuxSettingsSource _source = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ItemCatalog Catalog() =>
        new(LegacyTextCodec.Auto, NullLogger<ItemCatalog>.Instance, _directory);

    private HelperViewModel Window(InventoryWatch? bag = null) =>
        new(_source, Catalog(), bag);

    private static InventoryItem Item(string name) =>
        new(default(GameAddress), 0, 0x33, 0, false, 1, name);

    [Fact]
    public void ShowsWhatWasAlreadyPublished()
    {
        var settings = new AuxSettings { Profile = "Sqw887979", HelperEnabled = true };
        settings.Potions[2].Enabled = true;
        settings.Potions[2].Threshold = 60;
        settings.Potions[2].Item = "強力治癒藥水";
        settings.Misc.MonsterLevelColour = true;
        _source.Publish(settings);

        using var window = Window();

        window.Profile.ShouldBe("Sqw887979");
        window.HelperEnabled.ShouldBeTrue();
        window.Potions[2].Enabled.ShouldBeTrue();
        window.Potions[2].Threshold.ShouldBe(60);
        window.Potions[2].Item.ShouldBe("強力治癒藥水");
        window.Misc.MonsterLevelColour.ShouldBeTrue();
    }

    [Fact]
    public void PublishesAsSoonAsAnythingIsChanged()
    {
        using var window = Window();

        window.Misc.AllDay = true;

        _source.Current.Misc.AllDay.ShouldBeTrue();
    }

    [Fact]
    public void PublishesWhenARowIsChanged()
    {
        using var window = Window();

        window.Timers[3].Command = "/say hello";
        window.TimersEnabled = true;

        _source.Current.TimersEnabled.ShouldBeTrue();
        _source.Current.TimerRows[3].Command.ShouldBe("/say hello");
    }

    [Fact]
    public void PublishesWhenSomethingIsAddedToAList()
    {
        using var window = Window();

        window.Delete.Candidate = "破爛的短劍";
        window.Delete.AddCommand.Execute(null);

        _source.Current.DeleteList.ShouldBe(["破爛的短劍"]);
    }

    [Fact]
    public void PublishesNothingWhileItIsBeingFilledIn()
    {
        var published = 0;
        _source.Published += (_, _) => published++;

        using var window = Window();

        published.ShouldBe(0);
    }

    // Every field of the settings goes out and comes back, so a field this window does not
    // yet show is a field it would otherwise quietly reset for the player.
    [Fact]
    public void CarriesEverySettingThroughARoundTrip()
    {
        var settings = new AuxSettings
        {
            Profile = "Sqw887979",
            PotionUsePercent = true,
            PotionShowInventory = true,
            HelperEnabled = true,
            ShowExperience = true,
            KeepWeaponSharp = true,
            EatWhenHungry = true,
            TransformEnabled = true,
            TransformItem = "變形卷軸",
            TransformCondition = "狼人_re werewolf_3865",
            AntidoteEnabled = true,
            AntidoteItem = "解毒藥水",
            DeleteEnabled = true,
            DeleteList = ["破爛的短劍"],
            DissolveList = ["生鏽的頭盔"],
            ShoutEnabled = true,
            ShoutIntervalSeconds = 45,
            ShoutMessages = ["收購木材", "販賣藥水"],
            TimersEnabled = true,
            HelperEntries = [HelperEntrySyntax.Parse("0_加速術/ME")],
            HelperInventoryEntries = [HelperEntrySyntax.Parse("2_勇敢藥水")],
        };

        settings.ManaWhenSafe.Enabled = true;
        settings.ManaWhenSafe.HitPointsAtLeast = 80;
        settings.ManaWhenSafe.ManaAtMost = 30;
        settings.ManaWhenSafe.Item = "心靈轉換/M";
        settings.Macros[1].Enabled = true;
        settings.Macros[1].Command = "肉";
        settings.TimerRows[0].Enabled = true;
        settings.TimerRows[0].IntervalSeconds = 90;
        settings.TimerRows[0].Command = "0_加速術/ME";
        settings.Misc.GainDrift = true;
        settings.Misc.DamageAtFeet = true;

        _source.Publish(settings);

        using var window = Window();
        var back = window.ToSettings();

        back.Profile.ShouldBe("Sqw887979");
        back.PotionUsePercent.ShouldBeTrue();
        back.PotionShowInventory.ShouldBeTrue();
        back.HelperEnabled.ShouldBeTrue();
        back.HelperEntries.ShouldBe(settings.HelperEntries);
        back.ShowExperience.ShouldBeTrue();
        back.KeepWeaponSharp.ShouldBeTrue();
        back.EatWhenHungry.ShouldBeTrue();
        back.TransformEnabled.ShouldBeTrue();
        back.TransformItem.ShouldBe("變形卷軸");
        back.TransformCondition.ShouldBe("狼人_re werewolf_3865");
        back.AntidoteEnabled.ShouldBeTrue();
        back.AntidoteItem.ShouldBe("解毒藥水");
        back.DeleteEnabled.ShouldBeTrue();
        back.DeleteList.ShouldBe(["破爛的短劍"]);
        back.DissolveList.ShouldBe(["生鏽的頭盔"]);
        back.ShoutEnabled.ShouldBeTrue();
        back.ShoutIntervalSeconds.ShouldBe(45u);
        back.ShoutMessages.ShouldBe(["收購木材", "販賣藥水"]);
        back.TimersEnabled.ShouldBeTrue();
        back.TimerRows[0].IntervalSeconds.ShouldBe(90u);
        back.TimerRows[0].Command.ShouldBe("0_加速術/ME");
        back.Macros[1].Enabled.ShouldBeTrue();
        back.Macros[1].Command.ShouldBe("肉");
        back.ManaWhenSafe.HitPointsAtLeast.ShouldBe(80u);
        back.ManaWhenSafe.ManaAtMost.ShouldBe(30u);
        back.ManaWhenSafe.Item.ShouldBe("心靈轉換/M");
        back.Misc.GainDrift.ShouldBeTrue();
        back.Misc.DamageAtFeet.ShouldBeTrue();
    }

    // Nothing in this window edits them, and a window that dropped them would lose them
    // the first time the player moved any switch at all.
    [Fact]
    public void CarriesThroughTheListItDoesNotShow()
    {
        var carried = HelperEntrySyntax.Parse("2_勇敢藥水");
        _source.Publish(new AuxSettings { HelperInventoryEntries = [carried] });

        using var window = Window();
        window.Misc.AllDay = true;

        _source.Current.HelperInventoryEntries.ShouldBe([carried]);
    }

    [Fact]
    public void KeepsTheBuffListInTheOrderItWasGiven()
    {
        _source.Publish(new AuxSettings
        {
            HelperEntries =
            [
                HelperEntrySyntax.Parse("0_加速術/ME"),
                HelperEntrySyntax.Parse("4_保護罩/ME"),
            ],
        });

        using var window = Window();

        window.Buffs.Items.ShouldBe(["0_加速術/ME", "4_保護罩/ME"]);

        window.Buffs.Selected = "4_保護罩/ME";
        window.Buffs.MoveUpCommand.Execute(null);

        _source.Current.HelperEntries.Select(HelperEntrySyntax.ToSettingText)
            .ShouldBe(["4_保護罩/ME", "0_加速術/ME"]);
    }

    // The same number means two different things depending on this switch, and the
    // reference let the old one stand under the new meaning with nothing to say so.
    [Fact]
    public void BoundsADrinkingThresholdByHowItIsRead()
    {
        using var window = Window();

        window.UsePercent = false;
        window.Potions[0].Ceiling.ShouldBe(999_999);

        window.UsePercent = true;
        window.Potions[0].Ceiling.ShouldBe(100);
    }

    [Fact]
    public void WritesBackNoMoreThanTheCeiling()
    {
        using var window = Window();

        window.UsePercent = true;
        window.Potions[0].Threshold = 5000;

        _source.Current.Potions[0].Threshold.ShouldBe(100u);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(5u, 5u)]
    [InlineData(999_999u, 86_400u)]
    public void BoundsTheShoutingInterval(uint given, uint expected)
    {
        _source.Publish(new AuxSettings { ShoutIntervalSeconds = given });

        using var window = Window();

        window.ShoutIntervalSeconds.ShouldBe(expected);
        window.ToSettings().ShoutIntervalSeconds.ShouldBe(expected);
    }

    // An interval of nothing runs the command on every pass of the loop, which is a
    // command sent to the server ten times a second. The reference's box took it.
    [Fact]
    public void BoundsATimerInterval()
    {
        var settings = new AuxSettings();
        settings.TimerRows[0].IntervalSeconds = 0;
        _source.Publish(settings);

        using var window = Window();

        window.Timers[0].IntervalSeconds.ShouldBe(1);
        window.ToSettings().TimerRows[0].IntervalSeconds.ShouldBe(1u);
    }

    [Fact]
    public void ShowsWhatSomebodyElsePublished()
    {
        using var window = Window();

        // What the profile task does when a character walks into the world.
        _source.Publish(new AuxSettings { Profile = "Sqw887979", ShowExperience = true });

        window.Profile.ShouldBe("Sqw887979");
        window.ShowExperience.ShouldBeTrue();
    }

    // Its own change coming back would re-fill every control on the screen, which fights
    // with whoever is typing into one of them.
    [Fact]
    public void DoesNotFillItselfInAgainFromItsOwnChange()
    {
        using var window = Window();
        var loads = 0;
        window.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HelperViewModel.Profile))
            {
                loads++;
            }
        };

        window.Profile = "Sqw887979";

        loads.ShouldBe(1);
    }

    [Fact]
    public void OffersTheShippedListsWhenThePlayerHasNoFile()
    {
        using var window = Window();

        window.HealingChoices.ShouldNotBeEmpty();
        window.ManaChoices.ShouldNotBeEmpty();
        window.StateChoices.ShouldNotBeEmpty();
        window.AntidoteChoices.ShouldNotBeEmpty();
        window.TransformItemChoices.ShouldNotBeEmpty();
        window.TransformationChoices.ShouldNotBeEmpty();
    }

    // Both lists are spelled the same way, though one comes out of the player's file and
    // the other out of their settings.
    [Fact]
    public void SpellsWhatCanBeKeptUpTheSameWayAsWhatIs()
    {
        using var window = Window();

        window.StateChoices.ShouldContain("0_加速術/ME");
    }

    [Fact]
    public void OffersWhatIsInTheBagOnlyWhenAskedTo()
    {
        var bag = new InventoryWatch();
        bag.Fill([Item("我的私房藥水 (3)")]);

        using var window = Window(bag);

        window.ShowInventory = false;
        window.HealingChoices.ShouldNotContain("我的私房藥水");

        window.ShowInventory = true;
        window.HealingChoices.ShouldContain("我的私房藥水");
    }

    [Fact]
    public void OffersWhatIsInTheBagForGettingRidOf()
    {
        var bag = new InventoryWatch();
        bag.Fill([Item("破爛的短劍"), Item("生鏽的頭盔 (2)")]);

        using var window = Window(bag);
        window.RefreshInventory();

        window.BagChoices.ShouldBe(["破爛的短劍", "生鏽的頭盔"]);
    }

    // Reading the bag means walking the client's memory. Worth doing while somebody is
    // looking at a list of it, and worth doing never otherwise.
    [Fact]
    public void AsksForTheBagOnlyWhileItIsOpen()
    {
        var bag = new InventoryWatch();
        var window = Window(bag);

        bag.Wanted.ShouldBeFalse();

        window.Watching = true;
        bag.Wanted.ShouldBeTrue();

        window.Dispose();
        bag.Wanted.ShouldBeFalse();
    }

    [Fact]
    public void StopsListeningOnceItIsClosed()
    {
        var window = Window();
        window.Dispose();

        _source.Publish(new AuxSettings { Profile = "Sqw887979" });

        window.Profile.ShouldBeEmpty();
    }

    [Fact]
    public void MakesNoChangeOfItsOwnJustByBeingOpened()
    {
        var settings = new AuxSettings { Profile = "Sqw887979" };
        _source.Publish(settings);

        using var window = Window();

        _source.Current.ShouldBeSameAs(settings);
    }
}
