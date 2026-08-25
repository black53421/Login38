using System.Globalization;

namespace Login38.Aux.Settings;

/// <summary>
/// Reads and writes the one-line form operators use for helper entries.
/// </summary>
/// <remarks>
/// <para>
/// There are two spellings of the same thing, and both have to be read. The one operators
/// have in their existing files puts the status flag first and a suffix after a slash —
/// <c>0_加速術/ME</c>. The newer one puts the name first and separates three fields with
/// underscores — <c>加速術_0_MME</c>. Which one a line is in is decided by whether it has
/// a slash in it.
/// </para>
/// <para>
/// The middle field does double duty, and that is the part worth knowing: for most suffixes
/// it is a status flag number, and for the ones that act on something named it is that name.
/// So <c>提煉魔石_紅魔石_MI</c> is "cast 提煉魔石 on the item called 紅魔石", and the entry
/// has no status flag.
/// </para>
/// <para>
/// Nothing here fails. An operator's file is read at launch, and a line nobody can make
/// sense of should cost that one line rather than the whole file — so an unknown suffix
/// falls back to plainly using the item, which is the safest thing an entry can do.
/// </para>
/// </remarks>
public static class HelperEntrySyntax
{
    private const char Legacy = '/';
    private const char Native = '_';

    /// <summary>Reads one line, in whichever of the two spellings it is written in.</summary>
    public static HelperEntry Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var trimmed = line.Trim();

        if (trimmed.Contains(Legacy, StringComparison.Ordinal))
        {
            return ParseWithSlash(trimmed);
        }

        var fields = trimmed.Split(Native, 3);

        // Two fields is ambiguous. A leading number is the older spelling's status flag, so
        // `0_自我加速藥水` is a flag and a name; anything else is the newer spelling with its
        // suffix left off, so `肉_-1` is a name and a flag.
        if (fields is [var first, _] && IsNumber(first))
        {
            return ParseWithSlash(trimmed);
        }

        var (name, middle, suffix) = fields switch
        {
            [var a, var b, var c] => (a.Trim(), b.Trim(), c.Trim()),
            [var a, var b] => (a.Trim(), b.Trim(), string.Empty),
            _ => (trimmed, NoStateText, string.Empty),
        };

        var (kind, cast) = ParseSuffix(suffix, middle);

        return new HelperEntry(ParseStateId(middle), name, kind, cast);
    }

    /// <summary>
    /// Writes the line back, in the spelling operators keep their files in.
    /// </summary>
    /// <remarks>
    /// The older one wherever it can express the entry, because that is what is already in
    /// their files and what the rest of the documentation shows. The newer one only for the
    /// suffixes the older one has no spelling for.
    /// </remarks>
    public static string ToSettingText(HelperEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var id = entry.StateId.ToString(CultureInfo.InvariantCulture);
        var item = entry.Kind == EntryKind.Item;

        return entry.Cast switch
        {
            { Kind: CastKind.Item } => $"{id}_{entry.Name}",
            { Kind: CastKind.NoSpec } => $"{id}_{entry.Name}/M",
            { Kind: CastKind.OnSelf } => $"{id}_{entry.Name}/ME",
            { Kind: CastKind.Key, FunctionKey: var n } => $"{id}_{entry.Name}/KEY=F{n}",
            { Kind: CastKind.DelayKey, FunctionKey: var n } => $"{id}_{entry.Name}/DKEY=F{n}",
            { Kind: CastKind.OnSelfItem } => $"{id}_{entry.Name}/IME",

            { Kind: CastKind.HoverTarget } when item => $"{id}_{entry.Name}/IT",
            { Kind: CastKind.OnNamedEntity, Target: var t } when item => $"{id}_{entry.Name}/IT={t}",
            { Kind: CastKind.OnInUseItem, Target: null } when item => $"{id}_{entry.Name}/IA",
            { Kind: CastKind.OnInUseItem, Target: var t } when item => $"{id}_{entry.Name}/IA={t}",
            { Kind: CastKind.OnWieldedItem, Target: null } when item => $"{id}_{entry.Name}/IW",
            { Kind: CastKind.OnWieldedItem, Target: var t } when item => $"{id}_{entry.Name}/IW={t}",
            { Kind: CastKind.OnNamedItem, Target: var t } when item => $"{id}_{entry.Name}/I={t}",

            // No older spelling for these, so they stay in the newer one.
            { Kind: CastKind.HoverTarget } => $"{entry.Name}_{id}_MT",
            { Kind: CastKind.OnNamedEntity } => $"{entry.Name}_{id}_MT",
            { Kind: CastKind.OnInUseItem } => $"{entry.Name}_{id}_MIA",
            { Kind: CastKind.OnWieldedItem } => $"{entry.Name}_{id}_MIW",

            // The middle field carries the target instead of the flag, which is the whole
            // reason the newer spelling exists.
            { Kind: CastKind.OnNamedItem, Target: var t } => $"{entry.Name}_{t}_MI",

            { Kind: CastKind.DropItem } => $"{entry.Name}_{id}_ID",
            _ => $"{entry.Name}_{id}_INFO",
        };
    }

    /// <summary>
    /// Writes the line the way a timer or a function-key macro takes it.
    /// </summary>
    /// <remarks>
    /// The same entry without the status flag, because a command typed into a box is run
    /// when the box says to run it — there is no state to check first.
    /// </remarks>
    public static string ToCommandText(HelperEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var item = entry.Kind == EntryKind.Item;

        var suffix = entry.Cast switch
        {
            { Kind: CastKind.Item } => string.Empty,
            { Kind: CastKind.NoSpec } => "M",
            { Kind: CastKind.OnSelf } => "ME",
            { Kind: CastKind.Key, FunctionKey: var n } => $"KEY=F{n}",
            { Kind: CastKind.DelayKey, FunctionKey: var n } => $"DKEY=F{n}",
            { Kind: CastKind.OnSelfItem } => "IME",

            { Kind: CastKind.HoverTarget } => item ? "IT" : "MT",
            { Kind: CastKind.OnNamedEntity, Target: var t } => item ? $"IT={t}" : "MT",
            { Kind: CastKind.OnInUseItem, Target: null } => item ? "IA" : "MIA",
            { Kind: CastKind.OnInUseItem, Target: var t } => item ? $"IA={t}" : "MIA",
            { Kind: CastKind.OnWieldedItem, Target: null } => item ? "IW" : "MIW",
            { Kind: CastKind.OnWieldedItem, Target: var t } => item ? $"IW={t}" : "MIW",
            { Kind: CastKind.OnNamedItem, Target: var t } => item ? $"I={t}" : $"MI={t}",

            { Kind: CastKind.DropItem } => "ID",
            _ => "INFO",
        };

        return suffix.Length == 0 ? entry.Name : $"{entry.Name}{Legacy}{suffix}";
    }

    /// <summary>
    /// Reads a suffix of the newer spelling.
    /// </summary>
    /// <param name="suffix">The third field, with no leading slash.</param>
    /// <param name="middle">
    /// The second field, which is a target name for the suffixes that take one and a status
    /// flag for the rest.
    /// </param>
    public static (EntryKind Kind, CastTarget Cast) ParseSuffix(string suffix, string middle)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        ArgumentNullException.ThrowIfNull(middle);

        var s = suffix.Trim();

        if (TryParseKeyMacro(s, out var macro))
        {
            return (EntryKind.Key, macro);
        }

        // A number here is a status flag, so there is no name — which is the difference
        // between "on the one called X" and "on the first one you find".
        var target = IsNumber(middle) ? null : middle;

        return s switch
        {
            "" or "I" => (EntryKind.Item, CastTarget.Item),
            "ID" => (EntryKind.Item, CastTarget.Any(CastKind.DropItem)),
            "IA" or "IIA" => (EntryKind.Item, new CastTarget(CastKind.OnInUseItem, target)),
            "IW" or "IIW" => (EntryKind.Item, new CastTarget(CastKind.OnWieldedItem, target)),
            "II" => (EntryKind.Item, CastTarget.Named(CastKind.OnNamedItem, middle)),

            // With a name it is fully automatic — the entity is found by scanning. Without
            // one the client does its own target picking and the player clicks.
            "IT" => (EntryKind.Item, target is null
                ? CastTarget.Any(CastKind.HoverTarget)
                : CastTarget.Named(CastKind.OnNamedEntity, target)),

            "IME" => (EntryKind.Item, CastTarget.Any(CastKind.OnSelfItem)),

            "M" => (EntryKind.Skill, CastTarget.Any(CastKind.NoSpec)),
            "MME" => (EntryKind.Skill, CastTarget.Any(CastKind.OnSelf)),
            "MT" => (EntryKind.Skill, CastTarget.Any(CastKind.HoverTarget)),
            "MIA" => (EntryKind.Skill, CastTarget.Any(CastKind.OnInUseItem)),
            "MIW" => (EntryKind.Skill, CastTarget.Any(CastKind.OnWieldedItem)),
            "MI" => (EntryKind.Skill, CastTarget.Named(CastKind.OnNamedItem, middle)),

            "INFO" => (EntryKind.Item, CastTarget.Any(CastKind.Info)),

            // Suffixes that were written down but never implemented land here. Using the
            // item is the one outcome that cannot send a packet the server did not expect.
            _ => (EntryKind.Item, CastTarget.Item),
        };
    }

    /// <summary>Reads <c>F1</c> through <c>F12</c>.</summary>
    /// <returns>Null for anything else, including a key the client does not have.</returns>
    public static byte? ParseFunctionKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var trimmed = text.Trim();

        if (trimmed.Length < 2 || (trimmed[0] is not ('F' or 'f')))
        {
            return null;
        }

        return byte.TryParse(trimmed[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var key)
            && key is >= CastTarget.FirstFunctionKey and <= CastTarget.LastFunctionKey
            ? key
            : null;
    }

    /// <summary>
    /// Reads the older spelling, <c>&lt;flag&gt;_&lt;name&gt;/&lt;suffix&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The flag is optional: some sections of the operator's file have never carried one,
    /// so a line that does not start with a number is all name.
    /// </remarks>
    private static HelperEntry ParseWithSlash(string line)
    {
        var stateId = HelperEntry.NoState;
        var rest = line;
        var underscore = line.IndexOf(Native, StringComparison.Ordinal);

        if (underscore > 0 && IsNumber(line[..underscore]))
        {
            stateId = ParseStateId(line[..underscore]);
            rest = line[(underscore + 1)..];
        }

        var slash = rest.IndexOf(Legacy, StringComparison.Ordinal);

        if (slash < 0)
        {
            return new HelperEntry(stateId, rest.Trim(), EntryKind.Item, CastTarget.Item);
        }

        var name = rest[..slash].Trim();
        var (kind, cast) = ParseSlashSuffix(rest[(slash + 1)..].Trim());

        return new HelperEntry(stateId, name, kind, cast);
    }

    private static (EntryKind Kind, CastTarget Cast) ParseSlashSuffix(string suffix)
    {
        if (TryParseKeyMacro(suffix, out var macro))
        {
            return (EntryKind.Key, macro);
        }

        // The name comes after an equals sign here rather than from the middle field.
        var (head, name) = Split(suffix);

        if (name is not null)
        {
            return head switch
            {
                // Two spellings of the same thing, from before the newer one existed.
                "MI" or "M" => (EntryKind.Skill, CastTarget.Named(CastKind.OnNamedItem, name)),
                "I" => (EntryKind.Item, CastTarget.Named(CastKind.OnNamedItem, name)),
                "IA" => (EntryKind.Item, CastTarget.Named(CastKind.OnInUseItem, name)),
                "IW" => (EntryKind.Item, CastTarget.Named(CastKind.OnWieldedItem, name)),
                "IT" => (EntryKind.Item, CastTarget.Named(CastKind.OnNamedEntity, name)),
                _ => (EntryKind.Item, CastTarget.Item),
            };
        }

        return suffix switch
        {
            "IME" => (EntryKind.Item, CastTarget.Any(CastKind.OnSelfItem)),
            "M" => (EntryKind.Skill, CastTarget.Any(CastKind.NoSpec)),
            "ME" => (EntryKind.Skill, CastTarget.Any(CastKind.OnSelf)),
            "MIA" => (EntryKind.Skill, CastTarget.Any(CastKind.OnInUseItem)),
            "MIW" => (EntryKind.Skill, CastTarget.Any(CastKind.OnWieldedItem)),

            // A bare /MI is from a version that read the suffix as something else, and it
            // carries no name to act on. Casting without a target is the closest thing to it.
            "MI" => (EntryKind.Skill, CastTarget.Any(CastKind.NoSpec)),

            "IA" => (EntryKind.Item, CastTarget.Any(CastKind.OnInUseItem)),
            "IW" => (EntryKind.Item, CastTarget.Any(CastKind.OnWieldedItem)),
            "IT" => (EntryKind.Item, CastTarget.Any(CastKind.HoverTarget)),
            "ID" => (EntryKind.Item, CastTarget.Any(CastKind.DropItem)),
            "INFO" => (EntryKind.Item, CastTarget.Any(CastKind.Info)),
            _ => (EntryKind.Item, CastTarget.Item),
        };
    }

    private static bool TryParseKeyMacro(string suffix, out CastTarget cast)
    {
        cast = default;

        var (head, argument) = Split(suffix);

        if (argument is null)
        {
            return false;
        }

        var kind = head switch
        {
            "KEY" => CastKind.Key,
            "DKEY" => CastKind.DelayKey,
            _ => (CastKind?)null,
        };

        if (kind is null || ParseFunctionKey(argument) is not { } key)
        {
            return false;
        }

        cast = CastTarget.FunctionKeyPress(kind.Value, key);
        return true;
    }

    /// <summary>Splits <c>HEAD=argument</c>.</summary>
    private static (string Head, string? Argument) Split(string suffix)
    {
        var equals = suffix.IndexOf('=', StringComparison.Ordinal);

        return equals < 0 ? (suffix, null) : (suffix[..equals], suffix[(equals + 1)..].Trim());
    }

    private static readonly string NoStateText =
        HelperEntry.NoState.ToString(CultureInfo.InvariantCulture);

    private static bool IsNumber(string text) =>
        int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static int ParseStateId(string text) =>
        int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
            ? id
            : HelperEntry.NoState;
}
