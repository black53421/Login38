using Login38.Aux.Actions;
using Login38.Aux.Game;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Runtime;

/// <summary>What to do with something the player does not want.</summary>
public enum Disposal
{
    /// <summary>Destroy it outright.</summary>
    Destroy,

    /// <summary>Use a solvent on it, which is worth a little and destroys it too.</summary>
    Dissolve,
}

/// <summary>
/// Clears junk out of the bag.
/// </summary>
/// <remarks>
/// <para>
/// Two lists a player fills in: things to destroy, and things to dissolve. A character
/// killing monsters for hours fills a bag with what those monsters drop, and a full bag
/// stops them picking up anything worth having.
/// </para>
/// <para>
/// This is the one feature here that can lose something the player wanted. Two guards
/// against that, both from the reference and both kept: anything the client marks as worn
/// or being swung is never touched however it is spelled in the list, and a name matches
/// only in full — no prefixes, no "contains".
/// </para>
/// </remarks>
public sealed class DeleteTask : IAuxTask
{
    /// <summary>What a solvent is called, in either script.</summary>
    /// <remarks>
    /// Matched as a prefix because the client appends a stack count and sometimes a grade.
    /// </remarks>
    internal static readonly string[] SolventNames = ["溶解劑", "溶解剂"];

    private readonly GameActions _actions;
    private readonly ILogger<DeleteTask> _logger;

    public DeleteTask(GameActions actions, ILogger<DeleteTask> logger)
    {
        _actions = actions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "delete";

    /// <summary>Twice a second, which clears a bag full of junk in a few seconds.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.Settings;

        if (!settings.DeleteEnabled || (settings.DeleteList.Count == 0 && settings.DissolveList.Count == 0))
        {
            return;
        }

        if (!context.IsInWorld)
        {
            return;
        }

        if (Pick(settings.DeleteList, settings.DissolveList, context.Bag) is not { } chosen)
        {
            return;
        }

        var (how, item) = chosen;

        if (how == Disposal.Destroy)
        {
            _logger.LogInformation("Destroying {Item} ({Count})", item.Name, item.Count);

            _actions.Drop(context.Process, item.Param, item.Count);

            return;
        }

        if (Solvent(context.Bag) is not { } solvent)
        {
            _logger.LogInformation(
                "{Item} is to be dissolved, but there is no 溶解劑 in the bag", item.Name);

            return;
        }

        _logger.LogInformation("Dissolving {Item} with {Solvent}", item.Name, solvent.Name);

        _actions.UseOn(context.Process, solvent.Param, item.Param);
    }

    /// <summary>
    /// The first thing in the bag either list asks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Destroying is looked for before dissolving, so a name in both lists is destroyed —
    /// the cheaper action, and the one that cannot fail for want of a solvent.
    /// </para>
    /// <para>
    /// One item per pass. The item that comes back is the one that will be acted on, rather
    /// than a name to look up again: two stacks of the same thing are two different items,
    /// and finding it twice can find the other one.
    /// </para>
    /// </remarks>
    internal static (Disposal How, InventoryItem Item)? Pick(
        IReadOnlyList<string> destroy, IReadOnlyList<string> dissolve, IReadOnlyList<InventoryItem> bag)
    {
        ArgumentNullException.ThrowIfNull(destroy);
        ArgumentNullException.ThrowIfNull(dissolve);
        ArgumentNullException.ThrowIfNull(bag);

        foreach (var wanted in destroy)
        {
            if (InBag(bag, wanted) is { } item)
            {
                return (Disposal.Destroy, item);
            }
        }

        foreach (var wanted in dissolve)
        {
            if (InBag(bag, wanted) is { } item)
            {
                return (Disposal.Dissolve, item);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an item safe to get rid of, by the name the player wrote.
    /// </summary>
    /// <remarks>
    /// The stack count comes off both sides, because a player writing a list writes the
    /// name and the client shows <c>藥水 (12)</c>. What does not come off is the worn or
    /// wielded mark: an item carrying one is not a candidate at all, whatever it is called.
    /// </remarks>
    private static InventoryItem? InBag(IReadOnlyList<InventoryItem> bag, string wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return null;
        }

        var needle = ItemNames.StripQuantity(wanted.Trim());

        foreach (var item in bag)
        {
            if (item.IsInUse || item.IsWielded)
            {
                continue;
            }

            if (string.Equals(ItemNames.StripQuantity(item.Name), needle, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>The first solvent in the bag.</summary>
    private static InventoryItem? Solvent(IReadOnlyList<InventoryItem> bag)
    {
        foreach (var item in bag)
        {
            var name = ItemNames.StripQuantity(item.Name);

            if (SolventNames.Any(solvent => name.StartsWith(solvent, StringComparison.Ordinal)))
            {
                return item;
            }
        }

        return null;
    }
}
