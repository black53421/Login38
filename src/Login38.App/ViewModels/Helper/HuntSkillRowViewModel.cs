using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Aux.Hunt;

namespace Login38.App.ViewModels.Helper;

/// <summary>One turn of the rotation the hunt takes at what it is fighting.</summary>
/// <remarks>
/// The client knows the rest: how far a skill reaches and whether it acts on a target at all
/// both come out of the character's own spell book, so neither is something to ask a player
/// for and get wrong.
/// </remarks>
public sealed partial class HuntSkillRowViewModel : ObservableObject
{
    /// <summary>Whether the row is on.</summary>
    [ObservableProperty]
    private bool _enabled;

    /// <summary>
    /// Which kind of turn this is, in the order the combo lists them.
    /// </summary>
    /// <remarks>
    /// An index rather than the enum, because binding one to a combo box needs a converter
    /// and two entries in a fixed order need nothing at all. It matches
    /// <see cref="HuntStep"/> by position, which the round trip below relies on and the
    /// tests hold.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSkill))]
    private int _stepIndex;

    /// <summary>Whether the name matters for this row, which is what greys the box out.</summary>
    /// <remarks>
    /// A weapon row casts nothing, so it has no name — but the name is kept rather than
    /// cleared, because switching a row to the weapon and back is something a player does
    /// while working out an order and should not cost them the typing.
    /// </remarks>
    public bool IsSkill => StepIndex == (int)HuntStep.Skill;

    /// <summary>The skill, by the name in the player's own spell book.</summary>
    [ObservableProperty]
    private string? _name;

    /// <summary>The shortest gap between two casts of it, in seconds.</summary>
    [ObservableProperty]
    private double _intervalSeconds = 5;

    /// <summary>Leave it alone below this much mana, as a percentage.</summary>
    [ObservableProperty]
    private double _manaAtLeast;

    /// <summary>Reads one row out of the settings.</summary>
    public void Load(HuntSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        Enabled = skill.Enabled;
        StepIndex = (int)skill.Step;
        Name = skill.Name;
        IntervalSeconds = skill.IntervalSeconds;
        ManaAtLeast = skill.ManaAtLeast;
    }

    /// <summary>Writes it back.</summary>
    public HuntSkill ToRow() => new()
    {
        Enabled = Enabled,
        Step = StepIndex == (int)HuntStep.Weapon ? HuntStep.Weapon : HuntStep.Skill,
        Name = Name?.Trim() ?? string.Empty,
        IntervalSeconds = double.IsNaN(IntervalSeconds)
            ? HelperNumbers.ShortestInterval
            : Math.Clamp(
                Math.Round(IntervalSeconds, 1),
                0,
                HelperNumbers.LongestInterval),
        ManaAtLeast = HelperNumbers.Whole(ManaAtLeast, HelperNumbers.Percent),
    };
}
