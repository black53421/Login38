using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Aux.Settings;

namespace Login38.App.ViewModels.Helper;

/// <summary>A command bound to one of the function keys.</summary>
public sealed partial class MacroViewModel : ObservableObject
{
    /// <param name="key">Which function key, counted from one.</param>
    public MacroViewModel(int key) => Key = key;

    /// <summary>Which function key this is, counted from one.</summary>
    public int Key { get; }

    /// <summary>What the key is called on screen.</summary>
    public string Label => "F" + Key.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _enabled;

    /// <summary>What to run, in the same spelling the helper list uses.</summary>
    [ObservableProperty]
    private string? _command;

    /// <summary>Reads one macro out of the settings.</summary>
    public void Load(FunctionKeyMacro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);

        Enabled = macro.Enabled;
        Command = macro.Command;
    }

    /// <summary>Writes it back.</summary>
    public FunctionKeyMacro ToMacro() => new()
    {
        Enabled = Enabled,
        Command = Command?.Trim() ?? string.Empty,
    };
}
