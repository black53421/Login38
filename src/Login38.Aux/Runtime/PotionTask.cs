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
    private readonly ILogger<PotionTask> _logger;

    public PotionTask(GameActions actions, Spells spells, ILogger<PotionTask> logger)
    {
        _actions = actions;
        _spells = spells;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "potions";

    /// <summary>Twice a second, which is as fast as the client will take a potion anyway.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.Settings;
        var rules = settings.Potions.Where(Wanted).ToList();
        var mana = settings.ManaWhenSafe;
        var wantsMana = mana.Enabled && !string.IsNullOrWhiteSpace(mana.Item);

        if (rules.Count == 0 && !wantsMana)
        {
            return;
        }

        if (!context.IsInWorld)
        {
            return;
        }

        var player = context.Player;

        if (player.HitPoints.Maximum == 0)
        {
            // A character who has just arrived, before the server has said how much health
            // they have. Acting on zero out of zero would drink everything they own.
            return;
        }

        foreach (var rule in rules)
        {
            if (Crossed(settings, player.HitPoints, rule.Threshold))
            {
                Drink(context, rule.Item);
            }
        }

        if (wantsMana && Safe(settings, player))
        {
            Drink(context, mana.Item);
        }
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
    private void Drink(AuxContext context, string text)
    {
        var entry = HelperEntrySyntax.Parse(text);

        switch (entry.Kind)
        {
            case EntryKind.Item:
                Use(context, entry);
                break;

            case EntryKind.Skill:
                Cast(context, entry);
                break;

            default:
                _logger.LogInformation("{Text} is not something a potion rule can use", text);
                break;
        }
    }

    private void Use(AuxContext context, HelperEntry entry)
    {
        if (InventoryReader.FindByName(context.Bag, entry.Name) is not { } item)
        {
            _logger.LogInformation(
                "Time to use {Name}, but nothing in the bag of {Count} is called that",
                entry.Name, context.Bag.Count);

            return;
        }

        _actions.SendUseItem(context.Process, item.Param);
    }

    private void Cast(AuxContext context, HelperEntry entry)
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

            return;
        }

        if (_spells.Find(context.Process, entry.Name) is not { } packed)
        {
            _logger.LogInformation("Time to cast {Name}, but this character has not learned it", entry.Name);

            return;
        }

        _actions.Cast(context.Process, packed, aim.Value);
    }
}
