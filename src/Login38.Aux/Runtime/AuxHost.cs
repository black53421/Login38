using System.Diagnostics;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>One thing the helper does, on its own cadence.</summary>
public interface IAuxTask
{
    /// <summary>What it is called, for the log.</summary>
    string Name { get; }

    /// <summary>How often it wants to run.</summary>
    /// <remarks>
    /// Rounded up to the loop's own cadence, so this is "no more often than" rather than a
    /// promise. Nothing here needs to be exact — the fastest of them is a display that
    /// updates ten times a second.
    /// </remarks>
    TimeSpan Interval { get; }

    /// <summary>Does the work for one pass.</summary>
    /// <remarks>
    /// Called from the loop, so it must not block: everything else waits behind it. Reads
    /// of the game are cheap and there is nothing here that should wait on the network.
    /// </remarks>
    void Tick(AuxContext context);

    /// <summary>
    /// Whether this runs before the player has started the helper.
    /// </summary>
    /// <remarks>
    /// False for a feature, which is the point of the switch. True for the three things
    /// that are not features: the key that turns it on, the line in the chat window that
    /// says it went on, and the toggles — which have to be told to go back off, and cannot
    /// be if they are not being called.
    /// </remarks>
    bool RunsWhileOff => false;
}

/// <summary>A task with something to do once the loop has stopped.</summary>
/// <remarks>
/// Separate from <see cref="IAuxTask"/> because most tasks have nothing to do on the way
/// out: they read the game and act, and a game that has gone is simply nothing to act on.
/// The ones that implement this are the ones holding something the player would lose.
/// </remarks>
public interface IAuxTaskShutdown
{
    /// <summary>
    /// Called once after the last pass, whether the game exited or the launcher closed.
    /// </summary>
    /// <remarks>
    /// The game may already be gone, so this must not depend on reading it.
    /// </remarks>
    void Stopping();
}

/// <summary>
/// Runs the helper features while a game is up.
/// </summary>
/// <remarks>
/// <para>
/// One loop, not one thread per feature. The reference ran ten operating-system threads,
/// each with its own poll loop, its own sleep and its own copy of the settings lock — for
/// work that is a few memory reads and, occasionally, a packet. One loop that visits each
/// task when it is due does the same thing with one thread, one settings read per pass, and
/// one shared reading of the player and the bag.
/// </para>
/// <para>
/// It also gives shutdown and failure somewhere to live. A task that throws is logged and
/// skipped rather than taking its thread down silently, and the loop stops when the game
/// exits instead of polling a process that is not there.
/// </para>
/// </remarks>
public sealed class AuxHost
{
    /// <summary>How often the loop wakes up.</summary>
    /// <remarks>
    /// The shortest interval any task asks for. Everything slower is a multiple of it,
    /// counted rather than slept on separately.
    /// </remarks>
    public static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(100);

    private readonly IReadOnlyList<IAuxTask> _tasks;
    private readonly AuxSettingsSource _settings;
    private readonly ILegacyTextCodec _codec;
    private readonly HelperSwitch _switch;
    private readonly ILogger<AuxHost> _logger;

    public AuxHost(
        IEnumerable<IAuxTask> tasks,
        AuxSettingsSource settings,
        ILegacyTextCodec codec,
        HelperSwitch helperSwitch,
        ILogger<AuxHost> logger)
    {
        _tasks = [.. tasks];
        _settings = settings;
        _codec = codec;
        _switch = helperSwitch;
        _logger = logger;
    }

    /// <summary>Runs until the game exits or the launcher closes.</summary>
    public async Task RunAsync(RemoteProcess process, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (_tasks.Count == 0)
        {
            return;
        }

        var schedule = new TaskSchedule[_tasks.Count];

        for (var i = 0; i < _tasks.Count; i++)
        {
            schedule[i] = new TaskSchedule(_tasks[i]);
        }

        _logger.LogInformation(
            "Helper watching for Home; features on the switch: {Tasks}",
            string.Join(", ", _tasks.Select(t => t.Name)));

        using var timer = new PeriodicTimer(Cadence);
        var clock = Stopwatch.StartNew();

        try
        {
            // The first pass runs before the first wait, so a game that is already up is
            // acted on now rather than a cadence from now.
            do
            {
                if (!process.IsRunning)
                {
                    _logger.LogInformation("The game has exited; helper features stopped");
                    return;
                }

                RunDueTasks(schedule, process, clock.Elapsed);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Helper features stopped");
        }
        finally
        {
            StopTasks();
        }
    }

    /// <summary>
    /// Gives every task that wants one a chance to finish up.
    /// </summary>
    /// <remarks>
    /// In a finally, so it runs for a game that exited, a launcher that is closing and a
    /// loop that failed alike. Each is caught separately: one task that cannot write its
    /// file is not a reason for the next one to lose what it was holding.
    /// </remarks>
    private void StopTasks()
    {
        foreach (var task in _tasks.OfType<IAuxTaskShutdown>())
        {
            try
            {
                task.Stopping();
            }
            catch (Exception e) when (e is GameProcessException or InvalidOperationException or IOException)
            {
                _logger.LogWarning(e, "A helper feature could not finish up");
            }
        }
    }

    private void RunDueTasks(TaskSchedule[] schedule, RemoteProcess process, TimeSpan now)
    {
        AuxContext? context = null;

        foreach (var scheduled in schedule)
        {
            // Before the due test rather than after, so a feature's turn is not counted
            // as taken while the helper is off. The player presses Home and it acts on the
            // next pass rather than up to its own interval later.
            if (!_switch.IsOn && !scheduled.Task.RunsWhileOff)
            {
                continue;
            }

            if (now < scheduled.Due)
            {
                continue;
            }

            scheduled.Due = now + scheduled.Task.Interval;

            // Built on first use, so a pass where nothing is due reads nothing at all.
            context ??= new AuxContext(process, _settings.Current, _codec);

            try
            {
                scheduled.Task.Tick(context);
                scheduled.Reported = null;
            }
            catch (Exception e) when (e is GameProcessException or InvalidOperationException or IOException)
            {
                Report(scheduled, e);
            }
        }
    }

    /// <summary>
    /// Logs a task's failure once per spell of it.
    /// </summary>
    /// <remarks>
    /// These run for the length of a session. A task whose address has moved would otherwise
    /// write the same line ten times a second and bury everything else in the log.
    /// </remarks>
    private void Report(TaskSchedule scheduled, Exception e)
    {
        if (scheduled.Reported == e.Message)
        {
            return;
        }

        scheduled.Reported = e.Message;
        _logger.LogWarning(e, "{Task} could not run", scheduled.Task.Name);
    }

    private sealed class TaskSchedule
    {
        public TaskSchedule(IAuxTask task) => Task = task;

        public IAuxTask Task { get; }

        /// <summary>When it next wants to run. Zero, so everything runs on the first pass.</summary>
        public TimeSpan Due { get; set; }

        /// <summary>The last failure reported, so the same one is not reported twice.</summary>
        public string? Reported { get; set; }
    }
}
