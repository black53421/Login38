using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Aux.Settings;

namespace Login38.App.ViewModels.Helper;

/// <summary>One drinking rule: below this much, use that.</summary>
public sealed partial class PotionRowViewModel : ObservableObject
{
    /// <summary>Whether the rule is on.</summary>
    [ObservableProperty]
    private bool _enabled;

    /// <summary>The level to act below.</summary>
    [ObservableProperty]
    private double _threshold;

    /// <summary>What to use, by the name the player sees.</summary>
    [ObservableProperty]
    private string? _item;

    /// <summary>
    /// The largest value the box will take.
    /// </summary>
    /// <remarks>
    /// A hundred while the rules are read as percentages, and a character's hit points
    /// otherwise. The reference took whatever was typed, so a rule left at 80 after
    /// switching to percentages read as "below 80%", and one left at 80% after switching
    /// back read as "below 80 hit points" — the same number meaning two different things
    /// with nothing on screen to say which.
    /// </remarks>
    [ObservableProperty]
    private double _ceiling = HelperNumbers.Percent;

    /// <summary>Reads one rule out of the settings.</summary>
    public void Load(PotionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Enabled = row.Enabled;
        Threshold = row.Threshold;
        Item = row.Item;
    }

    /// <summary>Writes it back.</summary>
    public PotionRow ToRow() => new()
    {
        Enabled = Enabled,
        Threshold = HelperNumbers.Whole(Threshold, Ceiling),
        Item = Item?.Trim() ?? string.Empty,
    };
}
