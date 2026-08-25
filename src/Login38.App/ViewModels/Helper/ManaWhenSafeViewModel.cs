using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Aux.Settings;

namespace Login38.App.ViewModels.Helper;

/// <summary>
/// The rule for topping up mana only while it is safe to.
/// </summary>
/// <remarks>
/// Always read as percentages, whichever way the drinking rules above it are read: what
/// makes this safe is the proportion of hit points left, not how many there are.
/// </remarks>
public sealed partial class ManaWhenSafeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _enabled;

    /// <summary>Hit points must be at least this much of the maximum.</summary>
    [ObservableProperty]
    private double _hitPointsAtLeast;

    /// <summary>And mana at most this much.</summary>
    [ObservableProperty]
    private double _manaAtMost;

    [ObservableProperty]
    private string? _item;

    /// <summary>Reads the rule out of the settings.</summary>
    public void Load(ManaWhenSafeRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Enabled = rule.Enabled;
        HitPointsAtLeast = rule.HitPointsAtLeast;
        ManaAtMost = rule.ManaAtMost;
        Item = rule.Item;
    }

    /// <summary>Writes it back.</summary>
    public ManaWhenSafeRule ToRule() => new()
    {
        Enabled = Enabled,
        HitPointsAtLeast = HelperNumbers.Whole(HitPointsAtLeast, HelperNumbers.Percent),
        ManaAtMost = HelperNumbers.Whole(ManaAtMost, HelperNumbers.Percent),
        Item = Item?.Trim() ?? string.Empty,
    };
}
