namespace Login38.Core;

/// <summary>
/// The launcher's config-file spelling of booleans.
/// </summary>
/// <remarks>
/// Config files are read by, and written for, several tools that predate this one, so
/// the accepted spellings and the emitted spelling both matter. In particular
/// <see cref="ToConfigValue"/> is lower-case: <c>bool.ToString()</c> would emit
/// <c>True</c>, which older readers do not recognise.
/// </remarks>
public static class BooleanText
{
    /// <summary>Whether a config value means true. Anything unrecognised means false.</summary>
    public static bool IsTruthy(string? value) =>
        value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

    /// <summary>
    /// Whether a config value explicitly means false. Distinct from <c>!IsTruthy</c>,
    /// which is also true for missing or malformed values.
    /// </summary>
    public static bool IsFalsy(string? value) =>
        value?.Trim().ToLowerInvariant() is "0" or "false" or "no" or "off";

    /// <summary>Renders a boolean the way config readers expect it.</summary>
    public static string ToConfigValue(bool value) => value ? "true" : "false";
}
