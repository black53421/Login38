using System.Globalization;

namespace Login38.Aux.Game;

/// <summary>
/// The shape of a skill's name inside the client.
/// </summary>
/// <remarks>
/// The client stores a skill as <c>"加速術 (40/0)"</c> — the name, then its cost, its range
/// and sometimes its level in brackets. The player writes only the name in their settings,
/// so the brackets have to come off before the two can be matched; and the range in them is
/// the only place the client says how far a skill reaches.
/// </remarks>
internal static class SpellNames
{
    /// <summary>
    /// The name without the client's bracketed numbers.
    /// </summary>
    /// <remarks>
    /// Only brackets holding nothing but digits, slashes and spaces come off. A skill whose
    /// name genuinely ends in brackets — and some do — keeps them, because taking them off
    /// would make it unmatchable by the name the player can see.
    /// </remarks>
    internal static string StripSuffix(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();

        return Suffix(trimmed) is { } suffix ? trimmed[..suffix.Start].TrimEnd() : trimmed;
    }

    /// <summary>Where the bracketed numbers are, if the name ends in some.</summary>
    private static (int Start, string Inner)? Suffix(string trimmed)
    {
        var open = trimmed.LastIndexOf('(');

        if (open < 0)
        {
            return null;
        }

        var close = trimmed.LastIndexOf(')');

        if (close < open)
        {
            return null;
        }

        var inner = trimmed[(open + 1)..close];

        return inner.Length > 0 && inner.All(c => char.IsAsciiDigit(c) || c is '/' or ' ')
            ? (open, inner)
            : null;
    }
}
