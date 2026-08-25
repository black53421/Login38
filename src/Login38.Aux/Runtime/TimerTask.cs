using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Settings;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Runs a command every so often.
/// </summary>
/// <remarks>
/// <para>
/// Six rows, each a command and an interval. One row per pass at most: the client casts one
/// skill at a time and the server rate-limits, so firing three rows that all came due
/// together would throw two of them away and look like the timers not working.
/// </para>
/// <para>
/// Timed against the loop's own clock rather than the wall, so changing the machine's time
/// zone or the clock drifting does not fire everything at once.
/// </para>
/// </remarks>
public sealed class TimerTask : IAuxTask
{
    private readonly HelperDispatch _dispatch;
    private readonly ILogger<TimerTask> _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan?[] _lastFired = new TimeSpan?[AuxSettings.Timers];

    public TimerTask(HelperDispatch dispatch, ILogger<TimerTask> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "timers";

    /// <summary>
    /// Twice a second.
    /// </summary>
    /// <remarks>
    /// Intervals here are whole seconds, so this is twice as often as the shortest one can
    /// be — close enough that a row set to one second is not systematically late.
    /// </remarks>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Starts a row's interval again from now.
    /// </summary>
    /// <remarks>
    /// For the button beside each row in the helper window. A player who has just cast
    /// something by hand presses it so the timer does not immediately cast it again.
    /// </remarks>
    public void Restart(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, AuxSettings.Timers);

        _lastFired[row] = _clock.Elapsed;
    }

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.Settings;

        if (!settings.TimersEnabled || !context.IsInWorld)
        {
            return;
        }

        if (Due(settings.TimerRows, _lastFired, _clock.Elapsed) is not { } row)
        {
            return;
        }

        var command = settings.TimerRows[row].Command;

        _logger.LogInformation(
            "Timer {Row} is due: {Command} (every {Interval}s)",
            row, command, settings.TimerRows[row].IntervalSeconds);

        _dispatch.Send(context.Process, HelperEntrySyntax.Parse(command), context.Bag);

        // Marked as fired whatever came of it. A row whose item is not in the bag should
        // wait its interval before trying again rather than trying every pass.
        _lastFired[row] = _clock.Elapsed;
    }

    /// <summary>
    /// The first row that wants to run.
    /// </summary>
    /// <remarks>
    /// A row that has never fired is due immediately, so switching a timer on does the
    /// thing rather than waiting out its first interval.
    /// </remarks>
    internal static int? Due(TimerRow[] rows, TimeSpan?[] lastFired, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(lastFired);

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];

            if (!row.Enabled || string.IsNullOrWhiteSpace(row.Command))
            {
                continue;
            }

            if (lastFired[i] is not { } last || now - last >= TimeSpan.FromSeconds(row.IntervalSeconds))
            {
                return i;
            }
        }

        return null;
    }
}
