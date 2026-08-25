namespace Login38.Core.Morph.SmoothRun;

/// <summary>
/// Decides which run cycle each sprite gets.
/// </summary>
/// <remarks>
/// <para>
/// A sprite that carries its own needs no help. The rest are matched to a separate sprite
/// holding the run cycle for the same character, by two routes: a shared graphic id, or a
/// run sprite whose graphic id is the walking sprite's own id.
/// </para>
/// <para>
/// Matching merges field by field rather than taking the first source whole. One sprite in
/// a graphic can hold the frames and another the timing, and a run cycle with no timing
/// plays at the walk's speed — which is the whole thing this is meant to fix.
/// </para>
/// </remarks>
public static class RunPairMatcher
{
    /// <summary>Builds the sprite-to-run-cycle mapping the emitter writes out.</summary>
    public static IReadOnlyDictionary<ushort, RunPair> Match(
        SpriteFile file,
        IReadOnlyDictionary<ushort, SpriteRole> roles,
        IReadOnlyDictionary<ushort, RunPair> extracted)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(extracted);

        var matched = new Dictionary<ushort, RunPair>();

        // Sprites that already have what they need, whether they walk as well or not.
        foreach (var sprite in file.Sprites)
        {
            if (roles.GetValueOrDefault(sprite.Id) is SpriteRole.Both or SpriteRole.Run &&
                extracted.TryGetValue(sprite.Id, out var own))
            {
                matched[sprite.Id] = own;
            }
        }

        MatchWithinGraphics(matched, file, roles, extracted);
        MatchRunSpritesNamingTheirTarget(matched, file, roles, extracted);
        FillRemainingFromNamedWalkPairs(matched, file);

        return matched;
    }

    /// <summary>
    /// Matches sprites that share a graphic id.
    /// </summary>
    /// <remarks>
    /// The usual arrangement: <c>#16140 72=5373 keina walk</c> and
    /// <c>#16141 72=5373 keina run</c> are the same character, and the second exists only to
    /// hold the frames the first cannot.
    /// </remarks>
    private static void MatchWithinGraphics(
        Dictionary<ushort, RunPair> matched,
        SpriteFile file,
        IReadOnlyDictionary<ushort, SpriteRole> roles,
        IReadOnlyDictionary<ushort, RunPair> extracted)
    {
        var byGraphic = new SortedDictionary<uint, (List<ushort> Walks, List<ushort> Runs)>();

        foreach (var sprite in file.Sprites)
        {
            if (sprite.GraphicId is not { } graphic)
            {
                continue;
            }

            if (!byGraphic.TryGetValue(graphic, out var group))
            {
                byGraphic[graphic] = group = ([], []);
            }

            switch (roles.GetValueOrDefault(sprite.Id))
            {
                case SpriteRole.Walk or SpriteRole.Both:
                    group.Walks.Add(sprite.Id);
                    break;

                case SpriteRole.Run:
                    group.Runs.Add(sprite.Id);
                    break;
            }
        }

        foreach (var (walks, runs) in byGraphic.Values)
        {
            walks.Sort();
            runs.Sort();

            // Every run sprite in the graphic contributes, in id order. A later one fills
            // only what the earlier ones left empty, so the lowest id wins each field and
            // the result does not depend on file order.
            foreach (var runId in runs)
            {
                if (!extracted.TryGetValue(runId, out var source))
                {
                    continue;
                }

                foreach (var walkId in walks)
                {
                    matched[walkId] = Merge(matched.GetValueOrDefault(walkId), source, widenHeader: true);
                }
            }
        }
    }

    /// <summary>
    /// Matches a run sprite that points at its target directly.
    /// </summary>
    /// <remarks>
    /// Where the graphic id of a run-only sprite is itself a sprite id that walks, it is not
    /// a shared graphic — it is a reference. <c>#10641 56=4910</c> means "these frames belong
    /// to sprite 4910".
    /// </remarks>
    private static void MatchRunSpritesNamingTheirTarget(
        Dictionary<ushort, RunPair> matched,
        SpriteFile file,
        IReadOnlyDictionary<ushort, SpriteRole> roles,
        IReadOnlyDictionary<ushort, RunPair> extracted)
    {
        var walkIds = file.Sprites
            .Where(s => roles.GetValueOrDefault(s.Id) is SpriteRole.Walk or SpriteRole.Both)
            .Select(s => s.Id)
            .ToHashSet();

        var runSprites = file.Sprites
            .Where(s => roles.GetValueOrDefault(s.Id) == SpriteRole.Run)
            .OrderBy(s => s.Id);

        foreach (var sprite in runSprites)
        {
            if (sprite.GraphicId is not { } graphic || graphic > ushort.MaxValue)
            {
                continue;
            }

            var target = (ushort)graphic;

            if (target == sprite.Id || !walkIds.Contains(target) ||
                !extracted.TryGetValue(sprite.Id, out var source))
            {
                continue;
            }

            // The image count is not merged here, unlike within a graphic. These two
            // sprites do not share an image set — the reference is a pointer, not a group —
            // so the source's count says nothing about how many images the target has, and
            // a header claiming more than exist is worse than one claiming fewer.
            matched[target] = Merge(matched.GetValueOrDefault(target), source, widenHeader: false);
        }
    }

    /// <summary>
    /// The last resort, for sprites nothing else reached.
    /// </summary>
    /// <remarks>
    /// Deliberately last and deliberately narrow. It reads a sprite's own actions 0 and 4 as
    /// a run cycle on the strength of the name alone, which is loose enough that a sprite
    /// already matched to a real source must never have it applied on top.
    /// </remarks>
    private static void FillRemainingFromNamedWalkPairs(
        Dictionary<ushort, RunPair> matched, SpriteFile file)
    {
        foreach (var sprite in file.Sprites)
        {
            if (!matched.ContainsKey(sprite.Id) &&
                RunPairExtractor.FromNamedWalkPair(sprite) is { } pair)
            {
                matched[sprite.Id] = pair;
            }
        }
    }

    /// <summary>
    /// Fills a target's empty fields from a source, leaving what it already has.
    /// </summary>
    /// <param name="widenHeader">
    /// Whether the source's image count may raise the target's — true only when the two
    /// sprites draw from the same image set.
    /// </param>
    private static RunPair Merge(RunPair? existing, RunPair source, bool widenHeader) =>
        existing is null
            ? source
            : existing with
            {
                Left = existing.Left ?? source.Left,
                Right = existing.Right ?? source.Right,
                FrameRate = existing.FrameRate ?? source.FrameRate,
                SourceImageCount = widenHeader
                    ? Math.Max(existing.SourceImageCount, source.SourceImageCount)
                    : existing.SourceImageCount,
            };
}
