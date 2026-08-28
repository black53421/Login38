using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers when the helper drinks.
/// </summary>
/// <remarks>
/// This is the feature players notice: acting a moment too late loses a character, and
/// acting when it should not empties a bag of potions worth a week of play. So the rules
/// are pinned rather than left to read correctly.
/// </remarks>
public sealed class PotionTaskTests : IDisposable
{
    private const string Red = "紅色藥水";
    private const string Orange = "橘色藥水";
    private const string Blue = "藍色藥水";

    /// <summary>A fixed id per item, so what was used can be reported by name.</summary>
    private static readonly Dictionary<string, uint> Ids = new(StringComparer.Ordinal)
    {
        [Red] = 0x2001, [Orange] = 0x2002, [Blue] = 0x2003,
    };

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingActions _actions = new();
    private readonly StubSpells _spells = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void DrinksWhenHealthFallsBelowTheThreshold()
    {
        Run(Settings(Rule(Red, 50)), Player(hp: 40, max: 100), Bag(Red));

        _actions.Used.ShouldBe([Red]);
    }

    [Fact]
    public void LeavesItAloneWhileHealthIsHighEnough()
    {
        Run(Settings(Rule(Red, 50)), Player(hp: 60, max: 100), Bag(Red));

        _actions.Used.ShouldBeEmpty();
    }

    // Exactly at the threshold is not below it. Firing here would drink a potion at full
    // health for a rule set to 100.
    [Fact]
    public void DoesNotDrinkExactlyAtTheThreshold()
    {
        Run(Settings(Rule(Red, 50)), Player(hp: 50, max: 100), Bag(Red));

        _actions.Used.ShouldBeEmpty();
    }

    // The rows are in the player's order: the cheap potion above the expensive one, so the
    // expensive one is only reached when the cheap one has not been enough.
    [Fact]
    public void HonoursTheOrderTheRowsAreIn()
    {
        var settings = Settings(Rule(Red, 80), Rule(Orange, 30));

        Run(settings, Player(hp: 40, max: 100), Bag(Red, Orange));

        _actions.Used.ShouldBe([Red]);
    }

    [Fact]
    public void ReachesTheLowerRowWhenBothHaveBeenCrossed()
    {
        var settings = Settings(Rule(Red, 80), Rule(Orange, 30));

        Run(settings, Player(hp: 20, max: 100), Bag(Red, Orange));

        _actions.Used.ShouldBe([Red, Orange]);
    }

    [Fact]
    public void SkipsARowWhoseSwitchIsOff()
    {
        var settings = Settings(Rule(Red, 80, enabled: false), Rule(Orange, 80));

        Run(settings, Player(hp: 20, max: 100), Bag(Red, Orange));

        _actions.Used.ShouldBe([Orange]);
    }

    [Fact]
    public void SaysSoRatherThanDrinkingSomethingElseWhenTheBagIsEmpty()
    {
        Run(Settings(Rule(Red, 50)), Player(hp: 10, max: 100), Bag());

        _actions.Used.ShouldBeEmpty();
    }

    // The threshold means one of two quite different things depending on a single switch
    // that applies to every row at once, which is the client's own arrangement.
    [Fact]
    public void ReadsTheThresholdAsAnAmountWhenPercentagesAreOff()
    {
        var settings = Settings(Rule(Red, 500));
        settings.PotionUsePercent = false;

        Run(settings, Player(hp: 400, max: 2000), Bag(Red));

        _actions.Used.ShouldBe([Red]);
    }

    [Fact]
    public void ReadsTheSameThresholdAsAPercentageWhenTheyAreOn()
    {
        var settings = Settings(Rule(Red, 50));

        // 400 of 2000 is 20%, which is below 50 — as an amount, 400 is above it.
        Run(settings, Player(hp: 400, max: 2000), Bag(Red));

        _actions.Used.ShouldBe([Red]);
    }

    // Between the character screen and the world the inventory and the item routines are
    // not there. Acting then does not fail, it crashes the game.
    [Fact]
    public void DoesNothingBeforeTheCharacterIsInTheWorld()
    {
        Run(Settings(Rule(Red, 50)), Player(hp: 10, max: 100), Bag(Red), inWorld: false);

        _actions.Used.ShouldBeEmpty();
    }

    // A character who has just arrived, before the server has said how much health they
    // have. Zero out of zero reads as "empty" and would drink everything they own.
    [Fact]
    public void WaitsUntilTheServerHasSaidHowMuchHealthThereIs()
    {
        Run(Settings(Rule(Red, 50)), Player(hp: 0, max: 0), Bag(Red));

        _actions.Used.ShouldBeEmpty();
    }

    [Fact]
    public void ReadsNothingAtAllWhenNoRuleIsSwitchedOn()
    {
        var context = new StubContext(_process, new AuxSettings(), Player(hp: 1, max: 100), Bag(Red), true);

        Task().Tick(context);

        context.Reads.ShouldBe(0);
    }

    // Mana potions cost hit points in this game, so drinking one at the wrong moment is
    // worse than being out of mana. Both conditions have to hold.
    [Fact]
    public void TopsUpManaOnlyWhileHealthIsHigh()
    {
        var settings = Settings();
        settings.ManaWhenSafe = new ManaWhenSafeRule
        {
            Enabled = true, HitPointsAtLeast = 80, ManaAtMost = 40, Item = Blue,
        };

        Run(settings, Player(hp: 90, max: 100, mp: 20, maxMp: 100), Bag(Blue));

        _actions.Used.ShouldBe([Blue]);
    }

    [Fact]
    public void WillNotPayHitPointsForManaWhileHurt()
    {
        var settings = Settings();
        settings.ManaWhenSafe = new ManaWhenSafeRule
        {
            Enabled = true, HitPointsAtLeast = 80, ManaAtMost = 40, Item = Blue,
        };

        Run(settings, Player(hp: 50, max: 100, mp: 20, maxMp: 100), Bag(Blue));

        _actions.Used.ShouldBeEmpty();
    }

    [Fact]
    public void LeavesManaAloneWhenThereIsEnoughOfIt()
    {
        var settings = Settings();
        settings.ManaWhenSafe = new ManaWhenSafeRule
        {
            Enabled = true, HitPointsAtLeast = 80, ManaAtMost = 40, Item = Blue,
        };

        Run(settings, Player(hp: 90, max: 100, mp: 90, maxMp: 100), Bag(Blue));

        _actions.Used.ShouldBeEmpty();
    }

    // A rule can be a skill rather than an item — a healing spell instead of a potion.
    [Fact]
    public void CastsWhenTheRuleNamesASkill()
    {
        _spells.Known["治癒術"] = 42;

        Run(Settings(Rule("治癒術/ME", 50)), Player(hp: 10, max: 100), Bag());

        _actions.Casts.ShouldBe([(42u, SkillAim.Self)]);
    }

    // A potion rule that fired at whatever the mouse happened to be over is a rule that
    // heals a monster.
    [Fact]
    public void RefusesASkillAimedSomewhereARuleCannotFollow()
    {
        _spells.Known["治癒術"] = 42;

        Run(Settings(Rule("治癒術/MT", 50)), Player(hp: 10, max: 100), Bag());

        _actions.Casts.ShouldBeEmpty();
    }

    [Fact]
    public void RunsTwiceASecond() => Task().Interval.ShouldBe(TimeSpan.FromMilliseconds(500));

    private PotionTask Task() =>
        new(_actions, _spells, NullLogger<PotionTask>.Instance);

    private void Run(
        AuxSettings settings, PlayerState player, IReadOnlyList<InventoryItem> bag, bool inWorld = true) =>
        Task().Tick(new StubContext(_process, settings, player, bag, inWorld));

    private static AuxSettings Settings(params PotionRow[] rows)
    {
        var settings = new AuxSettings { PotionUsePercent = true };

        for (var i = 0; i < rows.Length; i++)
        {
            settings.Potions[i] = rows[i];
        }

        return settings;
    }

    private static PotionRow Rule(string item, uint threshold, bool enabled = true) =>
        new() { Enabled = enabled, Threshold = threshold, Item = item };

    private static PlayerState Player(uint hp, uint max, uint mp = 0, uint maxMp = 100) =>
        new(new Gauge(hp, max), new Gauge(mp, maxMp), 100, 50, 4);

    private static IReadOnlyList<InventoryItem> Bag(params string[] names) =>
    [
        .. names.Select((name, i) =>
            new InventoryItem(new GameAddress(0x1000u + ((uint)i * 0x100)), Ids[name], 0, 0, false, 1, name)),
    ];

    private static string NameOf(uint id) => Ids.First(pair => pair.Value == id).Key;

    /// <summary>A pass with a chosen player, bag and world.</summary>
    private sealed class StubContext : AuxContext
    {
        private readonly PlayerState _player;
        private readonly IReadOnlyList<InventoryItem> _bag;
        private readonly bool _inWorld;

        public StubContext(
            RemoteProcess process, AuxSettings settings, PlayerState player,
            IReadOnlyList<InventoryItem> bag, bool inWorld)
            : base(process, settings, LegacyTextCodec.Auto)
        {
            _player = player;
            _bag = bag;
            _inWorld = inWorld;
        }

        /// <summary>How many times the game was asked anything.</summary>
        public int Reads { get; private set; }

        public override bool IsInWorld
        {
            get
            {
                Reads++;
                return _inWorld;
            }
        }

        public override PlayerState Player
        {
            get
            {
                Reads++;
                return _player;
            }
        }

        public override IReadOnlyList<InventoryItem> Bag
        {
            get
            {
                Reads++;
                return _bag;
            }
        }
    }

    /// <summary>Remembers what it was asked for rather than touching a client.</summary>
    private sealed class RecordingActions() : GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance)
    {
        private readonly List<uint> _used = [];
        private readonly List<(uint Packed, SkillAim Aim)> _cast = [];

        /// <summary>What was used, back in the names the test wrote.</summary>
        public IReadOnlyList<string> Used => [.. _used.Select(NameOf)];

        public IReadOnlyList<(uint Packed, SkillAim Aim)> Casts => _cast;

        public override void SendUseItem(RemoteProcess process, uint itemId) => _used.Add(itemId);

        public override void Cast(
            RemoteProcess process, uint packed, SkillTarget target, GameAddress record = default) =>
            _cast.Add((packed, target.Aim));
    }

    /// <summary>A spell book with whatever the test says is in it.</summary>
    private sealed class StubSpells() : Spells(LegacyTextCodec.Auto, NullLogger<Spells>.Instance)
    {
        public Dictionary<string, uint> Known { get; } = [];

        public override uint? Find(RemoteProcess process, string name) =>
            Known.TryGetValue(name, out var packed) ? packed : null;
    }
}
