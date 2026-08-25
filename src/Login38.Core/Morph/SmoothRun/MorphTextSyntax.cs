using System.Globalization;

namespace Login38.Core.Morph.SmoothRun;

/// <summary>
/// Reading the morph table's own syntax.
/// </summary>
/// <remarks>
/// <para>
/// The format is a sprite header per entry and one line per action:
/// </para>
/// <code>
/// 300 0 41210                       file header
/// #16140 72=5373 keina walk         id, image count, shared graphic id, name
///     0.walk(1 8,8.0:2 8.1:2 ...)   action, name, (direction frames,frame data)
///     0-1.RunL(1 8,16.0:2 ...)      a dash variant
///     110.framerate(1 8,1 1 ...)    per-frame timing for the actions that follow
/// </code>
/// <para>
/// Some tables put a whole entry on the header line instead, with the actions appended
/// after the name. Both forms occur in the same file, so both are read.
/// </para>
/// </remarks>
public static class MorphTextSyntax
{
    /// <summary>Everything between the first <c>(</c> and the last <c>)</c>.</summary>
    public static ReadOnlySpan<char> FrameContent(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var trimmed = line.AsSpan().TrimStart();
        var open = trimmed.IndexOf('(');
        var close = trimmed.LastIndexOf(')');

        return open < 0 || close <= open + 1 ? [] : trimmed[(open + 1)..close];
    }

    /// <summary>The sprite id of a <c>#id ...</c> header.</summary>
    public static ushort? SpriteId(ReadOnlySpan<char> trimmed)
    {
        if (trimmed.IsEmpty || trimmed[0] != '#')
        {
            return null;
        }

        var digits = LeadingDigits(trimmed[1..]);

        return digits.IsEmpty ? null : ushort.TryParse(digits, out var id) ? id : null;
    }

    /// <summary>
    /// The image count, shared graphic id and name of a sprite header.
    /// </summary>
    /// <remarks>
    /// The graphic id is what links a walking sprite to the separate sprite that holds its
    /// run cycle, so a header without one cannot take part in that mapping.
    /// </remarks>
    public static bool TryReadHeader(
        ReadOnlySpan<char> trimmed, out uint imageCount, out uint? graphicId, out Range nameRange)
    {
        imageCount = 0;
        graphicId = null;
        nameRange = default;

        if (trimmed.IsEmpty || trimmed[0] != '#')
        {
            return false;
        }

        var afterHash = 1 + LeadingDigits(trimmed[1..]).Length;

        if (afterHash >= trimmed.Length)
        {
            // Nothing after the id, so there is no header body to read.
            return false;
        }

        var rest = afterHash + CountLeadingWhitespace(trimmed[afterHash..]);
        var count = LeadingDigits(trimmed[rest..]);

        if (count.IsEmpty || !uint.TryParse(count, out imageCount))
        {
            return false;
        }

        var afterCount = rest + count.Length;

        if (afterCount < trimmed.Length && trimmed[afterCount] == '=')
        {
            var graphic = LeadingDigits(trimmed[(afterCount + 1)..]);

            if (!graphic.IsEmpty && uint.TryParse(graphic, out var parsed))
            {
                graphicId = parsed;
                nameRange = TrimmedRange(trimmed, afterCount + 1 + graphic.Length);
                return true;
            }
        }

        nameRange = TrimmedRange(trimmed, afterCount);
        return true;
    }

    /// <summary>
    /// Reads an <c>N-V.</c> prefix.
    /// </summary>
    /// <returns>False when the line does not start with one.</returns>
    public static bool TryReadDashVariant(ReadOnlySpan<char> trimmed, out uint action, out uint variant)
    {
        action = 0;
        variant = 0;

        var actionDigits = LeadingDigits(trimmed);

        if (actionDigits.IsEmpty || !uint.TryParse(actionDigits, out action))
        {
            return false;
        }

        var afterAction = trimmed[actionDigits.Length..];

        if (afterAction.IsEmpty || afterAction[0] != '-')
        {
            return false;
        }

        var variantDigits = LeadingDigits(afterAction[1..]);

        if (variantDigits.IsEmpty || !uint.TryParse(variantDigits, out variant))
        {
            return false;
        }

        var afterVariant = afterAction[(1 + variantDigits.Length)..];

        return !afterVariant.IsEmpty && afterVariant[0] == '.';
    }

    /// <summary>The <c>N</c> of an <c>N.name(...)</c> line.</summary>
    public static uint? ActionNumber(ReadOnlySpan<char> trimmed)
    {
        var digits = LeadingDigits(trimmed);

        if (digits.IsEmpty || digits.Length >= trimmed.Length || trimmed[digits.Length] != '.')
        {
            return null;
        }

        return uint.TryParse(digits, out var action) ? action : null;
    }

    /// <summary>The name between the action's dot and its opening parenthesis.</summary>
    public static ReadOnlySpan<char> ActionName(ReadOnlySpan<char> trimmed)
    {
        var dot = trimmed.IndexOf('.');

        if (dot < 0)
        {
            return [];
        }

        var afterDot = trimmed[(dot + 1)..];
        var open = afterDot.IndexOf('(');

        return (open < 0 ? afterDot : afterDot[..open]).Trim();
    }

    /// <summary>Splits frame content into its header and its frames.</summary>
    /// <returns>False when there is no comma, or nothing after it.</returns>
    public static bool TrySplitContent(string content, out ReadOnlySpan<char> header, out string[] frames)
    {
        ArgumentNullException.ThrowIfNull(content);

        header = default;
        frames = [];

        var comma = content.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            return false;
        }

        frames = content[(comma + 1)..].Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (frames.Length == 0)
        {
            return false;
        }

        header = content.AsSpan(..comma);
        return true;
    }

    /// <summary>The image index the first frame draws from.</summary>
    public static uint? FirstSprite(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var comma = content.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            return null;
        }

        var after = content.AsSpan((comma + 1)..).TrimStart();
        var dot = after.IndexOf('.');

        return dot < 0 ? null : uint.TryParse(after[..dot].Trim(), out var value) ? value : null;
    }

    /// <summary>The row a frame draws from — the number before its dot.</summary>
    public static uint? FrameRow(string frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var dot = frame.IndexOf('.', StringComparison.Ordinal);

        return dot < 0 ? null : uint.TryParse(frame.AsSpan(..dot), out var row) ? row : null;
    }

    /// <summary>
    /// Whether two actions are the halves of one run cycle, interleaved.
    /// </summary>
    /// <remarks>
    /// The giveaway is that both sides play the same rows in opposite order: one starts on
    /// the left foot and the other on the right, and from the second frame on each plays
    /// the row the other started with. That pattern identifies a run source in tables where
    /// every action name has been stripped.
    /// </remarks>
    public static bool IsInterleavedRunPair(string first, string second)
    {
        if (!TrySplitContent(first, out var firstHeader, out var firstFrames) ||
            !TrySplitContent(second, out var secondHeader, out var secondFrames))
        {
            return false;
        }

        if (!firstHeader.Trim().SequenceEqual(secondHeader.Trim()) ||
            firstFrames.Length != secondFrames.Length ||
            firstFrames.Length < 2)
        {
            return false;
        }

        if (FrameRow(firstFrames[0]) is not { } firstRow ||
            FrameRow(secondFrames[0]) is not { } secondRow ||
            firstRow == secondRow)
        {
            return false;
        }

        for (var i = 1; i < firstFrames.Length; i++)
        {
            if (FrameRow(firstFrames[i]) != secondRow || FrameRow(secondFrames[i]) != firstRow)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Rewrites a sprite header's image count.
    /// </summary>
    /// <remarks>
    /// The count is the first number after the id, and only that number is replaced. The
    /// reference searched the whole line for the old count's digits, which finds them
    /// inside the sprite id first whenever the id contains them — <c>#3201 320=…</c> with a
    /// count of 320 rewrote the id.
    /// </remarks>
    public static string WithImageCount(string headerLine, uint newCount)
    {
        ArgumentNullException.ThrowIfNull(headerLine);

        var leading = CountLeadingWhitespace(headerLine);
        var span = headerLine.AsSpan(leading);

        if (span.IsEmpty || span[0] != '#')
        {
            return headerLine;
        }

        var afterId = 1 + LeadingDigits(span[1..]).Length;
        var countStart = afterId + CountLeadingWhitespace(span[afterId..]);
        var count = LeadingDigits(span[countStart..]);

        if (count.IsEmpty)
        {
            return headerLine;
        }

        var absolute = leading + countStart;

        return string.Concat(
            headerLine.AsSpan(..absolute),
            newCount.ToString(CultureInfo.InvariantCulture),
            headerLine.AsSpan(absolute + count.Length));
    }

    /// <summary>
    /// Finds one action inside a line that carries a whole entry.
    /// </summary>
    /// <remarks>
    /// Parenthesis-aware, because frame data contains no parentheses but action names can,
    /// and because an <c>N.</c> inside another action's content is not an action of its own.
    /// Only a digit run at nesting depth zero, preceded by whitespace, a closing
    /// parenthesis or the start of the line, starts one.
    /// </remarks>
    public static bool TryReadInlineAction(
        string line, uint target, out string content, out string name)
    {
        ArgumentNullException.ThrowIfNull(line);

        content = string.Empty;
        name = string.Empty;

        var depth = 0;

        for (var i = 0; i < line.Length; i++)
        {
            switch (line[i])
            {
                case '(':
                    depth++;
                    continue;

                case ')':
                    depth--;
                    continue;
            }

            if (depth != 0 || !char.IsAsciiDigit(line[i]) ||
                (i != 0 && line[i - 1] is not (' ' or '\t' or ')')))
            {
                continue;
            }

            var digits = LeadingDigits(line.AsSpan(i));
            var afterDigits = i + digits.Length;

            if (afterDigits < line.Length && line[afterDigits] == '.' &&
                uint.TryParse(digits, out var action) && action == target &&
                TryReadParenthesised(line, afterDigits, out content, out name))
            {
                return true;
            }

            // Past the whole run either way: a digit inside it cannot start another action.
            i = afterDigits - 1;
        }

        return false;
    }

    private static bool TryReadParenthesised(string line, int dot, out string content, out string name)
    {
        content = string.Empty;
        name = string.Empty;

        var open = line.IndexOf('(', dot);

        if (open < 0)
        {
            return false;
        }

        var close = line.IndexOf(')', open + 1);

        if (close < 0)
        {
            return false;
        }

        name = line[(dot + 1)..open].Trim();
        content = line[(open + 1)..close];
        return true;
    }

    private static ReadOnlySpan<char> LeadingDigits(ReadOnlySpan<char> text)
    {
        var length = 0;

        while (length < text.Length && char.IsAsciiDigit(text[length]))
        {
            length++;
        }

        return text[..length];
    }

    private static int CountLeadingWhitespace(ReadOnlySpan<char> text)
    {
        var length = 0;

        while (length < text.Length && char.IsWhiteSpace(text[length]))
        {
            length++;
        }

        return length;
    }

    private static Range TrimmedRange(ReadOnlySpan<char> text, int start)
    {
        var end = text.Length;

        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return start..end;
    }
}
