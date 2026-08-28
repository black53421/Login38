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
/// Covers reading a scroll when a fight has gone badly.
/// </summary>
/// <remarks>
/// The expensive failure here is not missing an escape, it is reading the whole stack: the
/// character stays below the threshold for the second or two it takes the server to move
/// them, and a rule that only asked "are we low" would fire on every pass of that second.
/// So the arming is pinned as hard as the firing.
/// </remarks>
public sealed class EscapeTaskTests : IDisposable
{
    private const string Scroll = "順風卷軸";
    private const string Blessed = "祝福的順風卷軸";
    private const uint ScrollId = 0x3001;
    private const uint BlessedId = 0x3002;

    private static readonly Dictionary<string, uint> Ids = new(StringComparer.Ordinal)
    {
        [Scroll] = ScrollId, [Blessed] = BlessedId,
    };

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingActions _actions = new();
    private readonly HuntSwitch _hunting = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void ReadsAnOrdinaryScrollWithNoDestination()
    {
        Run(Rule(Scroll), hp: 20, bag: [Scroll]);

        _actions.Teleports.ShouldBe([ScrollId]);
        _actions.Options.ShouldBeEmpty();
    }

    // Some servers send the offer after the scroll and will not move the character until it
    // is answered. Sending it when nothing is pending is ignored.
    [Fact]
    public void AnswersTheOfferTheServerMayBeWaitingFor()
    {
        Run(Rule(Scroll), hp: 20, bag: [Scroll]);

        _actions.Confirmed.ShouldBe(1);
    }

    [Fact]
    public void LeavesTheScrollAloneWhileHealthIsHighEnough()
    {
        Run(Rule(Scroll), hp: 40, bag: [Scroll]);

        _actions.Teleports.ShouldBeEmpty();
    }

    // At the threshold is not below it, the same way a drinking rule reads.
    [Fact]
    public void DoesNotReadExactlyAtTheThreshold()
    {
        Run(Rule(Scroll), hp: 30, bag: [Scroll]);

        _actions.Teleports.ShouldBeEmpty();
    }

    // The whole cooldown. Below the line the character stays below it for as long as the
    // teleport takes, and every pass of that is another scroll.
    [Fact]
    public void ReadsOneScrollAndNotTheStack()
    {
        var task = Task();

        task.Tick(Context(Rule(Scroll), hp: 20, bag: [Scroll]));
        task.Tick(Context(Rule(Scroll), hp: 20, bag: [Scroll]));
        task.Tick(Context(Rule(Scroll), hp: 20, bag: [Scroll]));

        _actions.Teleports.ShouldBe([ScrollId]);
    }

    [Fact]
    public void ArmsItselfAgainOnceTheCharacterHasRecovered()
    {
        var task = Task();

        task.Tick(Context(Rule(Scroll), hp: 20, bag: [Scroll]));
        task.Tick(Context(Rule(Scroll), hp: 90, bag: [Scroll]));
        task.Tick(Context(Rule(Scroll), hp: 20, bag: [Scroll]));

        _actions.Teleports.ShouldBe([ScrollId, ScrollId]);
    }

    // Being out of scrolls must not disarm it: the next pass is as good a time as any to
    // notice one has been picked up.
    [Fact]
    public void StaysArmedWhileThereIsNothingToRead()
    {
        var task = Task();

        task.Tick(Context(Rule(Scroll), hp: 20, bag: []));
        task.Tick(Context(Rule(Scroll), hp: 20, bag: [Scroll]));

        _actions.Teleports.ShouldBe([ScrollId]);
    }

    [Fact]
    public void DoesNothingWhileItIsSwitchedOff()
    {
        var rule = Rule(Scroll);
        rule.Enabled = false;

        Run(rule, hp: 5, bag: [Scroll]);

        _actions.Teleports.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingWithNoScrollNamed()
    {
        Run(Rule(string.Empty), hp: 5, bag: [Scroll]);

        _actions.Teleports.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingBeforeThereIsACharacter()
    {
        Task().Tick(Context(Rule(Scroll), hp: 5, bag: [Scroll], inWorld: false));

        _actions.Teleports.ShouldBeEmpty();
    }

    // Zero out of zero is a character the server has not finished describing, and reads as
    // nought per cent — which would read a scroll on the way into the world.
    [Fact]
    public void DoesNothingBeforeTheServerHasSaidHowMuchHealthThereIs()
    {
        Task().Tick(Context(Rule(Scroll), hp: 0, max: 0, bag: [Scroll]));

        _actions.Teleports.ShouldBeEmpty();
    }

    // Running away is not a change of hunting ground. The health was going and landing
    // somewhere else fixes none of that, so carrying on walks the same character into the
    // same trouble with less to spend on it. The flag goes off in the settings because that
    // is the copy the hunt reads on every pass.
    [Fact]
    public void TurnsTheHuntOffAfterwards()
    {
        var context = Context(Rule(Scroll), hp: 20, bag: [Scroll]);

        context.Settings.Hunt.Enabled = true;

        Task().Tick(context);

        context.Settings.Hunt.Enabled.ShouldBeFalse();
        _hunting.Taken().ShouldBeTrue();
    }

    [Fact]
    public void LeavesTheHuntAloneWhenNoScrollWasRead()
    {
        var context = Context(Rule(Scroll), hp: 20, bag: []);

        context.Settings.Hunt.Enabled = true;

        Task().Tick(context);

        context.Settings.Hunt.Enabled.ShouldBeTrue();
        _hunting.Taken().ShouldBeFalse();
    }

    [Fact]
    public void RunsTwiceASecond() => Task().Interval.ShouldBe(TimeSpan.FromMilliseconds(500));

    private EscapeTask Task() => new(_actions, _hunting, NullLogger<EscapeTask>.Instance);

    private void Run(EscapeRule rule, uint hp, string[] bag) =>
        Task().Tick(Context(rule, hp, bag));

    private StubContext Context(
        EscapeRule rule, uint hp, string[] bag, uint max = 100, bool inWorld = true)
    {
        var settings = new AuxSettings();
        settings.Hunt.Escape = rule;

        return new StubContext(_process, settings, Player(hp, max), Bag(bag), inWorld);
    }

    private static EscapeRule Rule(string item) =>
        new()
        {
            Enabled = true,
            HitPointsBelow = 30,
            Item = item,
        };

    private static PlayerState Player(uint hp, uint max) =>
        new(new Gauge(hp, max), new Gauge(50, 100), 100, 50, 4);

    private static IReadOnlyList<InventoryItem> Bag(params string[] names) =>
    [
        .. names.Select((name, i) =>
            new InventoryItem(new GameAddress(0x1000u + ((uint)i * 0x100)), Ids[name], 0, 0, false, 1, name)),
    ];

    /// <summary>A pass with a chosen player, bag and world.</summary>
    private sealed class StubContext(
        RemoteProcess process,
        AuxSettings settings,
        PlayerState player,
        IReadOnlyList<InventoryItem> bag,
        bool inWorld)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        public override bool IsInWorld => inWorld;

        public override PlayerState Player => player;

        public override IReadOnlyList<InventoryItem> Bag => bag;
    }

    /// <summary>Remembers what it was asked for rather than touching a client.</summary>
    private sealed class RecordingActions()
        : GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance)
    {
        private readonly List<uint> _teleports = [];
        private readonly List<(uint Item, string Choice)> _options = [];

        public IReadOnlyList<uint> Teleports => _teleports;

        public IReadOnlyList<(uint Item, string Choice)> Options => _options;

        public int Confirmed { get; private set; }

        public override void UseTeleportScroll(RemoteProcess process, uint itemId) =>
            _teleports.Add(itemId);

        public override void UseWithOption(RemoteProcess process, uint itemId, string choice) =>
            _options.Add((itemId, choice));

        public override void ConfirmTeleport(RemoteProcess process) => Confirmed++;
    }
}
