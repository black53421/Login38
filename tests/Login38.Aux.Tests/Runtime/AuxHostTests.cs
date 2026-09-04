using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers the loop the helper features run on.
/// </summary>
/// <remarks>
/// One loop stands in for the ten threads the reference ran, so what matters is that it
/// keeps the promises those threads made individually: each task on its own cadence, one
/// bad task not taking the rest down, and stopping when there is nothing left to poll.
/// </remarks>
public sealed class AuxHostTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    // Waiting a cadence first would mean a game that is already up is acted on late, and
    // the reference's own loops did the work before the sleep for the same reason.
    [Fact]
    public async Task RunsTheFirstPassWithoutWaiting()
    {
        var task = new CountingTask("eager", AuxHost.Cadence);
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();
        await Host([task]).RunAsync(_process, cancellation.Token);

        task.Ticks.ShouldBe(1);
    }

    [Fact]
    public async Task RunsEveryTaskOnTheFirstPass()
    {
        var first = new CountingTask("first", AuxHost.Cadence);
        var second = new CountingTask("second", AuxHost.Cadence);

        await Run([first, second], passes: 1);

        first.Ticks.ShouldBeGreaterThan(0);
        second.Ticks.ShouldBeGreaterThan(0);
    }

    // The whole reason there is one loop rather than ten: a task that wants to run twice a
    // second must not be run ten times a second because something else does.
    [Fact]
    public async Task RunsASlowTaskLessOftenThanAFastOne()
    {
        var fast = new CountingTask("fast", AuxHost.Cadence);
        var slow = new CountingTask("slow", AuxHost.Cadence * 5);

        await Run([fast, slow], passes: 10);

        fast.Ticks.ShouldBeGreaterThan(slow.Ticks);
        slow.Ticks.ShouldBeGreaterThan(0);
    }

    // A task whose address has moved should cost that feature, not the rest of them.
    [Fact]
    public async Task KeepsGoingWhenATaskThrows()
    {
        var broken = new ThrowingTask();
        var working = new CountingTask("working", AuxHost.Cadence);

        await Run([broken, working], passes: 4);

        broken.Ticks.ShouldBeGreaterThan(1);
        working.Ticks.ShouldBeGreaterThan(1);
    }

    // A pass where nothing is due should read nothing out of the game at all.
    [Fact]
    public async Task ReadsNothingOnAPassWithNothingToDo()
    {
        var task = new CountingTask("rare", TimeSpan.FromMinutes(5));

        await Run([task], passes: 5);

        task.Ticks.ShouldBe(1);
    }

    // Every task on one pass sees the same reading. Separate threads each read their own,
    // so two rules could act on two different moments a few milliseconds apart.
    [Fact]
    public async Task GivesEveryTaskOnAPassTheSameContext()
    {
        var first = new ContextTask();
        var second = new ContextTask();

        await Run([first, second], passes: 1);

        first.Seen.ShouldNotBeNull();
        second.Seen.ShouldBeSameAs(first.Seen);
    }

    [Fact]
    public async Task StopsWhenTheLauncherCloses()
    {
        var task = new CountingTask("any", AuxHost.Cadence);
        using var cancellation = new CancellationTokenSource(AuxHost.Cadence * 3);

        await Host([task]).RunAsync(_process, cancellation.Token);

        task.Ticks.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task DoesNothingWithNoTasks() =>
        await Should.NotThrowAsync(() => Run([], passes: 2));

    // Quitting straight from the world is the ordinary way to stop playing, and it is the
    // one case where a task holding unsaved work never sees another pass.
    [Fact]
    public async Task TellsTasksTheyAreStopping()
    {
        var task = new ClosingTask();

        await Run([task], passes: 1);

        task.Stopped.ShouldBe(1);
    }

    [Fact]
    public async Task TellsSwitchAwareTasksWhenHelperTurnsOff()
    {
        var helperSwitch = Started();
        var turningOff = new TurningOffTask(helperSwitch);
        var switchAware = new SwitchOffTask();
        using var cancellation = new CancellationTokenSource(AuxHost.Cadence * 2.5);

        await Host([turningOff, switchAware], helperSwitch).RunAsync(_process, cancellation.Token);

        switchAware.SwitchOffs.ShouldBe(1);
        switchAware.Ticks.ShouldBe(0);
    }

    // A task that cannot write its file is not a reason for the next one to lose what it
    // was holding.
    [Fact]
    public async Task StopsTheRestWhenOneCannotFinishUp()
    {
        var broken = new ClosingTask { Throw = true };
        var working = new ClosingTask();

        await Run([broken, working], passes: 1);

        working.Stopped.ShouldBe(1);
    }

    // The first pass is immediate, so this allows for `passes` more of them. The half
    // cadence keeps a tick from landing exactly on the cancel and making the count a race.
    private async Task Run(IAuxTask[] tasks, int passes)
    {
        using var cancellation = new CancellationTokenSource(AuxHost.Cadence * (passes - 0.5));

        await Host(tasks).RunAsync(_process, cancellation.Token);
    }

    /// <summary>A host whose helper the player has started, which is what the tasks assume.</summary>
    private static AuxHost Host(IAuxTask[] tasks) => Host(tasks, Started());

    private static AuxHost Host(IAuxTask[] tasks, HelperSwitch helperSwitch) =>
        new(tasks, new AuxSettingsSource(), LegacyTextCodec.Auto, helperSwitch, NullLogger<AuxHost>.Instance);

    private static HelperSwitch Started()
    {
        var helperSwitch = new HelperSwitch();

        helperSwitch.Toggle();

        return helperSwitch;
    }

    private sealed class CountingTask : IAuxTask
    {
        public CountingTask(string name, TimeSpan interval)
        {
            Name = name;
            Interval = interval;
        }

        public string Name { get; }

        public TimeSpan Interval { get; }

        public int Ticks { get; private set; }

        public void Tick(AuxContext context) => Ticks++;
    }

    private sealed class ThrowingTask : IAuxTask
    {
        public string Name => "broken";

        public TimeSpan Interval => AuxHost.Cadence;

        public int Ticks { get; private set; }

        public void Tick(AuxContext context)
        {
            Ticks++;
            throw new GameProcessException("its address has moved");
        }
    }

    private sealed class TurningOffTask : IAuxTask
    {
        private readonly HelperSwitch _switch;
        private bool _done;

        public TurningOffTask(HelperSwitch helperSwitch) => _switch = helperSwitch;

        public string Name => "turn-off";

        public TimeSpan Interval => AuxHost.Cadence;

        public bool RunsWhileOff => true;

        public void Tick(AuxContext context)
        {
            if (_done)
            {
                return;
            }

            _done = true;
            _switch.Toggle();
        }
    }

    private sealed class SwitchOffTask : IAuxTask, IAuxTaskSwitchOff
    {
        public string Name => "switch-off";

        public TimeSpan Interval => AuxHost.Cadence;

        public int Ticks { get; private set; }

        public int SwitchOffs { get; private set; }

        public void Tick(AuxContext context) => Ticks++;

        public void SwitchedOff(RemoteProcess process) => SwitchOffs++;
    }

    private sealed class ClosingTask : IAuxTask, IAuxTaskShutdown
    {
        public string Name => "closing";

        public TimeSpan Interval => AuxHost.Cadence;

        public bool Throw { get; init; }

        public int Stopped { get; private set; }

        public void Tick(AuxContext context)
        {
        }

        public void Stopping()
        {
            Stopped++;

            if (Throw)
            {
                throw new IOException("the file is in use");
            }
        }
    }

    private sealed class ContextTask : IAuxTask
    {
        public string Name => "context";

        public TimeSpan Interval => AuxHost.Cadence;

        public AuxContext? Seen { get; private set; }

        public void Tick(AuxContext context) => Seen ??= context;
    }
}
