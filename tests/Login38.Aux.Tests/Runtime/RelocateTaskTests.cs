using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Hunt;
using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers moving on when the spot has run out.
/// </summary>
/// <remarks>
/// The expensive failure is the same one the escape rule has — reading the whole stack — but
/// it arrives differently here. A character that is genuinely wedged stays wedged, so the
/// condition is still true on the pass after the scroll and on every pass until the server
/// moves it. Both clocks are therefore restarted by the read itself rather than by noticing
/// the character has landed.
/// </remarks>
public sealed class RelocateTaskTests : IDisposable
{
    private const string Scroll = "瞬間移動卷軸";
    private const string Blessed = "祝福的瞬間移動卷軸";
    private const uint ScrollId = 0x4001;
    private const uint BlessedId = 0x4002;

    private static readonly Dictionary<string, uint> Ids = new(StringComparer.Ordinal)
    {
        [Scroll] = ScrollId, [Blessed] = BlessedId,
    };

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingActions _actions = new();
    private readonly StubScan _scan = new();

    private (int X, int Y)? _at = (100, 200);
    private TimeSpan _slept;

    public void Dispose() => _process.Dispose();

    [Fact]
    public void RunsTwiceASecond() => Task().Interval.ShouldBe(TimeSpan.FromMilliseconds(500));

    [Fact]
    public void DoesNothingWhileThereIsSomethingToHuntAndTheCharacterIsMoving()
    {
        _scan.Found = [Wolf()];

        var task = Task();

        Walk(task, Rule(), steps: 10);

        _actions.Teleports.ShouldBeEmpty();
    }

    // Rooted. The character has not left the square for longer than the rule allows, and
    // there is plenty to hunt — which is the point: the room is fine, the character is not
    // getting to any of it.
    [Fact]
    public void ReadsAScrollWhenTheCharacterHasNotMovedForLongEnough()
    {
        _scan.Found = [Wolf()];

        Stand(Rule(stuck: 30), seconds: 31);

        _actions.Teleports.ShouldBe([ScrollId]);
    }

    // The one that shipped broken: a ranged character stands still to shoot, so "has not
    // moved" on its own says nothing about whether the spot is working — and with the seconds
    // set low it read a scroll in the middle of a fight. Something losing health is the spot
    // working, whoever is standing where.
    [Fact]
    public void LeavesTheScrollAloneWhileSomethingIsLosingHealth()
    {
        var task = Task();
        var rule = Rule(stuck: 30);

        _scan.Found = [Wolf(health: 100)];
        Step(task, rule);

        for (byte left = 90; left > 40; left -= 10)
        {
            _slept += TimeSpan.FromSeconds(20);
            _scan.Found = [Wolf(health: left)];

            Step(task, rule);
        }

        _actions.Teleports.ShouldBeEmpty();
    }

    // And the other half of it: standing over something that is not losing health is exactly
    // what being stuck looks like.
    [Fact]
    public void ReadsAScrollWhenNothingIsLosingHealthEither()
    {
        _scan.Found = [Wolf(health: 100)];

        Stand(Rule(stuck: 30), seconds: 31);

        _actions.Teleports.ShouldBe([ScrollId]);
    }

    // Barren. Nothing the picker would take, for long enough, whether or not the character
    // has been walking about.
    [Fact]
    public void ReadsAScrollWhenThereHasBeenNothingToHunt()
    {
        var task = Task();
        var rule = Rule(stuck: 0, barren: 20);

        Walk(task, rule, steps: 2);
        _slept += TimeSpan.FromSeconds(21);
        Step(task, rule);

        _actions.Teleports.ShouldBe([ScrollId]);
    }

    // A monster is worth staying for, however long the room was empty before it turned up.
    [Fact]
    public void ForgetsAnEmptyRoomAsSoonAsSomethingIsInIt()
    {
        var task = Task();
        var rule = Rule(stuck: 0, barren: 20);

        Step(task, rule);
        _slept += TimeSpan.FromSeconds(19);
        _scan.Found = [Wolf()];
        Step(task, rule);
        _scan.Found = [];
        _slept += TimeSpan.FromSeconds(5);
        Step(task, rule);

        _actions.Teleports.ShouldBeEmpty();
    }

    // The whole stack, otherwise. A wedged character is still wedged on the pass after the
    // scroll — the server has not moved it yet — so the read itself has to restart the clock.
    [Fact]
    public void DoesNotReadASecondScrollWhileTheFirstIsStillWorking()
    {
        _scan.Found = [Wolf()];

        var task = Task();
        var rule = Rule(stuck: 30);

        Stand(rule, seconds: 31, task: task);
        Step(task, rule);
        Step(task, rule);

        _actions.Teleports.ShouldBe([ScrollId]);
    }

    [Fact]
    public void LeavesItAloneWhileTheHuntIsOff()
    {
        _scan.Found = [Wolf()];

        Stand(Rule(stuck: 30), seconds: 31, hunting: false);

        _actions.Teleports.ShouldBeEmpty();
    }

    // Zero is off, and off has to mean off rather than "immediately".
    [Fact]
    public void TreatsZeroSecondsAsTurningThatHalfOff()
    {
        _scan.Found = [];

        Stand(Rule(stuck: 0, barren: 0), seconds: 120);

        _actions.Teleports.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingWithNoScrollInTheBag()
    {
        _scan.Found = [Wolf()];

        Stand(Rule(stuck: 30), seconds: 31, bag: []);

        _actions.Teleports.ShouldBeEmpty();
        _actions.Options.ShouldBeEmpty();
    }

    /// <summary>Stands on one square for a while, one pass every half second of it.</summary>
    private void Stand(
        RelocateRule rule,
        int seconds,
        string[]? bag = null,
        bool hunting = true,
        RelocateTask? task = null)
    {
        var running = task ?? Task();

        Step(running, rule, bag, hunting);
        _slept += TimeSpan.FromSeconds(seconds);
        Step(running, rule, bag, hunting);
    }

    /// <summary>Walks a square a pass, which is what not being stuck looks like.</summary>
    private void Walk(RelocateTask task, RelocateRule rule, int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            _at = (100 + (i * 2), 200);
            _slept += TimeSpan.FromSeconds(5);

            Step(task, rule);
        }
    }

    private void Step(
        RelocateTask task, RelocateRule rule, string[]? bag = null, bool hunting = true) =>
        task.Tick(Context(rule, bag ?? [Scroll, Blessed], hunting));

    private RelocateTask Task() => new(
        _actions, _scan, NullLogger<RelocateTask>.Instance, _ => _at, () => _slept);

    private static RelocateRule Rule(
        uint stuck = 30,
        uint barren = 0,
        string item = Scroll) =>
        new()
        {
            Enabled = true,
            StuckSeconds = stuck,
            BarrenSeconds = barren,
            Item = item,
        };

    private StubContext Context(RelocateRule rule, string[] bag, bool hunting)
    {
        var settings = new AuxSettings();

        settings.Hunt.Enabled = hunting;
        settings.Hunt.Relocate = rule;

        return new StubContext(_process, settings, Bag(bag));
    }

    private static HuntTarget Wolf(byte health = 100) =>
        new(new GameAddress(0x2000), 0x11, "狼", 120, 200, health);

    private static IReadOnlyList<InventoryItem> Bag(string[] names) =>
    [
        .. names.Select((name, i) =>
            new InventoryItem(new GameAddress(0x1000u + ((uint)i * 0x100)), Ids[name], 0, 0, false, 1, name)),
    ];

    private sealed class RecordingActions()
        : GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance)
    {
        private readonly List<uint> _teleports = [];
        private readonly List<(uint Item, string Choice)> _options = [];

        public IReadOnlyList<uint> Teleports => _teleports;

        public IReadOnlyList<(uint Item, string Choice)> Options => _options;

        public override void UseTeleportScroll(RemoteProcess process, uint itemId) =>
            _teleports.Add(itemId);

        public override void UseWithOption(RemoteProcess process, uint itemId, string choice) =>
            _options.Add((itemId, choice));

        public override void ConfirmTeleport(RemoteProcess process)
        {
        }
    }

    private sealed class StubScan : TargetScan
    {
        public StubScan()
            : base(LegacyTextCodec.Auto)
        {
        }

        public IReadOnlyList<HuntTarget> Found { get; set; } = [];

        public override IReadOnlyList<HuntTarget> All(RemoteProcess process) => Found;
    }

    private sealed class StubContext(
        RemoteProcess process, AuxSettings settings, IReadOnlyList<InventoryItem> bag)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        public override bool IsInWorld => true;

        public override IReadOnlyList<InventoryItem> Bag => bag;
    }
}
