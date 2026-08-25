namespace Login38.Core.Configuration;

/// <summary>One <c>key=value</c> line, together with the section it appeared under.</summary>
/// <param name="Section">Section name, lower-cased. Empty before the first section header.</param>
/// <param name="Key">Key, lower-cased and trimmed.</param>
/// <param name="Value">Value, trimmed. Case is preserved — it may be base64 or a URL.</param>
public readonly record struct IniEntry(string Section, string Key, string Value);

/// <summary>
/// A deliberately forgiving INI reader.
/// </summary>
/// <remarks>
/// <para>
/// The launcher's config files are also read and written by older third-party tools,
/// so the input is not guaranteed to be well formed. Anything unrecognised is skipped
/// rather than rejected: blank lines, <c>;</c> and <c>#</c> comments, lines with no
/// <c>=</c>, unknown sections and unknown keys. A stray line in a config file must
/// never stop the game from starting.
/// </para>
/// <para>
/// This is why <see cref="Microsoft.Extensions.Configuration"/>'s INI provider is not
/// used here: it throws on malformed input and on duplicate keys, which is the
/// opposite of what is wanted.
/// </para>
/// <para>
/// Values are split at the <em>first</em> <c>=</c>, so base64 padding and URLs
/// containing <c>=</c> survive intact.
/// </para>
/// </remarks>
public static class IniReader
{
    /// <summary>Reads every recognisable entry, in file order.</summary>
    public static IReadOnlyList<IniEntry> Read(string content)
    {
        // Materialised rather than yielded: line splitting uses spans, which cannot
        // cross a yield boundary. Config files here are a few dozen lines.
        var entries = new List<IniEntry>();
        var section = string.Empty;

        foreach (var rawLine in content.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].ToString().ToLowerInvariant();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim().ToString().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim().ToString();
            entries.Add(new IniEntry(section, key, value));
        }

        return entries;
    }
}
