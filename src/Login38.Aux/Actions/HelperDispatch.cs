using System.Linq;
using System.Globalization;
using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Actions;

/// <summary>What came of trying to carry out one helper entry.</summary>
public enum DispatchResult
{
    /// <summary>It was sent.</summary>
    Done,

    /// <summary>It was sent, and it was a cast.</summary>
    /// <remarks>
    /// Worth telling apart because the client casts one skill at a time: a caller working
    /// through a list has to stop at the first one rather than sending five the client will
    /// throw away.
    /// </remarks>
    Cast,

    /// <summary>Nothing was sent, and why is in the log.</summary>
    /// <remarks>
    /// Not a failure. The usual reasons are that the item is not in the bag or the skill has
    /// not been learned, both of which are ordinary while a player is playing.
    /// </remarks>
    Skipped,
}

/// <summary>
/// Carries out one line of a player's settings.
/// </summary>
/// <remarks>
/// <para>
/// A helper entry is a name and a suffix — <c>紅色藥水</c>, <c>加速術/ME</c>,
/// <c>祝福的卷軸/IW</c> — and the suffix decides which of a dozen quite different things
/// happens. Everything that reads those entries goes through here: the potion rows, the
/// timers, the buff list, the status page.
/// </para>
/// <para>
/// Nothing here gates on time or on state. A caller that wants a cooldown keeps its own,
/// because what a sensible cooldown is differs per feature — a potion's is half a second
/// and a buff's is two.
/// </para>
/// </remarks>
public class HelperDispatch
{
    private readonly GameActions _actions;
    private readonly Spells _spells;
    private readonly EntityScan _entities;
    private readonly ILogger<HelperDispatch> _logger;
    private readonly HashSet<string> _said = [];

    public HelperDispatch(
        GameActions actions, Spells spells, EntityScan entities, ILogger<HelperDispatch> logger)
    {
        _actions = actions;
        _spells = spells;
        _entities = entities;
        _logger = logger;
    }

    /// <summary>Carries out one entry against a game that is in the world.</summary>
    public virtual DispatchResult Send(
        RemoteProcess process, HelperEntry entry, IReadOnlyList<InventoryItem> bag)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(bag);

        var result = entry.Kind switch
        {
            EntryKind.Item => SendItem(process, entry, bag),
            EntryKind.Skill => SendSkill(process, entry, bag),

            // The function keys belong to the status page's own macro system, which is
            // driven by the player pressing them. Nothing on a timer should fire one.
            _ => Skip(entry, "it is a key macro, which only a keypress fires"),
        };

        if (result != DispatchResult.Skipped)
        {
            // Whatever was wrong with this entry is no longer wrong, so the next time
            // something is, it gets said.
            Worked(entry);
        }

        return result;
    }

    private DispatchResult SendItem(
        RemoteProcess process, HelperEntry entry, IReadOnlyList<InventoryItem> bag)
    {
        // Not a packet at all: it puts the player's condition in the log, for somebody
        // working out why one of their rules is not firing.
        if (entry.Cast.Kind == CastKind.Info)
        {
            try
            {
                var player = PlayerStateReader.Read(process);

                _logger.LogInformation(
                    "HP {Hp}/{MaxHp}  MP {Mp}/{MaxMp}  food {Food}%  weight {Weight}%  map {Map}",
                    player.HitPoints.Current, player.HitPoints.Maximum,
                    player.ManaPoints.Current, player.ManaPoints.Maximum,
                    player.FoodPercent, player.WeightPercent, player.MapId);
            }
            catch (GameProcessException e)
            {
                // Asked for at a moment when the numbers are not there. Saying so is the
                // whole point of this entry; failing the pass for it would not be.
                _logger.LogInformation(e, "The player's condition could not be read");
            }

            return DispatchResult.Done;
        }

        if (InventoryReader.FindByName(bag, entry.Name) is not { } source)
        {
            // Names, not a count. "Nothing in the bag of 15 is called that" cannot tell an
            // operator whether the item is absent, spelled differently in their file, or
            // read out of the client wrongly — and those need three different fixes.
            return Skip(entry, $"not in the bag. The bag holds: {Names(bag)}");
        }

        // "Use this on that" — a scroll on a weapon, a whetstone on what is being swung.
        // The server wants both, and using the scroll on its own only opens a target
        // cursor nobody is there to click.
        if (entry.Cast.Kind is CastKind.OnSelfItem or CastKind.OnInUseItem
            or CastKind.OnWieldedItem or CastKind.OnNamedItem or CastKind.OnNamedEntity)
        {
            return UseOnSomething(process, entry, bag, source);
        }

        if (entry.Cast.Kind == CastKind.DropItem)
        {
            // Throwing away by name is spelled the same as using, and the client's own drop
            // routine has not been found. Using it is what the reference falls back to, and
            // it is at least the thing the player asked for by name.
            _logger.LogInformation(
                "{Name}: throwing away is not implemented; using it instead", entry.Name);
        }

        _actions.UseItem(process, source.Entry);

        return DispatchResult.Done;
    }

    /// <summary>Sends "use this on that", once there is a that.</summary>
    private DispatchResult UseOnSomething(
        RemoteProcess process, HelperEntry entry, IReadOnlyList<InventoryItem> bag, InventoryItem source)
    {
        var target = TargetOf(process, entry, bag);

        if (target is null)
        {
            return Skip(entry, "there is nothing to use it on");
        }

        _actions.UseOn(process, source.Param, target.Value);

        return DispatchResult.Done;
    }

    /// <summary>What an entry's suffix says to aim at.</summary>
    private uint? TargetOf(RemoteProcess process, HelperEntry entry, IReadOnlyList<InventoryItem> bag) =>
        entry.Cast.Kind switch
        {
            CastKind.OnSelfItem => SelfId(process),
            CastKind.OnInUseItem => Worn(bag, entry.Cast.Target, i => i.IsInUse)?.Param,
            CastKind.OnWieldedItem => Worn(bag, entry.Cast.Target, i => i.IsWielded)?.Param,
            CastKind.OnNamedItem => Named(bag, entry.Cast.Target)?.Param,

            // Somebody else, by name. Nothing in the client maps names to objects, so this
            // is a walk of its heap — a second or so, and the only entry that costs one.
            CastKind.OnNamedEntity => Somebody(process, entry.Cast.Target),
            _ => null,
        };

    /// <summary>The id of whoever is called this, if anyone in sight is.</summary>
    private uint? Somebody(RemoteProcess process, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_entities.Find(process, name) is not { } entity)
        {
            return null;
        }

        _logger.LogInformation(
            "{Name} is at {Address}, id {Id:X8}", entity.Name, entity.Address, entity.Id);

        return entity.Id;
    }

    /// <summary>
    /// The first thing being worn or swung, optionally by name as well.
    /// </summary>
    /// <remarks>
    /// The name is a filter on top of the state, not instead of it: <c>/IW=銀劍</c> means
    /// "the silver sword, and only while it is the one being swung".
    /// </remarks>
    private static InventoryItem? Worn(
        IReadOnlyList<InventoryItem> bag, string? name, Func<InventoryItem, bool> state) =>
        InventoryReader.Find(bag, item => state(item) && (name is null || ItemNames.Match(item.Name, name)));

    private static InventoryItem? Named(IReadOnlyList<InventoryItem> bag, string? name) =>
        name is null ? null : InventoryReader.FindByName(bag, name);

    /// <summary>The player's own object id, which the client fills in on entering.</summary>
    private static uint? SelfId(RemoteProcess process) =>
        process.TryRead<uint>(GameFunctions.SelfId, out var id) && id != 0 ? id : null;

    private DispatchResult SendSkill(
        RemoteProcess process, HelperEntry entry, IReadOnlyList<InventoryItem> bag)
    {
        if (_spells.Find(process, entry.Name) is not { } packed)
        {
            _logger.LogInformation(
                "{Name} is not a skill this character knows. Did you mean {Near}?",
                entry.Name, string.Join(", ", _spells.Near(entry.Name)));

            return DispatchResult.Skipped;
        }

        var aim = AimOf(entry, bag);

        if (aim is null)
        {
            return Skip(entry, "there is nothing to cast it at");
        }

        _actions.Cast(process, packed, aim.Value);

        return DispatchResult.Cast;
    }

    private static SkillTarget? AimOf(HelperEntry entry, IReadOnlyList<InventoryItem> bag) =>
        entry.Cast.Kind switch
        {
            CastKind.OnSelf => SkillTarget.Self,

            // Nothing in this client writes a "what the mouse is over" global that a cast
            // can read, so aiming at the cursor is the same as not aiming: the server works
            // it out from what the session is doing. Sending a target it cannot resolve
            // gets an error back that stops the cast entirely.
            CastKind.NoSpec or CastKind.HoverTarget => SkillTarget.Whatever,

            CastKind.OnInUseItem => Item(Worn(bag, entry.Cast.Target, i => i.IsInUse)),
            CastKind.OnWieldedItem => Item(Worn(bag, entry.Cast.Target, i => i.IsWielded)),
            CastKind.OnNamedItem => Item(Named(bag, entry.Cast.Target)),
            _ => null,
        };

    private static SkillTarget? Item(InventoryItem? item) =>
        item is { } found ? SkillTarget.Item(found.Param) : null;

    /// <summary>The bag as an operator would read it, for a line that says what is there.</summary>
    /// <remarks>
    /// Cut off, because a full bag is a hundred and eighty of these and the point is to be
    /// readable. What is shown is enough to tell a spelling problem from an absent item.
    /// </remarks>
    private static string Names(IReadOnlyList<InventoryItem> bag)
    {
        const int Most = 30;

        if (bag.Count == 0)
        {
            return "(nothing)";
        }

        var shown = string.Join("、", bag.Take(Most).Select(i => i.Name));

        return bag.Count > Most
            ? string.Create(CultureInfo.InvariantCulture, $"{shown} …and {bag.Count - Most} more")
            : shown;
    }

    /// <summary>
    /// Nothing was sent, said once rather than on every pass.
    /// </summary>
    /// <remarks>
    /// Once per entry per spell of it, and cleared the moment that entry works. The
    /// alternative — and what the reference did — was to put the entry on its full cooldown
    /// when it was skipped, with the comment that this was to stop the log filling up. It
    /// worked, and it cost five seconds every time the reason for the skip was "not yet":
    /// an item still being read into the bag, or a character whose effect table had not
    /// settled. The player pressed the switch and watched nothing happen.
    /// </remarks>
    private DispatchResult Skip(HelperEntry entry, string why)
    {
        if (_said.Add(entry.Name + "\u0000" + why))
        {
            _logger.LogInformation("{Name}: {Why}", entry.Name, why);
        }

        return DispatchResult.Skipped;
    }

    /// <summary>Forgets what was said about an entry, so the next problem is reported.</summary>
    private void Worked(HelperEntry entry) =>
        _said.RemoveWhere(said => said.StartsWith(entry.Name + "\u0000", StringComparison.Ordinal));
}
