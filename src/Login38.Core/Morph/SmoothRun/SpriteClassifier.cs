namespace Login38.Core.Morph.SmoothRun;

/// <summary>
/// Decides what each sprite is: something that walks, something that runs, or both.
/// </summary>
/// <remarks>
/// <para>
/// Operators publish tables built for several different clients, and the run cycles arrive
/// in whatever shape that client used. There is no version marker to switch on, so this
/// works from the content: four independent signals, any one of which is enough.
/// </para>
/// <list type="number">
/// <item>A dash variant — <c>0-1.RunL</c> — which only ever means a run cycle.</item>
/// <item>An action named <c>RunL</c> or <c>RunR</c>.</item>
/// <item>Actions 32 and 33 drawing from rows eight apart, which is a run cycle whose
/// names have been stripped.</item>
/// <item>Actions 0 and 4 doing the same, with a name that has not been repurposed.</item>
/// </list>
/// <para>
/// A fifth signal is cross-sprite and runs afterwards: within one shared graphic id, a
/// sprite whose first frame is sixteen or more rows above the lowest is drawing from a
/// second strip, and a second strip in a walking sprite's graphic is a run cycle.
/// </para>
/// </remarks>
public static class SpriteClassifier
{
    /// <summary>
    /// How far above the lowest first frame a sprite must draw to be a second strip.
    /// </summary>
    /// <remarks>
    /// Eight rows is one animation. Sixteen means the sprite has skipped a whole strip,
    /// which is what a run cycle stored after a walk cycle looks like.
    /// </remarks>
    private const uint SecondStripGap = 16;

    /// <summary>Classifies every sprite in a table.</summary>
    public static IReadOnlyDictionary<ushort, SpriteRole> Classify(SpriteFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var roles = new Dictionary<ushort, SpriteRole>();

        foreach (var sprite in file.Sprites)
        {
            roles[sprite.Id] = ClassifyOne(sprite);
        }

        PromoteSecondStrips(roles, file);
        return roles;
    }

    private static SpriteRole ClassifyOne(Sprite sprite)
    {
        var runs = HasRunSignal(sprite);
        var walks = HasWalkSignal(sprite, runs);

        var role = (walks, runs) switch
        {
            (true, true) => SpriteRole.Both,
            (true, false) => SpriteRole.Walk,
            (false, true) => SpriteRole.Run,
            _ => SpriteRole.None,
        };

        // A sprite whose 0 and 4 are the two halves of one run cycle has no walk of its own,
        // whatever those actions are numbered — so it is a source to map from, not a target
        // to map onto.
        return role == SpriteRole.Both && IsAdjacentStripPair(sprite) ? SpriteRole.Run : role;
    }

    /// <summary>
    /// Promotes sprites that draw from a second strip within their graphic.
    /// </summary>
    /// <remarks>
    /// The comparison is only meaningful inside one shared graphic id, and only when there
    /// is something to compare against — so a graphic with a single sprite is left alone
    /// however high its frames start.
    /// </remarks>
    private static void PromoteSecondStrips(Dictionary<ushort, SpriteRole> roles, SpriteFile file)
    {
        // Grouped by graphic id, in file order within each group. Only the lowest and
        // highest first frames decide anything, and each sprite is judged on its own, so
        // nothing here depends on the order — the sorted map is for a result that reads the
        // same twice rather than for correctness.
        var byGraphic = new SortedDictionary<uint, List<(ushort Id, uint FirstSprite)>>();

        foreach (var sprite in file.Sprites)
        {
            if (sprite.GraphicId is { } graphic && FindAction(sprite, 0) is { } first)
            {
                if (!byGraphic.TryGetValue(graphic, out var group))
                {
                    byGraphic[graphic] = group = [];
                }

                group.Add((sprite.Id, first.FirstSprite));
            }
        }

        foreach (var group in byGraphic.Values)
        {
            if (group.Count < 2)
            {
                continue;
            }

            var baseline = group.Min(entry => entry.FirstSprite);

            if (group.Max(entry => entry.FirstSprite) < baseline + SecondStripGap)
            {
                continue;
            }

            foreach (var (id, firstSprite) in group)
            {
                if (firstSprite >= baseline + SecondStripGap &&
                    roles.GetValueOrDefault(id) != SpriteRole.Run &&
                    file.Sprites.FirstOrDefault(s => s.Id == id) is { } sprite &&
                    IsSecondStripRunSource(sprite))
                {
                    roles[id] = SpriteRole.Run;
                }
            }
        }
    }

    private static bool IsSecondStripRunSource(Sprite sprite)
    {
        if (FindAction(sprite, 0) is not { } first)
        {
            return false;
        }

        if (FindAction(sprite, 4) is not { } second)
        {
            // Nothing to compare against, so the name has to say it outright. The frame
            // count keeps out the four-frame weapon walks, which are not run cycles however
            // high they start.
            return first.FrameCount == 8 && IsNamedRunSource(first.Name);
        }

        return MorphTextSyntax.IsInterleavedRunPair(first.Content, second.Content) ||
               IsNamedRunSource(first.Name) || IsNamedRunSource(second.Name);
    }

    /// <summary>Names that only ever appear on a run cycle.</summary>
    private static bool IsNamedRunSource(string name)
    {
        var trimmed = name.Trim();

        return trimmed.StartsWith("runl", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("runr", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("walkfast", StringComparison.OrdinalIgnoreCase) ||
               trimmed.ToLowerInvariant() is "runone" or "runtwo" or "run_one" or "run_two"
                   or "run one" or "run two";
    }

    /// <summary>
    /// Whether actions 0 and 4 are the two halves of one run cycle.
    /// </summary>
    /// <remarks>
    /// Both draw eight frames in one direction, from rows exactly eight apart — the left
    /// and right strips of a run. The name check keeps out the many sprites that legitimately
    /// have two walks eight rows apart; a name that has been left alone, or has been stripped
    /// entirely, is not a reason to reject the pair, but a name that says something else is.
    /// </remarks>
    internal static bool IsAdjacentStripPair(Sprite sprite)
    {
        if (FindAction(sprite, 0) is not { } left || FindAction(sprite, 4) is not { } right)
        {
            return false;
        }

        if (left.Direction != 1 || right.Direction != 1 ||
            left.FrameCount != 8 || right.FrameCount != 8 ||
            AbsoluteDifference(left.FirstSprite, right.FirstSprite) != 8)
        {
            return false;
        }

        return IsUnrepurposedName(left.Name) || IsUnrepurposedName(right.Name) ||
               MorphTextSyntax.IsInterleavedRunPair(left.Content, right.Content);
    }

    /// <summary>A name that has not been given to something other than walking or running.</summary>
    internal static bool IsUnrepurposedName(string name)
    {
        var trimmed = name.Trim();

        return trimmed.Length == 0 ||
               trimmed is "walk" or "runl" or "runr" ||
               trimmed.StartsWith("walkfast", StringComparison.Ordinal);
    }

    private static bool HasRunSignal(Sprite sprite)
    {
        // A dash variant in a slot the client cannot address is dropped whole later, so it
        // is not a signal now either.
        if (sprite.Actions.Any(a => a.DashVariant is not null && a.BaseAction < WalkActions.SlotLimit))
        {
            return true;
        }

        if (sprite.Actions.Any(a => a.DashVariant is null && IsRunName(a.Name)))
        {
            return true;
        }

        if (FindAction(sprite, 32) is { } left && FindAction(sprite, 33) is { } right &&
            left.Direction == 1 && right.Direction == 1 &&
            left.FrameCount == 8 && right.FrameCount == 8 &&
            AbsoluteDifference(left.FirstSprite, right.FirstSprite) == 8)
        {
            return true;
        }

        return IsAdjacentStripPair(sprite);
    }

    private static bool HasWalkSignal(Sprite sprite, bool hasRun)
    {
        foreach (var action in sprite.Actions)
        {
            if (action.DashVariant is not null || !WalkActions.IsWalk(action.BaseAction))
            {
                continue;
            }

            // A walk slot holding a named run cycle is the run cycle, not a walk. Only
            // discounted once something else has already established the sprite runs,
            // because otherwise a sprite whose only actions are named runs would come out
            // as neither.
            if (hasRun && IsRunName(action.Name))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal static bool IsRunName(string name) =>
        name.StartsWith("runl", StringComparison.Ordinal) ||
        name.StartsWith("runr", StringComparison.Ordinal);

    internal static SpriteAction? FindAction(Sprite sprite, uint action) =>
        sprite.Actions.FirstOrDefault(a => a.DashVariant is null && a.BaseAction == action);

    internal static uint AbsoluteDifference(uint left, uint right) =>
        left > right ? left - right : right - left;
}
