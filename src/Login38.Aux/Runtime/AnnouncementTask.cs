using Login38.Aux.Actions;
using Login38.Aux.Game;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Says in the game's own chat window when the helper starts and stops.
/// </summary>
/// <remarks>
/// <para>
/// The player is looking at the client, not at the launcher, and everything the helper
/// does is silent by design. One green line is how they know Home was seen at all — and,
/// when the line does not come, that something went wrong before the character loaded.
/// </para>
/// <para>
/// Both ways round, because a switch with no feedback in one direction is a switch nobody
/// trusts. A player who presses Home twice by accident has to be told the second one
/// landed.
/// </para>
/// <para>
/// Said again for each character rather than once per launch. Leaving the world and coming
/// back is a new session as far as the player is concerned, and whether the helper is
/// still on is the first thing worth knowing.
/// </para>
/// </remarks>
public sealed class AnnouncementTask : IAuxTask
{
    /// <summary>What it says on the way in.</summary>
    internal const string Started = GameChat.GreenMark + "天堂喝水輔助啟動";

    /// <summary>And on the way out.</summary>
    internal const string Stopped = GameChat.GreenMark + "天堂喝水輔助關閉";

    private readonly GameChat _chat;
    private readonly HelperSwitch _switch;
    private readonly ILogger<AnnouncementTask> _logger;

    private bool _said;

    public AnnouncementTask(GameChat chat, HelperSwitch helperSwitch, ILogger<AnnouncementTask> logger)
    {
        _chat = chat;
        _switch = helperSwitch;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "announcement";

    /// <summary>Runs while the helper is off, because going off is what it reports.</summary>
    public bool RunsWhileOff => true;

    /// <summary>
    /// Twice a second.
    /// </summary>
    /// <remarks>
    /// Nobody is waiting on this to the tenth of a second, and every pass that finds the
    /// player in the world reads their gauges to see whether the client is ready to be
    /// written to.
    /// </remarks>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Nobody to tell. Forgotten rather than remembered, so that the next character to
        // load is told where the switch stands rather than inheriting the last one's line.
        if (!context.IsInWorld || !Ready(context.Player))
        {
            _said = false;

            return;
        }

        if (_said == _switch.IsOn)
        {
            return;
        }

        _said = _switch.IsOn;

        _logger.LogInformation("Told the player the helper is {State}", _said ? "running" : "stopped");

        _chat.Write(context.Process, _said ? Started : Stopped);
    }

    /// <summary>
    /// Whether the client is far enough into loading a character to be written to.
    /// </summary>
    /// <remarks>
    /// The client says it is in the world before the character's own numbers are there.
    /// A line written into that gap is a call into a routine whose window does not exist
    /// yet, which the reference guarded against in exactly this way.
    /// </remarks>
    internal static bool Ready(PlayerState player) =>
        player.HitPoints.Maximum > 0 && player.ManaPoints.Maximum > 0;
}
