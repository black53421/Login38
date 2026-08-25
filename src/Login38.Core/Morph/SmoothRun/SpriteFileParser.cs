namespace Login38.Core.Morph.SmoothRun;

/// <summary>
/// Turns a morph table into a <see cref="SpriteFile"/>.
/// </summary>
/// <remarks>
/// A lexer and nothing more: it decides what each line is, not what any of it means. Every
/// judgement about walking and running belongs to the stages after it.
/// </remarks>
public static class SpriteFileParser
{
    /// <summary>Reads a whole table.</summary>
    public static SpriteFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var rawLines = text.Split('\n');
        var sprites = new List<SpriteBuilder>();

        // The most recent 110 line, which applies to the actions after it and resets at
        // each sprite. Actions record it as they are read, because by the time a later
        // stage wants it the file has moved on.
        string? frameRate = null;

        for (var index = 0; index < rawLines.Length; index++)
        {
            var line = rawLines[index];
            var trimmed = line.AsSpan().TrimStart();

            if (!trimmed.IsEmpty && trimmed[0] == '#')
            {
                if (MorphTextSyntax.SpriteId(trimmed) is { } id)
                {
                    frameRate = null;
                    sprites.Add(ReadHeader(id, index, line, trimmed));
                }

                continue;
            }

            if (sprites.Count == 0)
            {
                continue;
            }

            ReadActionLine(sprites[^1], index, line, trimmed, ref frameRate);
        }

        return new SpriteFile(
            [.. sprites.Select(s => s.Build())], rawLines, text.EndsWith('\n'));
    }

    /// <summary>
    /// Reads a sprite header, including any actions written on the header line itself.
    /// </summary>
    /// <remarks>
    /// Some tables put a whole entry on one line. Those actions are recorded against the
    /// header's own line index, which is what stops the emitter from trying to delete or
    /// append after a line that carries the sprite's only content.
    /// </remarks>
    private static SpriteBuilder ReadHeader(
        ushort id, int index, string line, ReadOnlySpan<char> trimmed)
    {
        var sprite = MorphTextSyntax.TryReadHeader(trimmed, out var imageCount, out var graphicId, out _)
            ? new SpriteBuilder(id, index, imageCount, graphicId)
            : new SpriteBuilder(id, index, 0, null);

        var inlineText = line.AsSpan().TrimStart().ToString();

        foreach (var action in Enumerable.Concat(
                     WalkActions.All.ToArray(), WalkActions.InlineExtra.ToArray()))
        {
            if (MorphTextSyntax.TryReadInlineAction(inlineText, action, out var content, out var name) &&
                content.Length > 0 &&
                Build(index, action, dashVariant: null, content, name, frameRate: null) is { } parsed)
            {
                sprite.Actions.Add(parsed);
            }
        }

        return sprite;
    }

    private static void ReadActionLine(
        SpriteBuilder sprite, int index, string line, ReadOnlySpan<char> trimmed, ref string? frameRate)
    {
        if (MorphTextSyntax.TryReadDashVariant(trimmed, out var dashAction, out var variant))
        {
            // Only the two that mean a run cycle. Anything else with a dash is left where it
            // is, because the emitter deletes exactly the variants it collected and a
            // variant it did not understand has to survive untouched.
            if (variant is 1 or 2 &&
                Build(index, dashAction, variant, ContentOf(line), NameOf(trimmed), frameRate) is { } dash)
            {
                sprite.Actions.Add(dash);
            }

            return;
        }

        if (MorphTextSyntax.ActionNumber(trimmed) is not { } action)
        {
            return;
        }

        if (action == FrameRateAction)
        {
            var content = ContentOf(line);

            if (content.Length > 0)
            {
                frameRate = content;
            }
        }

        if (Build(index, action, dashVariant: null, ContentOf(line), NameOf(trimmed), frameRate) is { } parsed)
        {
            sprite.Actions.Add(parsed);
        }
    }

    /// <summary>The action number that carries per-frame timing rather than frames.</summary>
    private const uint FrameRateAction = 110;

    private static string ContentOf(string line) => MorphTextSyntax.FrameContent(line).ToString();

    private static string NameOf(ReadOnlySpan<char> trimmed) =>
        MorphTextSyntax.ActionName(trimmed).ToString().ToLowerInvariant().Trim();

    /// <summary>
    /// Builds an action, or nothing when the line does not carry usable frame data.
    /// </summary>
    /// <remarks>
    /// Both header fields have to be present, but only the direction has to parse. A frame
    /// count that is there and unreadable becomes zero, which no later check accepts — so
    /// the action survives into the emitter's copy of the file without being treated as a
    /// run cycle.
    /// </remarks>
    private static SpriteAction? Build(
        int index, uint action, uint? dashVariant, string content, string name, string? frameRate)
    {
        if (content.Length == 0 || !MorphTextSyntax.TrySplitContent(content, out var header, out _))
        {
            return null;
        }

        var fields = header.ToString().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length < 2 || !uint.TryParse(fields[0], out var direction))
        {
            return null;
        }

        var frameCount = uint.TryParse(fields[1], out var parsed) ? parsed : 0;

        return new SpriteAction(
            index, action, dashVariant, name, content, direction, frameCount,
            MorphTextSyntax.FirstSprite(content) ?? 0, frameRate);
    }

    private sealed class SpriteBuilder(ushort id, int headerLineIndex, uint imageCount, uint? graphicId)
    {
        public List<SpriteAction> Actions { get; } = [];

        public Sprite Build() => new(id, headerLineIndex, imageCount, graphicId, Actions);
    }
}
