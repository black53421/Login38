namespace Login38.Aux.Game;

/// <summary>
/// Reduces an item name to the part the player would call it.
/// </summary>
/// <remarks>
/// <para>
/// The client puts several things inside the name that are not part of it: a stack count,
/// and a mark saying the item is worn or being wielded. A rule an operator writes says
/// "silver sword" — it has to keep matching once the sword is in hand and the client has
/// renamed it to "silver sword (揮舞)", and once the stack has gone from 999 to 1,000 and
/// the client has started writing a thousands separator.
/// </para>
/// <para>
/// What is deliberately <em>not</em> stripped is a bracket that is part of the name.
/// "魔法卷軸 (擬似魔法武器)" is a different scroll from "魔法卷軸 (初級治癒術)", and an
/// operator has to be able to name one of them.
/// </para>
/// </remarks>
public static class ItemNames
{
    /// <summary>The mark on an item that is currently worn.</summary>
    public const string InUseMark = "(使用中)";

    /// <summary>The mark on a weapon that is currently held.</summary>
    public const string WieldedMark = "(揮舞)";

    private static readonly string[] StateMarks = [WieldedMark, InUseMark];

    /// <summary>Removes a trailing stack count.</summary>
    /// <remarks>
    /// Only when the brackets hold nothing but digits and separators — which is what makes
    /// this safe to run on every name. A bracket with anything else in it is part of the
    /// name.
    /// </remarks>
    public static string StripQuantity(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var open = name.LastIndexOf(" (", StringComparison.Ordinal);

        if (open < 0)
        {
            return name.Trim();
        }

        var close = name.LastIndexOf(')');

        if (close < open + 2)
        {
            return name.Trim();
        }

        var inside = name.AsSpan((open + 2)..close);

        if (inside.IsEmpty)
        {
            return name.Trim();
        }

        foreach (var character in inside)
        {
            if (!char.IsAsciiDigit(character) && character != ',')
            {
                return name.Trim();
            }
        }

        return name[..open].TrimEnd();
    }

    /// <summary>Removes the client's own mark for worn or wielded.</summary>
    /// <remarks>
    /// The two marks are listed rather than matched as "any bracket at the end", because
    /// plenty of item names end in a bracket that belongs to them.
    /// </remarks>
    public static string StripState(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.TrimEnd();

        foreach (var mark in StateMarks)
        {
            if (trimmed.EndsWith(mark, StringComparison.Ordinal))
            {
                return trimmed[..^mark.Length].TrimEnd();
            }
        }

        return trimmed;
    }

    /// <summary>Both of the above, which is what a name is compared by.</summary>
    public static string Clean(string name) => StripState(StripQuantity(name));

    /// <summary>Whether two names refer to the same item, whatever state it is in.</summary>
    public static bool Match(string name, string wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        return Clean(name).Equals(Clean(wanted), StringComparison.Ordinal);
    }
}
