using System.Globalization;
using System.Text;

namespace Login38.Core.Morph.SmoothRun;

/// <summary>
/// Writes the table back out with the run cycles folded in.
/// </summary>
/// <remarks>
/// A copy of the original lines with three edits: dash variants and unaddressable actions
/// are dropped, sprite headers are widened where they need to be, and slots 98 and 99 are
/// appended to each sprite that got a run cycle. Everything else — comments, spacing,
/// directives this code does not understand — passes through untouched, because the table
/// belongs to the operator and only the parts this understands are its business.
/// </remarks>
public static class SmoothRunEmitter
{
    /// <summary>The action number that carries per-frame timing.</summary>
    private const uint FrameRateAction = 110;

    /// <summary>Renders the table.</summary>
    public static string Emit(SpriteFile file, IReadOnlyDictionary<ushort, RunPair> runCycles)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(runCycles);

        var owner = OwnerPerLine(file);
        var dropped = DroppedLines(file, owner);
        var lastKeptLine = LastKeptLinePerSprite(file, owner, dropped);

        // Last one wins, as everywhere else here. A table with two entries for the same id
        // is malformed, but it is the operator's file and refusing to write it out at all
        // would be a worse answer than treating the later entry as the live one.
        var spritesById = new Dictionary<ushort, Sprite>();

        foreach (var sprite in file.Sprites)
        {
            spritesById[sprite.Id] = sprite;
        }

        var output = new StringBuilder(file.RawLines.Sum(line => line.Length + 1));

        for (var index = 0; index < file.RawLines.Count; index++)
        {
            if (dropped[index])
            {
                continue;
            }

            var line = file.RawLines[index];
            var sprite = owner[index] is { } id ? spritesById[id] : null;
            var runCycle = sprite is not null ? runCycles.GetValueOrDefault(sprite.Id) : null;

            // Widening and appending can both apply to the same line, when a sprite's whole
            // entry is written on its header. The reference returned early after widening,
            // which left exactly those sprites with a header claiming frames that were
            // never added.
            output.Append(
                sprite is not null && runCycle is not null && sprite.HeaderLineIndex == index &&
                runCycle.SourceImageCount > sprite.ImageCount
                    ? MorphTextSyntax.WithImageCount(line, runCycle.SourceImageCount)
                    : line);

            if (sprite is not null && runCycle is not null && lastKeptLine[sprite.Id] == index)
            {
                Append(output, runCycle);
            }

            if (index < file.RawLines.Count - 1)
            {
                output.Append('\n');
            }
        }

        // The split that produced the lines drops a trailing newline into a final empty
        // element, which is reproduced above — unless that element was itself dropped.
        if (file.EndsWithNewline && (output.Length == 0 || output[^1] != '\n'))
        {
            output.Append('\n');
        }

        return output.ToString();
    }

    private static void Append(StringBuilder output, RunPair runCycle)
    {
        // Timing is emitted only alongside frames. On its own it would change the speed of
        // the walk that is already there.
        if (runCycle.HasFrames && runCycle.FrameRate is { } frameRate)
        {
            output.Append(CultureInfo.InvariantCulture, $"\n\t{FrameRateAction}.framerate({frameRate})");
        }

        AppendSlot(output, RunSlot.Left, runCycle.Left);
        AppendSlot(output, RunSlot.Right, runCycle.Right);
    }

    /// <remarks>
    /// Written as <c>walk</c> because that is what the client's parser expects to find in an
    /// action slot. The name carries no meaning past that point.
    /// </remarks>
    private static void AppendSlot(StringBuilder output, RunSlot slot, string? content)
    {
        if (content is not null)
        {
            output.Append(CultureInfo.InvariantCulture, $"\n\t{(int)slot}.walk({content})");
        }
    }

    /// <summary>Which sprite each line belongs to.</summary>
    /// <remarks>
    /// A sprite owns every line from its header until the next header, whatever is on them.
    /// Lines before the first header belong to nothing.
    /// </remarks>
    private static ushort?[] OwnerPerLine(SpriteFile file)
    {
        var owner = new ushort?[file.RawLines.Count];
        var starts = file.Sprites.ToDictionary(s => s.HeaderLineIndex, s => s.Id);
        ushort? current = null;

        for (var index = 0; index < owner.Length; index++)
        {
            if (starts.TryGetValue(index, out var id))
            {
                current = id;
            }

            owner[index] = current;
        }

        return owner;
    }

    /// <summary>Lines that do not survive into the output.</summary>
    private static bool[] DroppedLines(SpriteFile file, ushort?[] owner)
    {
        var dropped = new bool[file.RawLines.Count];

        // Dash variants: their frames are now in slots 98 and 99, and the client's parser
        // does not understand the syntax.
        foreach (var sprite in file.Sprites)
        {
            foreach (var action in sprite.Actions)
            {
                if (action.DashVariant is not null)
                {
                    dropped[action.LineIndex] = true;
                }
            }
        }

        for (var index = 0; index < file.RawLines.Count; index++)
        {
            // Actions past what the client can address. Only inside a sprite: a bare number
            // outside one is somebody's comment, not an action.
            if (!dropped[index] && owner[index] is not null &&
                MorphTextSyntax.ActionNumber(file.RawLines[index].AsSpan().TrimStart())
                    is { } action && action >= WalkActions.SlotLimit)
            {
                dropped[index] = true;
            }
        }

        return dropped;
    }

    /// <summary>
    /// The last surviving line of each sprite, which is where its new slots go.
    /// </summary>
    /// <remarks>
    /// It has to be a surviving line: appending after one that is about to be dropped would
    /// take the new slots with it.
    /// </remarks>
    private static Dictionary<ushort, int> LastKeptLinePerSprite(
        SpriteFile file, ushort?[] owner, bool[] dropped)
    {
        var last = new Dictionary<ushort, int>();

        foreach (var sprite in file.Sprites)
        {
            var line = sprite.HeaderLineIndex;

            for (var index = sprite.HeaderLineIndex + 1; index < file.RawLines.Count; index++)
            {
                if (owner[index] != sprite.Id)
                {
                    break;
                }

                if (!dropped[index])
                {
                    line = index;
                }
            }

            last[sprite.Id] = line;
        }

        return last;
    }
}
