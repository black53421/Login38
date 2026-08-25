using System.Text.Json;
using Login38.Aux.Settings;
using Shouldly;

namespace Login38.Aux.Tests.Settings;

/// <summary>
/// Pins the settings file's shape against the one players already have.
/// </summary>
/// <remarks>
/// These files hold a lot of manual setup — seven drinking rules, a helper list, six
/// timers, per character. A rename on either side loses all of it silently: the file still
/// reads, the field is just missing, and the player finds out when the rule does not fire.
/// </remarks>
public sealed class AuxSettingsJsonTests
{
    // A file written by the reference, cut down to one of each interesting shape.
    private const string Existing = """
        {
          "current_profile": "Sqw887979",
          "potion_rows": [
            { "enabled": true, "threshold": 60, "item": "綠色藥水" },
            { "enabled": false, "threshold": 0, "item": "" },
            { "enabled": false, "threshold": 0, "item": "" },
            { "enabled": false, "threshold": 0, "item": "" },
            { "enabled": false, "threshold": 0, "item": "" },
            { "enabled": false, "threshold": 0, "item": "" },
            { "enabled": false, "threshold": 0, "item": "" }
          ],
          "mp_when_safe": { "enabled": true, "hp_lower": 80, "mp_upper": 40, "item": "藍色藥水" },
          "potion_use_percent": true,
          "potion_show_inventory": false,
          "buff_enabled": true,
          "buff_items": [
            { "id": 0, "name": "加速術", "item_type": "S", "cast_target": "Self_" },
            { "id": -1, "name": "提煉魔石", "item_type": "S", "cast_target": { "OnNamedItem": "紅魔石" } },
            { "id": -1, "name": "淨化卷軸", "item_type": "I", "cast_target": { "OnInUseItem": null } },
            { "id": -1, "name": "祝福卷軸", "item_type": "I", "cast_target": { "OnWieldedItem": "神聖戰鎚" } },
            { "id": 3, "name": "召喚術", "item_type": "K", "cast_target": { "Key": 1 } },
            { "id": 4, "name": "龍息術", "item_type": "K", "cast_target": { "DelayKey": 12 } },
            { "id": -1, "name": "肉", "item_type": "I", "cast_target": "Item" }
          ],
          "buff_inventory_items": [],
          "status_show_exp": true,
          "status_whetstone": false,
          "status_eat_meat": true,
          "status_transform_enabled": false,
          "status_transform_item": "變形卷軸",
          "status_transform_cond": "hp<50",
          "status_antidote_enabled": true,
          "status_antidote_item": "解毒藥水",
          "fkey_macros": [
            { "enabled": true, "command": "加速術/ME" },
            { "enabled": false, "command": "" },
            { "enabled": false, "command": "" },
            { "enabled": false, "command": "" }
          ],
          "delete_enabled": true,
          "delete_list": ["破爛的短劍"],
          "dissolve_list": ["生鏽的匕首"],
          "shout_enabled": false,
          "shout_interval_sec": 30,
          "shout_messages": ["收購材料"],
          "misc": {
            "all_day": true,
            "underwater_pump": false,
            "low_cpu": true,
            "monster_level_color": true,
            "show_clock": false,
            "show_attack_dmg": true,
            "damage_at_feet": false
          },
          "timer_master_enabled": true,
          "timer_rows": [
            { "enabled": true, "interval_sec": 300, "command": "肉" },
            { "enabled": false, "interval_sec": 5, "command": "" },
            { "enabled": false, "interval_sec": 5, "command": "" },
            { "enabled": false, "interval_sec": 5, "command": "" },
            { "enabled": false, "interval_sec": 5, "command": "" },
            { "enabled": false, "interval_sec": 5, "command": "" }
          ]
        }
        """;

    [Fact]
    public void ReadsAFileTheReferenceWrote()
    {
        var settings = Read();

        settings.Profile.ShouldBe("Sqw887979");
        settings.Potions[0].Enabled.ShouldBeTrue();
        settings.Potions[0].Threshold.ShouldBe(60u);
        settings.Potions[0].Item.ShouldBe("綠色藥水");
        settings.PotionUsePercent.ShouldBeTrue();

        settings.ManaWhenSafe.Enabled.ShouldBeTrue();
        settings.ManaWhenSafe.HitPointsAtLeast.ShouldBe(80u);
        settings.ManaWhenSafe.ManaAtMost.ShouldBe(40u);

        settings.ShowExperience.ShouldBeTrue();
        settings.EatWhenHungry.ShouldBeTrue();
        settings.AntidoteItem.ShouldBe("解毒藥水");
        settings.TransformCondition.ShouldBe("hp<50");

        settings.Macros[0].Command.ShouldBe("加速術/ME");
        settings.DeleteList.ShouldBe(["破爛的短劍"]);
        settings.DissolveList.ShouldBe(["生鏽的匕首"]);
        settings.ShoutIntervalSeconds.ShouldBe(30u);
        settings.ShoutMessages.ShouldBe(["收購材料"]);

        settings.Misc.AllDay.ShouldBeTrue();
        settings.Misc.LowCpu.ShouldBeTrue();
        settings.Misc.MonsterLevelColour.ShouldBeTrue();
        settings.Misc.ShowAttackDamage.ShouldBeTrue();
        settings.Misc.ShowClock.ShouldBeFalse();

        settings.TimersEnabled.ShouldBeTrue();
        settings.TimerRows[0].IntervalSeconds.ShouldBe(300u);
        settings.TimerRows[0].Command.ShouldBe("肉");
    }

    // A bare string for the ones that take no target, an object for the ones that do —
    // which is what the reference's serialiser produced and what is in every existing file.
    [Fact]
    public void ReadsEveryShapeOfCastTarget()
    {
        var entries = Read().HelperEntries;

        entries.Count.ShouldBe(7);

        entries[0].Kind.ShouldBe(EntryKind.Skill);
        entries[0].Cast.Kind.ShouldBe(CastKind.OnSelf);

        entries[1].Cast.Kind.ShouldBe(CastKind.OnNamedItem);
        entries[1].Cast.Target.ShouldBe("紅魔石");

        // Null is not absent here: it means "the first one you find", which is a different
        // instruction from acting on something with no name.
        entries[2].Cast.Kind.ShouldBe(CastKind.OnInUseItem);
        entries[2].Cast.Target.ShouldBeNull();

        entries[3].Cast.Kind.ShouldBe(CastKind.OnWieldedItem);
        entries[3].Cast.Target.ShouldBe("神聖戰鎚");

        entries[4].Kind.ShouldBe(EntryKind.Key);
        entries[4].Cast.Kind.ShouldBe(CastKind.Key);
        entries[4].Cast.FunctionKey.ShouldBe((byte)1);

        entries[5].Cast.Kind.ShouldBe(CastKind.DelayKey);
        entries[5].Cast.FunctionKey.ShouldBe((byte)12);

        entries[6].Kind.ShouldBe(EntryKind.Item);
        entries[6].Cast.Kind.ShouldBe(CastKind.Item);
    }

    [Fact]
    public void WritesWhatItRead()
    {
        var settings = Read();
        var written = JsonSerializer.Serialize(settings, AuxSettingsJson.Options);

        JsonSerializer.Deserialize<AuxSettings>(written, AuxSettingsJson.Options)
            .ShouldNotBeNull()
            .HelperEntries.ShouldBe(settings.HelperEntries);
    }

    // The names on the wire are the contract with files that already exist.
    [Theory]
    [InlineData("\"current_profile\"")]
    [InlineData("\"potion_rows\"")]
    [InlineData("\"mp_when_safe\"")]
    [InlineData("\"hp_lower\"")]
    [InlineData("\"mp_upper\"")]
    [InlineData("\"buff_items\"")]
    [InlineData("\"item_type\"")]
    [InlineData("\"cast_target\"")]
    [InlineData("\"fkey_macros\"")]
    [InlineData("\"timer_rows\"")]
    [InlineData("\"interval_sec\"")]
    [InlineData("\"monster_level_color\"")]
    [InlineData("\"damage_at_feet\"")]
    public void KeepsTheNamesTheFileAlreadyUses(string name) =>
        JsonSerializer.Serialize(Read(), AuxSettingsJson.Options).ShouldContain(name);

    // The variant name has a trailing underscore because the language it was written in
    // reserves the word. It is what is on disk, so it is what is read and written.
    [Fact]
    public void KeepsTheReferencesOwnNameForSelfCast() =>
        JsonSerializer.Serialize(Read(), AuxSettingsJson.Options).ShouldContain("\"Self_\"");

    // Players open this file. Escaped names cannot be read or corrected by hand.
    [Fact]
    public void WritesNamesAsThemselves() =>
        JsonSerializer.Serialize(Read(), AuxSettingsJson.Options).ShouldContain("綠色藥水");

    // An older file, or one somebody trimmed by hand. The window indexes these arrays.
    [Fact]
    public void FillsInRowsAFileLeftOut()
    {
        var settings = JsonSerializer.Deserialize<AuxSettings>(
            """{ "potion_rows": [ { "enabled": true, "threshold": 10, "item": "肉" } ] }""",
            AuxSettingsJson.Options)!.Normalise();

        settings.Potions.Length.ShouldBe(AuxSettings.PotionRows);
        settings.Potions[0].Item.ShouldBe("肉");
        settings.Potions[6].Item.ShouldBeEmpty();
        settings.Macros.Length.ShouldBe(AuxSettings.FunctionKeyMacros);
        settings.TimerRows.Length.ShouldBe(AuxSettings.Timers);
    }

    // A file from a version with more rows than this one has.
    [Fact]
    public void TrimsRowsAFileHasTooManyOf()
    {
        var rows = string.Join(',', Enumerable.Repeat("""{ "enabled": false, "interval_sec": 1, "command": "" }""", 9));

        var settings = JsonSerializer.Deserialize<AuxSettings>(
            $$"""{ "timer_rows": [{{rows}}] }""", AuxSettingsJson.Options)!.Normalise();

        settings.TimerRows.Length.ShouldBe(AuxSettings.Timers);
    }

    [Fact]
    public void StartsFromDefaultsForAnEmptyFile()
    {
        var settings = JsonSerializer.Deserialize<AuxSettings>("{}", AuxSettingsJson.Options)!.Normalise();

        settings.Potions.Length.ShouldBe(AuxSettings.PotionRows);
        settings.HelperEntries.ShouldBeEmpty();
        settings.ManaWhenSafe.ShouldNotBeNull();
        settings.Misc.ShouldNotBeNull();
        settings.TimerRows[0].IntervalSeconds.ShouldBe(5u);
    }

    // A field from a newer version is not a reason to lose the file.
    [Fact]
    public void IgnoresAFieldItDoesNotKnow() =>
        Should.NotThrow(() => JsonSerializer.Deserialize<AuxSettings>(
            """{ "current_profile": "A", "something_new": { "a": [1, 2] } }""", AuxSettingsJson.Options));

    // Likewise a cast kind it does not know: that entry falls back to plainly using the
    // item, which cannot send the server a packet it did not expect.
    [Fact]
    public void ReadsACastKindItDoesNotKnowAsUsingTheItem()
    {
        var settings = JsonSerializer.Deserialize<AuxSettings>(
            """{ "buff_items": [ { "id": 1, "name": "x", "item_type": "S", "cast_target": "OnSomethingNew" } ] }""",
            AuxSettingsJson.Options)!;

        settings.HelperEntries[0].Cast.Kind.ShouldBe(CastKind.Item);
    }

    private static AuxSettings Read() =>
        JsonSerializer.Deserialize<AuxSettings>(Existing, AuxSettingsJson.Options)!.Normalise();
}
