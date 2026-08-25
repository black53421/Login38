namespace Login38.Core.Diagnostics;

/// <summary>
/// Settings for the launcher's own log sink.
/// </summary>
public sealed class LauncherLogOptions
{
    /// <summary>
    /// Set this to a truthy value to turn on file logging. Off by default: players
    /// should not accumulate log files on disk, but a support case can ask for one
    /// without a new build.
    /// </summary>
    public const string WriteLogsEnvironmentVariable = "LOGIN38_WRITE_LOGS";

    /// <summary>Whether to write log files at all.</summary>
    public bool WriteToFile { get; set; }

    /// <summary>Where the log files go. Defaults to the folder holding the executable.</summary>
    public string Directory { get; set; } = AppContext.BaseDirectory;

    /// <summary>Everything that is not startup diagnostics.</summary>
    public string GeneralFileName { get; set; } = "launcher_debug.log";

    /// <summary>
    /// Startup and patching diagnostics. Kept separate because these are the lines a
    /// support case actually needs, and mixing them with per-frame helper chatter
    /// makes them impossible to read.
    /// </summary>
    public string StartupFileName { get; set; } = "launcher_startup_timing.log";

    /// <summary>
    /// Logger categories starting with one of these go to <see cref="StartupFileName"/>.
    /// Routing by category rather than by scanning the message text, which is what the
    /// reference implementation did, keeps the classification out of the message body.
    /// </summary>
    public IList<string> StartupCategoryPrefixes { get; } =
    [
        "Login38.App.Launching",
        "Login38.Patching",
        "Login38.Interop",
    ];

    /// <summary>Full path of the general log file.</summary>
    public string GeneralFilePath => Path.Combine(Directory, GeneralFileName);

    /// <summary>Full path of the startup diagnostics file.</summary>
    public string StartupFilePath => Path.Combine(Directory, StartupFileName);

    /// <summary>Whether a category's lines belong in the startup diagnostics file.</summary>
    public bool IsStartupCategory(string category)
    {
        foreach (var prefix in StartupCategoryPrefixes)
        {
            if (category.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
