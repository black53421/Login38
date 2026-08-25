using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Aux.Settings;

namespace Login38.App.ViewModels.Helper;

/// <summary>One command run on a fixed interval.</summary>
public sealed partial class TimerRowViewModel : ObservableObject
{
    /// <param name="row">Which of the six this is, for the button that starts it again.</param>
    public TimerRowViewModel(int row) => Row = row;

    /// <summary>Which of the six this is.</summary>
    public int Row { get; }

    /// <summary>What the row is called on screen.</summary>
    public string Label => (Row + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);

    [ObservableProperty]
    private bool _enabled;

    /// <summary>How long between runs, in seconds.</summary>
    [ObservableProperty]
    private double _intervalSeconds = HelperNumbers.ShortestInterval;

    /// <summary>What to run, in the same spelling the helper list uses.</summary>
    [ObservableProperty]
    private string? _command;

    /// <summary>Reads one row out of the settings.</summary>
    public void Load(TimerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Enabled = row.Enabled;
        IntervalSeconds = Math.Clamp(
            row.IntervalSeconds, HelperNumbers.ShortestInterval, HelperNumbers.LongestInterval);
        Command = row.Command;
    }

    /// <summary>Writes it back.</summary>
    public TimerRow ToRow() => new()
    {
        Enabled = Enabled,
        IntervalSeconds = HelperNumbers.Whole(
            IntervalSeconds, HelperNumbers.ShortestInterval, HelperNumbers.LongestInterval),
        Command = Command?.Trim() ?? string.Empty,
    };
}
