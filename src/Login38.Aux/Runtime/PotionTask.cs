using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>
/// Drinks when the player is running low.
/// </summary>
/// <remarks>
/// <para>
/// Seven rules in order, and the first one whose threshold has been crossed wins. Order is
/// the player's: they put the cheap potion above the expensive one so the expensive one is
/// only reached when the cheap one has not been enough.
/// </para>
/// <para>
/// Then one more rule for mana, which is separate because mana potions in this game cost
/// hit points. Drinking one at the wrong moment is worse than being out of mana, so it
/// asks two questions rather than one: enough health, and low enough mana.
/// </para>
/// </remarks>
public sealed class PotionTask : IAuxTask
{
    private readonly GameActions _actions;
    private readonly Spells _spells;
    private readonly ActionArbiter _arbiter;
    private readonly ILogger<PotionTask> _logger;
    private readonly object _sync = new();
    private readonly Func<TimeSpan> _now;
    private readonly Func<RemoteProcess, uint?> _selfId;

    private TimeSpan _nextActionAt;

    public PotionTask(GameActions actions, Spells spells, ILogger<PotionTask> logger)
        : this(actions, spells, logger, MonotonicNow(), ReadSelfId, new ActionArbiter())
    {
    }

    public PotionTask(
        GameActions actions, Spells spells, ActionArbiter arbiter, ILogger<PotionTask> logger)
        : this(actions, spells, logger, MonotonicNow(), ReadSelfId, arbiter)
    {
    }

    internal PotionTask(
        GameActions actions, Spells spells, ILogger<PotionTask> logger, Func<TimeSpan> now,
        Func<RemoteProcess, uint?>? selfId = null, ActionArbiter? arbiter = null)
    {
        _actions = actions;
        _spells = spells;
        _arbiter = arbiter ?? new ActionArbiter();
        _logger = logger;
        _now = now ?? throw new ArgumentNullException(nameof(now));
        _selfId = selfId ?? ReadSelfId;
    }

    /// <inheritdoc/>
    public string Name => "potions";

    /// <summary>Checks on every host pass so a hotkey wake must yield to an urgent potion.</summary>
    /// <remarks>
    /// With no event wake the host itself still runs at its normal 100 ms cadence, so this
    /// does not turn ordinary potion polling into a busy loop.
    /// </remarks>
    public TimeSpan Interval => TimeSpan.Zero;

    /// <summary>Minimum spacing between two potion-rule actions.</summary>
    internal static readonly TimeSpan ActionCooldown = TimeSpan.FromMilliseconds(500);

    /// <summary>Monotonic task time.</summary>
    internal TimeSpan Now => _now();

    private static Func<TimeSpan> MonotonicNow()
    {
        var clock = Stopwatch.StartNew();
        return () => clock.Elapsed;
    }

    /// <inheritdoc/>
    public void Tick(AuxContext context) => TryAct(context);

    /// <summary>
    /// Checks and, when needed, performs one recovery action. Safe to call from the fast
    /// hotkey worker as well as the normal helper loop.
    /// </summary>
    internal bool TryAct(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            return TryActLocked(context);
        }
    }

    private bool TryActLocked(AuxContext context)
    {
        var settings = context.Settings;
        var rules = settings.Potions.Where(Wanted).ToList();
        var mana = settings.ManaWhenSafe;
        var wantsMana = mana.Enabled && !string.IsNullOrWhiteSpace(mana.Item);

        if (rules.Count == 0 && !wantsMana)
        {
            return false;
        }

        if (!context.IsInWorld)
        {
            return false;
        }

        var player = context.Player;

        if (player.HitPoints.Maximum == 0)
        {
            // A character who has just arrived, before the server has said how much health
            // they have. Acting on zero out of zero would drink everything they own.
            return false;
        }

        var now = Now;

        // Detection stays fast, but actions remain paced independently. The player's gauges
        // are read on every poll so the first pass after the cooldown expires acts on fresh
        // state rather than on a decision queued half a second earlier.
        if (now < _nextActionAt)
        {
            return false;
        }

        foreach (var rule in rules)
        {
            if (!Crossed(settings, player.HitPoints, rule.Threshold))
            {
                continue;
            }

            using var priority = _arbiter.WithPriority(PlayerActionPriority.Potion);

            if (Drink(context, rule.Item))
            {
                context.PotionActionTaken = true;
                _nextActionAt = now + ActionCooldown;
                return true;
            }
        }

        if (wantsMana && Safe(settings, player))
        {
            using var priority = _arbiter.WithPriority(PlayerActionPriority.Potion);

            if (Drink(context, mana.Item))
            {
                context.PotionActionTaken = true;
                _nextActionAt = now + ActionCooldown;
                return true;
            }
        }

        return false;
    }

    private static bool Wanted(PotionRow row) => row.Enabled && !string.IsNullOrWhiteSpace(row.Item);

    /// <summary>Whether a gauge has fallen below what the player asked for.</summary>
    /// <remarks>
    /// The threshold is a percentage or an absolute amount depending on one switch that
    /// applies to every rule at once, which is the client's own arrangement.
    /// </remarks>
    private static bool Crossed(AuxSettings settings, Gauge gauge, uint threshold) =>
        settings.PotionUsePercent ? gauge.Percent < threshold : gauge.Current < threshold;

    /// <summary>Whether it is safe to pay hit points for mana.</summary>
    private static bool Safe(AuxSettings settings, PlayerState player)
    {
        var rule = settings.ManaWhenSafe;

        return settings.PotionUsePercent
            ? player.HitPoints.Percent >= rule.HitPointsAtLeast && player.ManaPoints.Percent <= rule.ManaAtMost
            : player.HitPoints.Current >= rule.HitPointsAtLeast && player.ManaPoints.Current <= rule.ManaAtMost;
    }

    /// <summary>
    /// Carries out one rule, which is an item to use or a skill to cast on oneself.
    /// </summary>
    /// <remarks>
    /// Deliberately not the shared dispatcher, and the difference is the item path: this
    /// sends the packet, where the dispatcher calls the client's own item routine. Drinking
    /// happens while a character is being hit, several times in a row, and the packet does
    /// not depend on any client-side state being intact at the moment it arrives.
    /// </remarks>
    private bool Drink(AuxContext context, string text)
    {
        var entry = HelperEntrySyntax.Parse(text);

        return entry.Kind switch
        {
            EntryKind.Item => Use(context, entry),
            EntryKind.Skill => Cast(context, entry),
            _ => Unsupported(text),
        };
    }

    private bool Unsupported(string text)
    {
        _logger.LogInformation("{Text} is not something a potion rule can use", text);
        return false;
    }

    private bool Use(AuxContext context, HelperEntry entry)
    {
        if (InventoryReader.FindByName(context.Bag, entry.Name) is not { } item)
        {
            _logger.LogInformation(
                "Time to use {Name}, but nothing in the bag of {Count} is called that",
                entry.Name, context.Bag.Count);

            return false;
        }

        switch (entry.Cast.Kind)
        {
            case CastKind.Item:
                _actions.SendUseItem(context.Process, item.Param);
                return true;

            case CastKind.OnSelfItem:
                if (_selfId(context.Process) is not { } selfId || selfId == 0)
                {
                    _logger.LogInformation(
                        "Time to use {Name} on self, but the character object id is not ready",
                        entry.Name);

                    return false;
                }

                _actions.UseOn(context.Process, item.Param, selfId);
                return true;

            default:
                _logger.LogInformation(
                    "{Name} is aimed somewhere a potion rule cannot follow; only /I and /IME work here",
                    entry.Name);

                return false;
        }
    }

    private static uint? ReadSelfId(RemoteProcess process) =>
        process.TryRead<uint>(GameFunctions.SelfId, out var id) && id != 0 ? id : null;

    private bool Cast(AuxContext context, HelperEntry entry)
    {
        // Only the two ways of casting on oneself. A potion rule that fired at whatever the
        // mouse happened to be over would be a rule that healed a monster.
        var aim = entry.Cast.Kind switch
        {
            CastKind.OnSelf => SkillTarget.Self,
            CastKind.NoSpec => SkillTarget.Whatever,
            _ => (SkillTarget?)null,
        };

        if (aim is null)
        {
            _logger.LogInformation(
                "{Name} is aimed somewhere a potion rule cannot follow; only /M and /ME work here",
                entry.Name);

            return false;
        }

        if (_spells.Find(context.Process, entry.Name) is not { } packed)
        {
            _logger.LogInformation("Time to cast {Name}, but this character has not learned it", entry.Name);

            return false;
        }

        _actions.Cast(context.Process, packed, aim.Value);
        return true;
    }
}
