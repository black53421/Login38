using System.Collections.Frozen;
using System.Globalization;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Game;

/// <summary>
/// What kind of thing each sprite draws, read out of the client's own sprite table.
/// </summary>
/// <remarks>
/// <para>
/// The client loads its sprite definitions as text and keeps the whole blob in the heap.
/// Each record is a sprite number followed by lines of properties, one of which —
/// <c>102.type(n)</c> — says what the sprite is: a monster, a piece of scenery, a door. A
/// record can also be declared as an alias of another (<c>1234=1200</c>), in which case its
/// type is whatever the sprite it points at ends up with.
/// </para>
/// <para>
/// This matters to the monster colours because the level byte on a world entity is only a
/// level when the entity is a monster. On anything else it is some other field entirely,
/// and colouring by it paints doors and signposts.
/// </para>
/// <para>
/// Loaded once and kept. The client does not rewrite this table, and the scan that finds it
/// is a walk of the whole heap.
/// </para>
/// </remarks>
internal sealed class SpriteTypes
{
    /// <summary>The type number that means "a monster".</summary>
    internal const byte Monster = 10;

    /// <summary>How the type of a record is written.</summary>
    private static ReadOnlySpan<byte> TypeProperty => "102.type("u8;

    /// <summary>
    /// How many records have to come out of a region before it is believed to be the table.
    /// </summary>
    /// <remarks>
    /// The marker appears in the loader's own code and in whatever the client has most
    /// recently parsed, so finding it is not enough. The real table has thousands.
    /// </remarks>
    private const int Credible = 100;

    /// <summary>The largest region worth pulling across the process boundary whole.</summary>
    private const int LargestRegion = 64 * 1024 * 1024;

    /// <summary>1 MB per read while looking for the marker.</summary>
    private const int ChunkSize = 0x10_0000;

    /// <summary>How far an alias chain is followed before it is called a cycle.</summary>
    private const int LongestChain = 16;

    private readonly ILogger _logger;

    private FrozenDictionary<ushort, byte> _bySprite = FrozenDictionary<ushort, byte>.Empty;

    internal SpriteTypes(ILogger logger) => _logger = logger;

    /// <summary>Whether the table has been found.</summary>
    internal bool Loaded => _bySprite.Count > 0;

    /// <summary>
    /// What a sprite draws, or null while the table has not been found.
    /// </summary>
    /// <remarks>
    /// Null is not "nothing": the caller has a weaker rule for entities whose sprite is
    /// unknown, because refusing to colour anything until the table turns up would mean
    /// the feature does nothing for the first minute of a session.
    /// </remarks>
    internal byte? For(ushort sprite) => _bySprite.TryGetValue(sprite, out var type) ? type : null;

    /// <summary>
    /// Looks for the table, once.
    /// </summary>
    /// <returns>Whether it is loaded now.</returns>
    internal bool Load(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (Loaded)
        {
            return true;
        }

        foreach (var region in process.Regions(HeapWalk.HeapStart, HeapWalk.HeapEnd))
        {
            if (region.Kind == MemoryKind.Image || region.Size <= TypeProperty.Length)
            {
                continue;
            }

            if (!Holds(process, region))
            {
                continue;
            }

            if (region.Size > LargestRegion)
            {
                // Rather than reading the first however-many megabytes and parsing half a
                // table: say so, and carry on with the weaker rule.
                _logger.LogWarning(
                    "The sprite table may be in the {Size} MB region at {Region}, which is too big to read whole",
                    region.Size / (1024 * 1024), region.Start);

                continue;
            }

            var raw = new byte[region.Size];

            if (!process.TryReadBytes(region.Start, raw))
            {
                continue;
            }

            var parsed = Parse(raw);

            if (parsed.Count < Credible)
            {
                continue;
            }

            _bySprite = parsed.ToFrozenDictionary();

            _logger.LogInformation(
                "Read {Count} sprite types from the client's table at {Region}", parsed.Count, region.Start);

            return true;
        }

        return false;
    }

    /// <summary>Whether the marker appears anywhere in a region.</summary>
    /// <remarks>
    /// Chunked, with an overlap, so the region is not pulled across whole until there is
    /// reason to think it is the right one.
    /// </remarks>
    private static bool Holds(RemoteProcess process, MemoryRegion region)
    {
        var overlap = TypeProperty.Length - 1;
        var buffer = new byte[ChunkSize + overlap];

        for (var offset = 0u; offset < region.Size;)
        {
            var take = (int)Math.Min(buffer.Length, region.Size - offset);
            var window = buffer.AsSpan(0, take);

            if (!process.TryReadBytes(region.Start + offset, window))
            {
                return false;
            }

            if (window.IndexOf(TypeProperty) >= 0)
            {
                return true;
            }

            if (take < buffer.Length)
            {
                return false;
            }

            offset += ChunkSize;
        }

        return false;
    }

    /// <summary>
    /// Reads sprite types out of the client's table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Byte for byte rather than decoded first. The table is ASCII property names around
    /// names in the client's own code page, and nothing here looks at the names — decoding
    /// two megabytes of Big5 to find the digits in <c>102.type(10)</c> would be work done
    /// only to be thrown away.
    /// </para>
    /// <para>
    /// The reference decodes it as UTF-8 with replacement, which for a client running on a
    /// GBK code page turns a good part of the table into replacement characters. It happens
    /// to survive that because a multi-byte sequence can never swallow a following ASCII
    /// byte, so the property names stay intact — but that is luck rather than design.
    /// </para>
    /// </remarks>
    internal static Dictionary<ushort, byte> Parse(ReadOnlySpan<byte> raw)
    {
        Dictionary<ushort, byte> direct = [];
        Dictionary<ushort, ushort> aliases = [];
        ushort? current = null;

        foreach (var line in Lines(raw))
        {
            if (Header(line) is { } header)
            {
                current = header.Sprite;

                if (header.Alias is { } target)
                {
                    aliases[header.Sprite] = target;
                }
            }

            if (current is { } record && Type(line) is { } type)
            {
                direct[record] = type;
            }
        }

        return Resolve(direct, aliases);
    }

    /// <summary>Splits the blob on the two things the client separates records with.</summary>
    private static List<byte[]> Lines(ReadOnlySpan<byte> raw)
    {
        // Materialised rather than yielded as spans: a span cannot cross an iterator
        // boundary, and the table is parsed once per session.
        List<byte[]> lines = [];
        var start = 0;

        for (var i = 0; i <= raw.Length; i++)
        {
            if (i != raw.Length && raw[i] != (byte)'\n' && raw[i] != 0)
            {
                continue;
            }

            var end = i;

            if (end > start && raw[end - 1] == (byte)'\r')
            {
                end--;
            }

            lines.Add(raw[start..end].ToArray());
            start = i + 1;
        }

        return lines;
    }

    /// <summary>
    /// Reads a record header: the sprite number, and the sprite it copies if it is an alias.
    /// </summary>
    /// <returns>Null when the line is not a header.</returns>
    private static (ushort Sprite, ushort? Alias)? Header(ReadOnlySpan<byte> line)
    {
        if (line.Length > 0 && line[0] == (byte)'#')
        {
            line = line[1..];
        }

        if (line.Length == 0 || !IsDigit(line[0]))
        {
            return null;
        }

        var digits = 0;

        while (digits < line.Length && IsDigit(line[digits]))
        {
            digits++;
        }

        if (!Number(line[..digits], out ushort sprite))
        {
            return null;
        }

        var rest = TrimStart(line[digits..]);

        if (rest.Length == 0)
        {
            return (sprite, null);
        }

        var token = 0;

        while (token < rest.Length && !IsSpace(rest[token]))
        {
            token++;
        }

        var equals = rest[..token].IndexOf((byte)'=');

        return equals < 0 || !Number(rest[(equals + 1)..token], out ushort alias)
            ? (sprite, null)
            : (sprite, alias);
    }

    /// <summary>Reads the type out of a property line.</summary>
    private static byte? Type(ReadOnlySpan<byte> line)
    {
        var at = line.IndexOf(TypeProperty);

        if (at < 0)
        {
            return null;
        }

        var tail = line[(at + TypeProperty.Length)..];
        var close = tail.IndexOf((byte)')');

        return close < 0 || !Number(Trim(tail[..close]), out byte type) ? null : type;
    }

    /// <summary>
    /// Gives every record a type, following aliases to whichever record declares one.
    /// </summary>
    private static Dictionary<ushort, byte> Resolve(
        Dictionary<ushort, byte> direct, Dictionary<ushort, ushort> aliases)
    {
        Dictionary<ushort, byte> resolved = new(direct.Count + aliases.Count);

        foreach (var sprite in direct.Keys.Concat(aliases.Keys))
        {
            var current = sprite;

            for (var hop = 0; hop < LongestChain; hop++)
            {
                if (direct.TryGetValue(current, out var type))
                {
                    resolved[sprite] = type;
                    break;
                }

                if (!aliases.TryGetValue(current, out current))
                {
                    break;
                }
            }
        }

        return resolved;
    }

    private static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> span)
    {
        var at = 0;

        while (at < span.Length && IsSpace(span[at]))
        {
            at++;
        }

        return span[at..];
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
    {
        span = TrimStart(span);

        while (span.Length > 0 && IsSpace(span[^1]))
        {
            span = span[..^1];
        }

        return span;
    }

    private static bool Number<T>(ReadOnlySpan<byte> digits, out T value)
        where T : struct, System.Numerics.INumberBase<T> =>
        T.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
