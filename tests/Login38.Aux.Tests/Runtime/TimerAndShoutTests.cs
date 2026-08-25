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
/// Covers the two features that act on a clock rather than on the player's condition.
/// </summary>
public sealed class TimerAndShoutTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    // A row that has never fired is due now, so switching a timer on does the thing rather
    // than waiting out its first interval in silence.
    [Fact]
    public void FiresARowThatHasNeverFired() =>
        TimerTask.Due(Rows(Row("加速術/ME", 60)), Never(), TimeSpan.Zero).ShouldBe(0);

    [Fact]
    public void WaitsOutTheIntervalAfterThat()
    {
        var fired = Never();
        fired[0] = TimeSpan.Zero;

        TimerTask.Due(Rows(Row("加速術/ME", 60)), fired, TimeSpan.FromSeconds(59)).ShouldBeNull();
        TimerTask.Due(Rows(Row("加速術/ME", 60)), fired, TimeSpan.FromSeconds(60)).ShouldBe(0);
    }

    // One row per pass. The client casts one skill at a time and the server rate-limits,
    // so firing three that came due together throws two of them away.
    [Fact]
    public void PicksOneRowEvenWhenSeveralAreDue() =>
        TimerTask.Due(Rows(Row("a", 1), Row("b", 1), Row("c", 1)), Never(), TimeSpan.Zero).ShouldBe(0);

    [Fact]
    public void SkipsARowWhoseSwitchIsOff() =>
        TimerTask.Due(Rows(Row("a", 1, enabled: false), Row("b", 1)), Never(), TimeSpan.Zero).ShouldBe(1);

    [Fact]
    public void SkipsARowWithNothingWrittenInIt() =>
        TimerTask.Due(Rows(Row("  ", 1), Row("b", 1)), Never(), TimeSpan.Zero).ShouldBe(1);

    [Fact]
    public void HasNothingToDoWithNoRows() =>
        TimerTask.Due(Rows(), Never(), TimeSpan.Zero).ShouldBeNull();

    [Fact]
    public void SendsTheCommandOfTheRowThatCameDue()
    {
        var dispatch = new RecordingDispatch();

        new TimerTask(dispatch, NullLogger<TimerTask>.Instance)
            .Tick(Context(Timers(Row("加速術/ME", 5))));

        dispatch.Sent.ShouldBe(["加速術"]);
    }

    [Fact]
    public void DoesNothingWhileTheMasterSwitchIsOff()
    {
        var dispatch = new RecordingDispatch();
        var settings = Timers(Row("加速術/ME", 5));
        settings.TimersEnabled = false;

        new TimerTask(dispatch, NullLogger<TimerTask>.Instance).Tick(Context(settings));

        dispatch.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingBeforeTheCharacterIsInTheWorld()
    {
        var dispatch = new RecordingDispatch();

        new TimerTask(dispatch, NullLogger<TimerTask>.Instance)
            .Tick(Context(Timers(Row("加速術/ME", 5)), inWorld: false));

        dispatch.Sent.ShouldBeEmpty();
    }

    // Marked as fired whatever came of it. A row whose item is not in the bag should wait
    // its interval before trying again rather than trying twice a second for an hour.
    [Fact]
    public void WaitsAfterARowThatCouldNotBeCarriedOut()
    {
        var dispatch = new RecordingDispatch { Result = DispatchResult.Skipped };
        var task = new TimerTask(dispatch, NullLogger<TimerTask>.Instance);
        var settings = Timers(Row("加速術/ME", 60));

        task.Tick(Context(settings));
        task.Tick(Context(settings));

        dispatch.Sent.Count.ShouldBe(1);
    }

    // The button beside each row, for a player who has just cast something by hand.
    [Fact]
    public void StartsARowsIntervalAgainWhenAsked()
    {
        var dispatch = new RecordingDispatch();
        var task = new TimerTask(dispatch, NullLogger<TimerTask>.Instance);

        task.Restart(0);
        task.Tick(Context(Timers(Row("加速術/ME", 60))));

        dispatch.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesARowThatIsNotThere() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerTask(new RecordingDispatch(), NullLogger<TimerTask>.Instance)
                .Restart(AuxSettings.Timers));

    [Fact]
    public void SaysTheFirstMessageStraightAway()
    {
        var actions = new RecordingActions();

        new ShoutTask(actions, NullLogger<ShoutTask>.Instance).Tick(Context(Shouting("one", "two")));

        actions.Said.ShouldBe(["one"]);
    }

    // Round-robin rather than repeating one, so a stall can list several things without
    // anyone reading the same line twice running.
    [Fact]
    public void MovesOnToTheNextMessage()
    {
        var task = new ShoutTask(new RecordingActions(), NullLogger<ShoutTask>.Instance);

        task.Next.ShouldBe(0);
        task.Tick(Context(Shouting("one", "two", "three")));
        task.Next.ShouldBe(1);
    }

    // And back to the start after the last one.
    [Fact]
    public void ComesBackToTheFirstMessageAfterTheLast()
    {
        var task = new ShoutTask(new RecordingActions(), NullLogger<ShoutTask>.Instance);

        task.Tick(Context(Shouting("only")));

        task.Next.ShouldBe(0);
    }

    [Fact]
    public void SaysNothingAgainUntilTheIntervalHasPassed()
    {
        var actions = new RecordingActions();
        var task = new ShoutTask(actions, NullLogger<ShoutTask>.Instance);
        var settings = Shouting("one", "two");

        task.Tick(Context(settings));
        task.Tick(Context(settings));
        task.Tick(Context(settings));

        actions.Said.ShouldBe(["one"]);
    }

    // On the ordinary channel despite the feature's name: a shout goes to the whole map and
    // gets an account muted.
    [Fact]
    public void SpeaksToWhoeverIsStandingThere()
    {
        var actions = new RecordingActions();

        new ShoutTask(actions, NullLogger<ShoutTask>.Instance).Tick(Context(Shouting("one")));

        actions.Channels.ShouldBe([ChatChannel.Normal]);
    }

    [Theory]
    [InlineData(false, 30)]
    [InlineData(true, 0)]
    public void DoesNothingWhenItIsOffOrHasNoInterval(bool enabled, uint interval)
    {
        var actions = new RecordingActions();
        var settings = Shouting("one");
        settings.ShoutEnabled = enabled;
        settings.ShoutIntervalSeconds = interval;

        new ShoutTask(actions, NullLogger<ShoutTask>.Instance).Tick(Context(settings));

        actions.Said.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingWithNothingToSay()
    {
        var actions = new RecordingActions();

        new ShoutTask(actions, NullLogger<ShoutTask>.Instance).Tick(Context(Shouting()));

        actions.Said.ShouldBeEmpty();
    }

    private static TimerRow[] Rows(params TimerRow[] rows) => rows;

    private static TimeSpan?[] Never() => new TimeSpan?[AuxSettings.Timers];

    private static TimerRow Row(string command, uint seconds, bool enabled = true) =>
        new() { Enabled = enabled, IntervalSeconds = seconds, Command = command };

    private static AuxSettings Timers(params TimerRow[] rows)
    {
        var settings = new AuxSettings { TimersEnabled = true };

        for (var i = 0; i < rows.Length; i++)
        {
            settings.TimerRows[i] = rows[i];
        }

        return settings;
    }

    private static AuxSettings Shouting(params string[] messages) =>
        new() { ShoutEnabled = true, ShoutIntervalSeconds = 30, ShoutMessages = [.. messages] };

    private StubContext Context(AuxSettings settings, bool inWorld = true) =>
        new(_process, settings, inWorld);

    /// <summary>A pass with nothing in the bag and a chosen world.</summary>
    private sealed class StubContext(RemoteProcess process, AuxSettings settings, bool inWorld)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        public override bool IsInWorld => inWorld;

        public override IReadOnlyList<InventoryItem> Bag => [];
    }

    /// <summary>Remembers what it was handed rather than touching a client.</summary>
    private sealed class RecordingDispatch()
        : HelperDispatch(
            new GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance),
            new Spells(LegacyTextCodec.Auto, NullLogger<Spells>.Instance),
            new EntityScan(LegacyTextCodec.Auto, NullLogger<EntityScan>.Instance),
            NullLogger<HelperDispatch>.Instance)
    {
        private readonly List<string> _sent = [];

        public DispatchResult Result { get; init; } = DispatchResult.Done;

        public List<string> Sent => _sent;

        public override DispatchResult Send(
            RemoteProcess process, HelperEntry entry, IReadOnlyList<InventoryItem> bag)
        {
            _sent.Add(entry.Name);

            return Result;
        }
    }

    /// <summary>Likewise.</summary>
    private sealed class RecordingActions()
        : GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance)
    {
        private readonly List<string> _said = [];
        private readonly List<ChatChannel> _channels = [];

        public IReadOnlyList<string> Said => _said;

        public IReadOnlyList<ChatChannel> Channels => _channels;

        public override void Say(RemoteProcess process, ChatChannel channel, string message)
        {
            _said.Add(message);
            _channels.Add(channel);
        }
    }
}
