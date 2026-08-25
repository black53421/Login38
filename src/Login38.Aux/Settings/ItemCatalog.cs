using System.Reflection;
using Login38.Core.Text;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Settings;

/// <summary>
/// One list of choices the helper window offers, and where in the file it comes from.
/// </summary>
/// <param name="Section">The INI section, without its brackets.</param>
/// <param name="KeyPrefix">
/// What the keys carrying this list start with. One section holds several lists — the
/// healing section carries <c>Item0…</c> for what to drink and <c>HPMP0…</c> for what
/// converts hit points into mana — so the prefix is what separates them.
/// </param>
public readonly record struct ItemList(string Section, string KeyPrefix);

/// <summary>
/// The item and spell names the helper window offers, read from a file the player owns.
/// </summary>
/// <remarks>
/// <para>
/// Names in this game are strings the server and the client agree on, and they differ
/// between servers. So they are not compiled in: a copy of the list ships inside the
/// launcher, is written out beside it on first run, and from then on the file is the
/// truth. A player adding a potion their server has edits one line and restarts nothing.
/// </para>
/// <para>
/// The reference had five near-identical readers for what is one operation — walk an INI
/// section, take the values whose key starts with something — and kept its fallback lists
/// as a second hand-written copy of what the template already said, so the two drifted.
/// There is one reader here, and the fallback is the shipped template parsed by it.
/// </para>
/// </remarks>
public sealed class ItemCatalog
{
    /// <summary>What the file is called, beside the launcher.</summary>
    public const string FileName = "linhelperZ.ini";

    /// <summary>What to drink to heal.</summary>
    public static ItemList Healing { get; } = new("AllHP", "Item");

    /// <summary>What turns hit points into mana.</summary>
    public static ItemList ManaConversion { get; } = new("AllHP", "HPMP");

    /// <summary>What the helper can keep up, as entries the syntax understands.</summary>
    public static ItemList States { get; } = new("AllState", "Item");

    /// <summary>The scrolls that ask which shape to take.</summary>
    public static ItemList TransformItems { get; } = new("AllPolyItems", "Item");

    /// <summary>The shapes those scrolls offer.</summary>
    public static ItemList Transformations { get; } = new("AllPolymorphs", "Item");

    /// <summary>What clears poison.</summary>
    public static ItemList Antidotes { get; } = new("AllAntidote", "Item");

    private readonly ILegacyTextCodec _codec;
    private readonly ILogger<ItemCatalog> _logger;
    private readonly Lock _gate = new();

    private string? _text;

    /// <param name="directory">Where the file lives. Defaults to beside the launcher.</param>
    public ItemCatalog(ILegacyTextCodec codec, ILogger<ItemCatalog> logger, string? directory = null)
    {
        _codec = codec;
        _logger = logger;
        Path = System.IO.Path.Combine(directory ?? AppContext.BaseDirectory, FileName);
    }

    /// <summary>Where the file is.</summary>
    public string Path { get; }

    /// <summary>
    /// Makes sure the player has a file, and that it has every section this build knows about.
    /// </summary>
    /// <remarks>
    /// A file that is already there is never rewritten — it is the player's, and it is
    /// usually the reason they are looking at this window at all. Only sections it has
    /// never heard of are appended, taken from the shipped copy rather than from a second
    /// literal that has to be kept in step by hand.
    /// </remarks>
    /// <returns>False when nothing could be written, which is already logged.</returns>
    public bool Ensure()
    {
        var shipped = Shipped.Value;

        try
        {
            if (!File.Exists(Path))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, shipped);
                _logger.LogInformation("Wrote a starting item list to {Path}", Path);

                Forget();
                return true;
            }

            var have = Sections(Read()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = Sections(shipped).Where(section => !have.Contains(section)).ToList();

            if (missing.Count == 0)
            {
                return true;
            }

            var appended = string.Concat(missing.Select(section =>
                Environment.NewLine + Block(shipped, section)));

            File.AppendAllText(Path, appended);
            _logger.LogInformation(
                "Added {Count} section(s) to {Path}: {Sections}", missing.Count, Path, missing);

            Forget();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not fatal. Every list falls back to the shipped copy, so the window still
            // offers everything — the player just cannot edit it until this is sorted out.
            _logger.LogWarning(e, "Could not write {Path}; offering the built-in lists instead", Path);
            return false;
        }
    }

    /// <summary>What the player's file offers for one list, or the shipped copy if it says nothing.</summary>
    public IReadOnlyList<string> Offer(ItemList list)
    {
        var mine = Values(Read(), list);

        if (mine.Count > 0)
        {
            return mine;
        }

        _logger.LogInformation(
            "{Path} has no [{Section}] {Prefix}* entries; offering the built-in list",
            Path, list.Section, list.KeyPrefix);

        return Values(Shipped.Value, list);
    }

    /// <summary>Reads the file again, for a player who edited it while the launcher was open.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _text = null;
        }
    }

    /// <summary>
    /// Takes the values of one list out of an INI's text.
    /// </summary>
    /// <remarks>
    /// Order is the file's, because it is the order the player sees in the dropdown and
    /// they arranged it. Repeats are dropped: two identical rows in a list of choices are
    /// never what was meant, and the reference offered them.
    /// </remarks>
    public static IReadOnlyList<string> Values(string text, ItemList list)
    {
        ArgumentNullException.ThrowIfNull(text);

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in Lines(text, list.Section))
        {
            var split = line.IndexOf('=', StringComparison.Ordinal);

            if (split < 0)
            {
                continue;
            }

            if (!line.AsSpan(0, split).Trim().StartsWith(list.KeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(split + 1)..].Trim();

            if (value.Length > 0 && seen.Add(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>The sections an INI's text declares, in the order it declares them.</summary>
    public static IReadOnlyList<string> Sections(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var names = new List<string>();

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (Header(line.Trim()) is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// One whole section of an INI's text, from its header to the next one.
    /// </summary>
    /// <remarks>
    /// Comments inside the section come with it. They are what tells a player what the
    /// section is for, and a section appended without them is a list of bare names.
    /// </remarks>
    public static string Block(string text, string section)
    {
        ArgumentNullException.ThrowIfNull(text);

        var block = new List<string>();
        var inside = false;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var name = Header(line.Trim());

            if (name is not null)
            {
                if (inside)
                {
                    break;
                }

                inside = name.Equals(section, StringComparison.OrdinalIgnoreCase);
            }

            if (inside)
            {
                block.Add(line.ToString());
            }
        }

        // Trailing blank lines belong to whatever follows, not to this.
        while (block.Count > 0 && block[^1].Trim().Length == 0)
        {
            block.RemoveAt(block.Count - 1);
        }

        return block.Count == 0 ? string.Empty : string.Join(Environment.NewLine, block) + Environment.NewLine;
    }

    /// <summary>The lines inside one section, comments and blanks already dropped.</summary>
    private static IEnumerable<string> Lines(string text, string section)
    {
        var inside = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (Header(line) is { } name)
            {
                inside = name.Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inside)
            {
                yield return line;
            }
        }
    }

    /// <summary>The name in a section header, or null for any other line.</summary>
    private static string? Header(ReadOnlySpan<char> line) =>
        line.Length >= 2 && line[0] == '[' && line[^1] == ']'
            ? line[1..^1].Trim().ToString()
            : null;

    /// <summary>
    /// The file's text, read once.
    /// </summary>
    /// <remarks>
    /// The reference read and parsed the whole file again for every dropdown it filled —
    /// five reads to open one tab. It is a small file, but it is also on disk, and the
    /// window fills these while a game is being played.
    /// </remarks>
    private string Read()
    {
        lock (_gate)
        {
            if (_text is not null)
            {
                return _text;
            }

            try
            {
                // Through the codec, not as UTF-8. A player who edited this in an older
                // editor has a code page 950 file, and the reference silently read that
                // as nothing at all — an empty dropdown with no explanation.
                _text = File.Exists(Path) ? _codec.ReadTextFile(Path) : string.Empty;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(e, "Could not read {Path}; offering the built-in lists", Path);
                _text = string.Empty;
            }

            return _text;
        }
    }

    /// <summary>The copy that ships inside the launcher.</summary>
    private static readonly Lazy<string> Shipped = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName)
                           ?? throw new InvalidOperationException($"{FileName} is not embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });
}
