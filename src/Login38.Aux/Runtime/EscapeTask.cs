using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Hunt;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Reads a teleport scroll when a fight has gone badly.
/// </summary>
/// <remarks>
/// <para>
/// One packet, whatever the scroll is. Where the character lands is decided between its
/// teleport control ring and the server; without the ring every scroll is random however it
/// is asked for, and with the ring the client puts up its own list. Neither is the
/// launcher's business, so it reads the scroll and stops there.
/// </para>
/// <para>
/// Separate from <see cref="PotionTask"/> and from the hunt because it is neither. Drinking
/// is a rule about a number; escaping is a decision to stop being where the character is,
/// and the hunt has no opinion about that — it will happily pick a new target in the new
/// room, which is usually the point.
/// </para>
/// </remarks>
public sealed class EscapeTask : IAuxTask
{
    private readonly GameActions _actions;
    private readonly HuntSwitch _hunting;
    private readonly ILogger<EscapeTask> _logger;

    /// <summary>
    /// Whether the health has been above the line since the last scroll.
    /// </summary>
    /// <remarks>
    /// The whole cooldown. Below the line the character stays below it for as long as it
    /// takes the server to move them, so a rule that only asked "are we low" would read the
    /// entire stack in the second before the teleport landed. Above the line again — which,
    /// once the scroll has worked, happens on its own — it arms itself.
    /// </remarks>
    private bool _armed = true;

    public EscapeTask(GameActions actions, HuntSwitch hunting, ILogger<EscapeTask> logger)
    {
        _actions = actions;
        _hunting = hunting;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "escape";

    /// <summary>Twice a second, which is how often the potions look at the same number.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rule = context.Settings.Hunt.Escape;

        if (rule is not { Ready: true } || !context.IsInWorld)
        {
            return;
        }

        var health = context.Player.HitPoints;

        if (health.Maximum == 0)
        {
            // A character the server has not finished describing. Acting on zero out of
            // zero would read a scroll on the way into the world.
            return;
        }

        if (health.Percent >= rule.HitPointsBelow)
        {
            _armed = true;

            return;
        }

        if (!_armed)
        {
            return;
        }

        if (InventoryReader.FindByName(context.Bag, rule.Item) is not { } scroll)
        {
            // Not disarmed: the player is out of scrolls, and the next pass is as good a
            // time as any to notice one has been picked up.
            _logger.LogInformation(
                "health is {Percent}% and it is time to read {Item}, but there is none in the bag",
                health.Percent, rule.Item);

            return;
        }

        _actions.UseTeleportScroll(context.Process, scroll.Param);

        // The server sends the offer after the scroll and waits for the answer before
        // moving anything. Sending it when nothing is pending is ignored.
        _actions.ConfirmTeleport(context.Process);

        // Off, and off in the settings rather than only in this object, because the hunt
        // reads that flag on every pass and would otherwise pick a target in the room the
        // character has just landed in. A character that had to run is not a character that
        // should carry on hunting somewhere else — which is the whole difference between this
        // and RelocateTask, and is why they are two tasks.
        context.Settings.Hunt.Enabled = false;
        _hunting.TurnOff();

        _logger.LogInformation(
            "health is {Percent}%; read {Item} and turned the hunt off",
            health.Percent, rule.Item);

        _armed = false;
    }
}
