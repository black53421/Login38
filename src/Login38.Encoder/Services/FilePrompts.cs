using System.Windows;
using Microsoft.Win32;

namespace Login38.Encoder.Services;

/// <summary>The desktop's own file and folder pickers.</summary>
/// <remarks>
/// Every one of these is modal on the encoder's window, so an operator cannot start a
/// second package while choosing the frames for the first.
/// </remarks>
public sealed class FilePrompts : IFilePrompts
{
    /// <inheritdoc/>
    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };

        return Show(dialog) ? dialog.FileName : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> OpenFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = true,
        };

        // In the order the picker returns them, which is the order they play. A dialog
        // that sorted them would put frame 10 before frame 2.
        return Show(dialog) ? dialog.FileNames : [];
    }

    /// <inheritdoc/>
    public string? OpenFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };

        return Owner() is { } owner
            ? dialog.ShowDialog(owner) == true ? dialog.FolderName : null
            : dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <inheritdoc/>
    public void Say(string message) =>
        MessageBox.Show(message, "編碼器", MessageBoxButton.OK, MessageBoxImage.Information);

    private static bool Show(OpenFileDialog dialog) =>
        (Owner() is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    private static Window? Owner() => Application.Current?.MainWindow;
}
