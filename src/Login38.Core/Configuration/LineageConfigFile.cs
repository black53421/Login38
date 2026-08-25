using Login38.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Login38.Core.Configuration;

/// <summary>
/// Applies display settings to the client's <c>lineage.cfg</c> before launch.
/// </summary>
public sealed class LineageConfigFile
{
    /// <summary>The file's name inside the game directory.</summary>
    public const string FileName = "lineage.cfg";

    private readonly ILogger<LineageConfigFile> _logger;

    public LineageConfigFile(ILogger<LineageConfigFile> logger) => _logger = logger;

    /// <summary>
    /// Forces the client's display settings.
    /// </summary>
    /// <remarks>
    /// Both values are written in one pass so the file is read and written at most
    /// once, and so a launch never leaves fullscreen and window size disagreeing.
    /// </remarks>
    /// <returns>Whether the file was modified.</returns>
    public bool ApplyDisplaySettings(string gameDirectory, bool fullScreen, WindowMode windowMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var path = Path.Combine(gameDirectory, FileName);

        // A player who has never opened the in-game settings menu has no config at all,
        // and the launcher still has to be able to force windowed mode.
        if (!File.Exists(path))
        {
            var created = LineageConfigDocument.CreateMinimal(fullScreen, windowMode);
            created.SaveIfDirty(path);
            _logger.LogInformation(
                "Created {File} with fullScreen={FullScreen} windowMode={WindowMode}",
                FileName, fullScreen, windowMode);
            return true;
        }

        var document = LineageConfigDocument.Load(path);

        // A missing key is not an error: an older client's config legitimately lacks
        // records this launcher knows about, and inserting one would mean resizing the
        // record stream.
        if (!document.TrySetByte(LineageConfigDocument.FullScreenKey, fullScreen ? (byte)1 : (byte)0))
        {
            _logger.LogDebug("{File} has no FullScreen record; leaving it alone", FileName);
        }

        if (!document.TrySetUInt32(LineageConfigDocument.WindowModeKey, (uint)windowMode))
        {
            _logger.LogDebug("{File} has no WindowMode record; leaving it alone", FileName);
        }

        if (!document.SaveIfDirty(path))
        {
            return false;
        }

        _logger.LogInformation(
            "Updated {File}: fullScreen={FullScreen} windowMode={WindowMode} ({Resolution})",
            FileName, fullScreen, windowMode, windowMode.ToResolution());
        return true;
    }
}
