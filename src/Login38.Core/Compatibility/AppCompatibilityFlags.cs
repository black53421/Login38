using System.Globalization;
using Microsoft.Win32;

namespace Login38.Core.Compatibility;

/// <summary>
/// Sets the per-executable compatibility flags Windows exposes under an executable's
/// Properties dialog.
/// </summary>
/// <remarks>
/// <para>
/// The client is a 1990s DirectDraw application, and modern Windows applies two things
/// to it that it was never written for: fullscreen optimisations, which put a compositor
/// between it and the screen, and DPI virtualisation, which bilinear-scales its 800x600
/// back buffer up to the desktop scale and leaves ghosting around every sprite edge.
/// </para>
/// <para>
/// Both are switched off through the same registry value the Compatibility tab writes.
/// The setting is per-path and applies at the next launch, so it is written before the
/// client starts rather than patched into it.
/// </para>
/// </remarks>
public static class AppCompatibilityFlags
{
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    /// <summary>Stops the compositor taking over the client's fullscreen presentation.</summary>
    public const string DisableFullscreenOptimizations = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    /// <summary>Makes Windows treat the client as DPI aware instead of scaling its output.</summary>
    public const string HighDpiAware = "HIGHDPIAWARE";

    /// <summary>
    /// The marker Windows puts at the front of the value. Not a flag, and dropping it
    /// makes Windows ignore the whole entry.
    /// </summary>
    private const string Marker = "~";

    /// <summary>
    /// Ensures <paramref name="flags"/> are set for an executable, leaving any others in
    /// place.
    /// </summary>
    /// <returns>Whether the registry had to be changed.</returns>
    /// <remarks>
    /// Written under <c>HKEY_CURRENT_USER</c>, so this needs no elevation and only
    /// affects the player who launched it.
    /// </remarks>
    public static bool Ensure(string executablePath, params string[] flags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(flags);

        // The value name is the path, matched literally by Windows, so it has to be the
        // same form the shell would write.
        var fullPath = Path.GetFullPath(executablePath);

        using var key = Registry.CurrentUser.CreateSubKey(LayersKey, writable: true)
            ?? throw new InvalidOperationException($@"HKCU\{LayersKey} could not be opened.");

        var existing = key.GetValue(fullPath) as string;
        var merged = Merge(existing, flags);

        if (string.Equals(existing?.Trim(), merged, StringComparison.Ordinal))
        {
            return false;
        }

        key.SetValue(fullPath, merged, RegistryValueKind.String);
        return true;
    }

    /// <summary>
    /// Adds flags to an existing value without disturbing what is already there.
    /// </summary>
    /// <remarks>
    /// The player may have set flags of their own through the Properties dialog, and this
    /// value is the only place they exist. Comparison is case-insensitive because Windows
    /// treats the flags that way and the dialog does not normalise what it writes.
    /// </remarks>
    public static string Merge(string? existing, params string[] flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        var present = (existing ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.Equals(part, Marker, StringComparison.Ordinal))
            .ToList();

        foreach (var flag in flags)
        {
            if (!present.Any(p => string.Equals(p, flag, StringComparison.OrdinalIgnoreCase)))
            {
                present.Add(flag);
            }
        }

        return present.Count == 0
            ? Marker
            : string.Create(CultureInfo.InvariantCulture, $"{Marker} {string.Join(' ', present)}");
    }
}
