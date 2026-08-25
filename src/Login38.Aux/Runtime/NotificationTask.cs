using System.Diagnostics;
using Login38.Aux.Notifications;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Shows what was picked up and what the last kill was worth.
/// </summary>
/// <remarks>
/// <para>
/// The server tells the client both of these and the client draws neither. This keeps the
/// detour that catches them in the game, reads what it caught, and keeps the board of what
/// is currently on screen — which is what the overlay draws from.
/// </para>
/// <para>
/// Two switches, and one detour behind them. Turning a switch off drops that kind on the
/// way in rather than taking the detour out: the server sends the packet regardless, and
/// putting a branch back into the client's dispatcher every time a player changes their
/// mind is the more expensive of the two.
/// </para>
/// </remarks>
public sealed class NotificationTask : IAuxTask, IAuxTaskShutdown
{
    private readonly NotificationHook _hook;
    private readonly NotificationBoard _board;
    private readonly ILogger<NotificationTask> _logger;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private RemoteProcess? _game;
    private bool _reported;

    public NotificationTask(
        NotificationHook hook, NotificationBoard board, ILogger<NotificationTask> logger)
    {
        _hook = hook;
        _board = board;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "notifications";

    /// <summary>Every pass of the loop.</summary>
    /// <remarks>
    /// The fastest anything here runs, because a pickup that appears a tenth of a second
    /// after it happened reads as laggy. Drawing is not on this clock: the overlay works
    /// out the fade from when each thing arrived, so it stays smooth between passes.
    /// </remarks>
    public TimeSpan Interval => AuxHost.Cadence;

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var misc = context.Settings.Misc;

        try
        {
            if (!misc.PickupToast && !misc.GainDrift)
            {
                Stop(context.Process);

                return;
            }

            _game = context.Process;

            if (!context.IsInWorld)
            {
                // Between characters the board holds the last one's pickups, which would
                // come back on screen the moment the next one entered the world.
                _board.Clear();

                return;
            }

            _hook.Install(context.Process);

            foreach (var caught in _hook.Drain(context.Process))
            {
                if (Wanted(caught, misc.PickupToast, misc.GainDrift))
                {
                    _board.Push(caught, _clock.Elapsed);
                }
            }

            _board.Tick(_clock.Elapsed);
            _reported = false;
        }
        catch (GameProcessException e)
        {
            if (!_reported)
            {
                _reported = true;
                _logger.LogWarning(e, "{Task} could not be applied", Name);
            }
        }
    }

    /// <inheritdoc/>
    public void Stopping()
    {
        if (_game is { IsRunning: true } game)
        {
            try
            {
                Stop(game);
            }
            catch (GameProcessException e)
            {
                _logger.LogWarning(e, "{Task} could not be taken out of the game", Name);
            }
        }
        else
        {
            _hook.Forget();
        }

        _board.Clear();
        _game = null;
    }

    /// <summary>Whether the player asked to see this kind.</summary>
    internal static bool Wanted(Notification notification, bool toasts, bool drifts) =>
        notification switch
        {
            Notification.Toast => toasts,
            Notification.Drift => drifts,
            _ => false,
        };

    private void Stop(RemoteProcess process)
    {
        _hook.Remove(process);
        _board.Clear();
    }
}
