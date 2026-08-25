using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Login38.Core.Morph;

/// <summary>One row of the server's <c>spr_action</c> table.</summary>
/// <param name="SpriteId">Which sprite the action belongs to.</param>
/// <param name="ActionId">Which action of that sprite.</param>
/// <param name="FrameCount">How many frames it draws.</param>
/// <param name="FrameRate">How fast, in frames per second.</param>
public readonly record struct SprAction(uint SpriteId, uint ActionId, uint FrameCount, uint FrameRate);

/// <summary>What generating the table produced.</summary>
/// <param name="Sql">The statements, one per line.</param>
/// <param name="Rows">How many there are.</param>
public readonly record struct SprActionSqlResult(string Sql, int Rows);

/// <summary>
/// Builds the server's <c>spr_action</c> table out of the client's own sprite table.
/// </summary>
/// <remarks>
/// <para>
/// A server needs to know how long each animation is so that it can time what a character
/// is doing against what the client is drawing. That is written down in exactly one place —
/// the client's <c>SPR.txt</c> — and this reads it out.
/// </para>
/// <para>
/// The reading is deliberately blunt, and has to be: <c>SPR.txt</c> is decades of hand
/// edits, with names, comments and labels in several languages sitting between the numbers.
/// So everything that could not be part of a number is turned into a separator and what is
/// left is read positionally. That is what the client's own loader does, and matching it
/// matters more than being able to explain any one line.
/// </para>
/// </remarks>
public static class SprActionSql
{
    /// <summary>What an action runs at until a line says otherwise.</summary>
    public const uint DefaultFrameRate = 24;

    /// <summary>The action that is not an action: it sets the rate for the ones after it.</summary>
    public const uint FrameRateAction = 110;

    /// <summary>
    /// Where the frame counts start in a command, and how far apart they are.
    /// </summary>
    /// <remarks>
    /// A command reads <c>action, loop, parts</c> and then a run of <c>image, frame,
    /// count</c> triples, so the counts are every third token from the sixth. Nothing in
    /// the file says so; it is the shape the client's loader reads.
    /// </remarks>
    private const int FirstFrameCount = 5;

    /// <inheritdoc cref="FirstFrameCount"/>
    private const int FrameCountStride = 3;

    /// <summary>
    /// The actions the server's table has a column for.
    /// </summary>
    /// <remarks>
    /// Everything else in the file — shadows, sounds, the type and size markers — is drawn
    /// by the client alone and means nothing to a server.
    /// </remarks>
    private static readonly FrozenSet<uint> Supported = new uint[]
    {
        0, 1, 4, 5, 11, 12, 17, 18, 19, 20, 21, 24, 25, 28, 29, 30, 31,
        40, 41, 46, 47, 50, 51, 54, 55, 58, 59, 62, 63, 66, 67,
    }.ToFrozenSet();

    /// <summary>Reads a sprite table and writes the statements that fill the server's.</summary>
    /// <exception cref="FormatException">The text holds no sprite at all.</exception>
    public static SprActionSqlResult Generate(string text)
    {
        var actions = Read(text);

        return new SprActionSqlResult(ToSql(actions), actions.Count);
    }

    /// <summary>
    /// Reads every action the server has a row for.
    /// </summary>
    /// <exception cref="FormatException">The text holds no sprite at all.</exception>
    public static IReadOnlyList<SprAction> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var start = text.IndexOf('#', StringComparison.Ordinal);

        if (start < 0)
        {
            throw new FormatException("The table holds no sprite: nothing in it starts with '#'.");
        }

        var actions = new List<SprAction>();

        // What a reference copies from, by sprite and action. The reference implementation
        // scanned everything read so far for each one, which on a real table — tens of
        // thousands of rows — is the whole cost of the operation.
        var found = new Dictionary<(uint Sprite, uint Action), SprAction>();

        var sprite = 0u;
        var reading = false;
        var rate = DefaultFrameRate;
        var tokens = new List<string>();

        foreach (var line in text.AsSpan(start).EnumerateLines())
        {
            Tokens(line, tokens);

            if (tokens.Count == 0)
            {
                continue;
            }

            var first = tokens[0];

            if (first[0] == '#')
            {
                sprite = Number(first.AsSpan(1))
                         ?? throw new FormatException($"'{first}' does not name a sprite.");
                reading = true;

                // The rest of the header line is the image count and a label, which may be
                // digits. Neither is a command.
                continue;
            }

            if (!reading)
            {
                continue;
            }

            if (first.Contains('=', StringComparison.Ordinal))
            {
                if (Reference(first) is { } copy &&
                    found.TryGetValue((copy.Sprite, copy.Action), out var copied))
                {
                    Add(actions, found, copied with { SpriteId = sprite });
                }

                continue;
            }

            if (Number(first) is not { } code)
            {
                continue;
            }

            if (code == FrameRateAction)
            {
                if (tokens.Count > 1 && Number(tokens[1]) is { } given)
                {
                    rate = given;
                }

                continue;
            }

            if (!Supported.Contains(code))
            {
                continue;
            }

            Add(actions, found, new SprAction(sprite, code, Frames(tokens), rate));
        }

        return actions;
    }

    /// <summary>Writes the statements, one per row.</summary>
    public static string ToSql(IEnumerable<SprAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var sql = new StringBuilder();

        foreach (var action in actions)
        {
            // Every field is a number read out of the file, so there is nothing here that
            // could carry a quote — but they are written through the invariant culture all
            // the same, because a server importing this is not on the operator's machine.
            sql.Append(CultureInfo.InvariantCulture,
                $"INSERT INTO `spr_action` VALUES ('{action.SpriteId}', '{action.ActionId}', '{action.FrameCount}', '{action.FrameRate}');\n");
        }

        return sql.ToString();
    }

    /// <summary>
    /// Records a row, and remembers it as the one a later reference copies.
    /// </summary>
    /// <remarks>
    /// The first of its kind wins, which is what a scan from the beginning found.
    /// </remarks>
    private static void Add(
        List<SprAction> actions,
        Dictionary<(uint Sprite, uint Action), SprAction> found,
        SprAction action)
    {
        actions.Add(action);
        found.TryAdd((action.SpriteId, action.ActionId), action);
    }

    /// <summary>Adds up how many frames a command draws.</summary>
    private static uint Frames(List<string> tokens)
    {
        var total = 0u;

        for (var at = FirstFrameCount; at < tokens.Count; at += FrameCountStride)
        {
            if (Number(tokens[at]) is { } count)
            {
                // Saturating: a table with a nonsense count should produce a row a server
                // will reject, not a row that wrapped round to something plausible.
                total = total > uint.MaxValue - count ? uint.MaxValue : total + count;
            }
        }

        return total;
    }

    /// <summary>
    /// Reads <c>action=sprite</c>, which is how the table says "the same as that one".
    /// </summary>
    /// <returns>Both, or null when either side does not begin with a number.</returns>
    private static (uint Action, uint Sprite)? Reference(string token)
    {
        // Only the first `=` separates: what follows may carry anything, and only its
        // leading digits are read.
        var split = token.IndexOf('=', StringComparison.Ordinal);

        return (Number(token.AsSpan(0, split)), Number(token.AsSpan(split + 1))) is
            ({ } action, { } sprite)
            ? (action, sprite)
            : null;
    }

    /// <summary>
    /// The number a token starts with, or null when it does not start with one.
    /// </summary>
    /// <remarks>
    /// The leading digits and no more, because the client's own loader hands these to
    /// <c>strtoul</c> and stops where it stops: <c>2&lt;478</c> is two.
    /// </remarks>
    private static uint? Number(ReadOnlySpan<char> token)
    {
        var end = 0;

        while (end < token.Length && char.IsAsciiDigit(token[end]))
        {
            end++;
        }

        return end > 0 && uint.TryParse(token[..end], CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Cuts one line into the numbers it holds.
    /// </summary>
    /// <remarks>
    /// Everything that cannot be part of a number separates: letters, punctuation, and
    /// anything outside ASCII — which is what makes a Chinese label between two numbers
    /// disappear rather than glue them together. A minus sign is the exception: kept when
    /// a digit follows it, so that a negative number stays one token and is then read as
    /// no number at all rather than as its digits.
    /// </remarks>
    private static void Tokens(ReadOnlySpan<char> line, List<string> into)
    {
        into.Clear();

        var start = -1;

        for (var at = 0; at < line.Length; at++)
        {
            var next = at + 1 < line.Length ? line[at + 1] : '\0';

            if (Separates(line[at], next))
            {
                if (start >= 0)
                {
                    into.Add(line[start..at].ToString());
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = at;
            }
        }

        if (start >= 0)
        {
            into.Add(line[start..].ToString());
        }
    }

    /// <inheritdoc cref="Tokens"/>
    private static bool Separates(char c, char next) =>
        !char.IsAscii(c)
        || char.IsAsciiLetter(c)
        || c is ' ' or '\t' or '\n' or '\f' or '\r'
        || c is '_' or '.' or '(' or ')' or ',' or ';' or ':' or '\''
        || (c == '-' && !char.IsAsciiDigit(next));
}
