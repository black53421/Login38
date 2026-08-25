namespace Login38.Core;

/// <summary>
/// Reads the boolean environment variables used to toggle launcher behaviour.
/// </summary>
/// <remarks>
/// These exist so a feature can be turned off on a player's machine without shipping
/// a new build. Unset means off, and an unparseable value means off rather than
/// throwing — a bad variable should never stop the game from starting.
/// </remarks>
public static class EnvironmentSwitch
{
    /// <summary>Whether the named variable is set to a truthy value.</summary>
    public static bool IsEnabled(string name) =>
        BooleanText.IsTruthy(Environment.GetEnvironmentVariable(name));

    /// <summary>
    /// Whether the named variable is explicitly set to a falsy value. Distinct from
    /// <c>!IsEnabled</c>: this is false when the variable is unset, which lets a
    /// feature default to on while still being switchable off.
    /// </summary>
    public static bool IsDisabled(string name) =>
        BooleanText.IsFalsy(Environment.GetEnvironmentVariable(name));

    /// <summary>Whether the named variable is set at all, regardless of value.</summary>
    public static bool IsSet(string name) =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));

    /// <summary>The variable's raw value, or null when unset or empty.</summary>
    public static string? Value(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
