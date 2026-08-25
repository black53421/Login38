using System.Globalization;

namespace Login38.Core.Configuration;

/// <summary>
/// The player's own settings, stored in <c>launcher.ini</c> beside the executable.
/// </summary>
/// <remarks>
/// Kept separate from the operator-distributed <c>config.ini</c> on purpose. That file
/// carries the server's skin and announcement URLs and gets redistributed; if player
/// choices lived there, every update would overwrite them.
/// </remarks>
public sealed class UserPreferences
{
    public const string FileName = "launcher.ini";

    private const string SectionHeader = "[Settings]";
    private const string WindowedKey = "windowed";
    private const string WindowModeKey = "window_mode";

    /// <summary>Run the game in a window rather than fullscreen. Default on.</summary>
    public bool Windowed { get; set; } = true;

    public WindowMode WindowMode { get; set; } = WindowModeExtensions.Default;

    /// <summary>
    /// Loads from <paramref name="path"/>. A missing, unreadable or malformed file
    /// yields defaults — a broken preferences file must never stop the game starting.
    /// </summary>
    public static UserPreferences Load(string path)
    {
        var preferences = new UserPreferences();

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return preferences;
        }

        foreach (var entry in IniReader.Read(content))
        {
            switch (entry.Key)
            {
                case WindowedKey:
                    preferences.Windowed = BooleanText.IsTruthy(entry.Value);
                    break;

                case WindowModeKey:
                    // An out-of-range value keeps the default rather than clamping:
                    // the four sizes are not a continuum.
                    if (byte.TryParse(entry.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var raw) &&
                        raw.ToWindowMode() is { } mode)
                    {
                        preferences.WindowMode = mode;
                    }

                    break;
            }
        }

        return preferences;
    }

    /// <summary>
    /// Writes to <paramref name="path"/>.
    /// </summary>
    /// <returns>
    /// Whether the write succeeded. Failure is reported rather than thrown: a
    /// read-only install directory should not block a launch, it should just mean the
    /// choice is not remembered.
    /// </returns>
    public bool TrySave(string path)
    {
        var content =
            $"{SectionHeader}\n" +
            $"{WindowedKey}={BooleanText.ToConfigValue(Windowed)}\n" +
            $"{WindowModeKey}={(byte)WindowMode}\n";

        try
        {
            File.WriteAllText(path, content);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The default location: beside the running executable.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);
}
