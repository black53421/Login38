using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Aux.Hunt;

namespace Login38.App.ViewModels.Helper;

/// <summary>
/// The hunt page.
/// </summary>
/// <remarks>
/// <para>
/// Five fields and two lists, which is the whole of what a player can usefully decide.
/// Everything the reference's six tabs were laid out for — a walk driver, a replan budget,
/// a pathfinder iteration cap — described work this does not do: the client walks, and the
/// only thing it needs told is which creature.
/// </para>
/// <para>
/// Nothing here reaches the game. The task applies it on its next pass, so a switch moved
/// with no client running is simply a switch that is already on when one starts.
/// </para>
/// </remarks>
public sealed partial class HuntViewModel : ObservableObject
{
    /// <summary>Whether to hunt at all.</summary>
    [ObservableProperty]
    private bool _enabled;

    /// <summary>How far to look, counted in steps over the client's own collision grid.</summary>
    [ObservableProperty]
    private double _rangeSteps = 60;

    /// <summary>How close to stand before shooting, for a weapon that reaches further.</summary>
    [ObservableProperty]
    private double _standoffTiles = 8;

    /// <summary>Whether to turn on whatever starts hitting the character.</summary>
    [ObservableProperty]
    private bool _retaliate = true;

    /// <summary>How long nothing may happen before the current target is given up on.</summary>
    [ObservableProperty]
    private double _stallSeconds = 6;

    /// <summary>And how long it is left alone afterwards.</summary>
    [ObservableProperty]
    private double _ignoreSeconds = 30;

    /// <summary>
    /// The longest either relocate clock may be set to, in seconds.
    /// </summary>
    /// <remarks>
    /// Ten minutes. Past that a player wanting the character to stay put wants the rule off,
    /// and the box says so by refusing the number rather than by accepting one that never
    /// fires.
    /// </remarks>
    private const double Longest = 600;

    /// <summary>Whether to read a scroll when the spot has run out.</summary>
    [ObservableProperty]
    private bool _relocateEnabled;

    /// <summary>How long the character may stand on one square before moving on. 0 = never.</summary>
    [ObservableProperty]
    private double _relocateStuckSeconds = 90;

    /// <summary>How long there may be nothing to hunt before moving on. 0 = never.</summary>
    [ObservableProperty]
    private double _relocateBarrenSeconds = 60;

    /// <summary>Which scroll to read for it.</summary>
    [ObservableProperty]
    private string _relocateItem = string.Empty;

    /// <summary>Whether to read a scroll when the fight has gone badly.</summary>
    [ObservableProperty]
    private bool _escapeEnabled;

    /// <summary>Read it below this much health, as a percentage.</summary>
    [ObservableProperty]
    private double _escapeHitPoints = 30;


    /// <summary>What to read, by the name in the bag.</summary>
    [ObservableProperty]
    private string? _escapeItem;


    /// <summary>What to throw at the target besides the weapon.</summary>
    public ObservableCollection<HuntSkillRowViewModel> Skills { get; } =
        [.. Enumerable.Range(0, HuntSettings.SkillRows).Select(_ => new HuntSkillRowViewModel())];

    /// <summary>Names never to attack.</summary>
    public ChoiceListViewModel Blacklist { get; } = new();

    /// <summary>Only these, when it is not empty.</summary>
    public ChoiceListViewModel Whitelist { get; } = new();

    /// <summary>Reads the page out of the settings.</summary>
    public void Load(HuntSettings hunt)
    {
        ArgumentNullException.ThrowIfNull(hunt);

        Enabled = hunt.Enabled;
        Retaliate = hunt.Retaliate;
        RangeSteps = hunt.RangeSteps;
        StandoffTiles = hunt.StandoffTiles;
        StallSeconds = hunt.StallSeconds;
        IgnoreSeconds = hunt.IgnoreSeconds;
        Blacklist.Load(hunt.Blacklist);
        Whitelist.Load(hunt.Whitelist);

        for (var i = 0; i < Skills.Count && i < hunt.Skills.Length; i++)
        {
            Skills[i].Load(hunt.Skills[i]);
        }

        var escape = hunt.Escape;

        var relocate = hunt.Relocate;

        RelocateEnabled = relocate.Enabled;
        RelocateStuckSeconds = relocate.StuckSeconds;
        RelocateBarrenSeconds = relocate.BarrenSeconds;
        RelocateItem = relocate.Item;

        EscapeEnabled = escape.Enabled;
        EscapeHitPoints = escape.HitPointsBelow;
        EscapeItem = escape.Item;
    }

    /// <summary>Writes it back.</summary>
    public HuntSettings ToSettings() => new()
    {
        Enabled = Enabled,
        Retaliate = Retaliate,
        RangeSteps = (int)RangeSteps,
        StandoffTiles = (int)StandoffTiles,
        StallSeconds = (int)StallSeconds,
        IgnoreSeconds = (int)IgnoreSeconds,
        Blacklist = [.. Blacklist.Items],
        Whitelist = [.. Whitelist.Items],
        Skills = [.. Skills.Select(row => row.ToRow())],
        Relocate = new RelocateRule
        {
            Enabled = RelocateEnabled,
            StuckSeconds = HelperNumbers.Whole(RelocateStuckSeconds, Longest),
            BarrenSeconds = HelperNumbers.Whole(RelocateBarrenSeconds, Longest),
            Item = RelocateItem.Trim(),
        },
        Escape = new EscapeRule
        {
            Enabled = EscapeEnabled,
            HitPointsBelow = HelperNumbers.Whole(EscapeHitPoints, HelperNumbers.Percent),
            Item = EscapeItem?.Trim() ?? string.Empty,
        },
    };
}
