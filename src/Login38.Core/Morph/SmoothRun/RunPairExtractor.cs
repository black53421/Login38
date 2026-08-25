namespace Login38.Core.Morph.SmoothRun;

/// <summary>
/// Pulls the two halves of a run cycle out of the sprites that carry one.
/// </summary>
/// <remarks>
/// <para>
/// The conventions are tried in order and the first that matches wins, because a sprite can
/// satisfy more than one and they do not always name the same pair of actions. Dash variants
/// are the most explicit, then names, then the two structural shapes.
/// </para>
/// <para>
/// This runs over every sprite, not only the ones classified as running: a walking sprite
/// can carry its own run cycle in actions 0 and 4 and need no partner at all.
/// </para>
/// </remarks>
public static class RunPairExtractor
{
    /// <summary>Extracts a run cycle from each sprite that has one.</summary>
    public static IReadOnlyDictionary<ushort, RunPair> Extract(
        SpriteFile file, IReadOnlyDictionary<ushort, SpriteRole> roles)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(roles);

        var pairs = new Dictionary<ushort, RunPair>();

        foreach (var sprite in file.Sprites)
        {
            if (ExtractOne(sprite, roles.GetValueOrDefault(sprite.Id)) is not { } pair)
            {
                continue;
            }

            // Dash variants carry no timing of their own, so a sprite that matched on one
            // has none — even when its actions 0 and 4 would have supplied it. Filled in
            // here rather than inside the dash convention, because it is the sprite that
            // has the timing, not the convention that found the frames.
            pairs[sprite.Id] = pair.FrameRate is null
                ? pair with { FrameRate = AdjacentStripFrameRate(sprite) }
                : pair;
        }

        return pairs;
    }

    private static RunPair? ExtractOne(Sprite sprite, SpriteRole role) =>
        FromDashVariants(sprite) ?? FromNames(sprite) ?? FromActions32And33(sprite) ??
        FromActions0And4(sprite, role);

    /// <summary>
    /// The timing of a sprite whose actions 0 and 4 are a run cycle by name.
    /// </summary>
    /// <remarks>
    /// Stricter than the classifier's version of the same shape: interleaved frames are not
    /// accepted here, because timing is only borrowed from an action that was written as a
    /// run cycle rather than recognised as one.
    /// </remarks>
    private static string? AdjacentStripFrameRate(Sprite sprite)
    {
        if (Find(sprite, 0) is not { } left || Find(sprite, 4) is not { } right)
        {
            return null;
        }

        if (left.Direction != 1 || right.Direction != 1 ||
            left.FrameCount != 8 || right.FrameCount != 8 ||
            SpriteClassifier.AbsoluteDifference(left.FirstSprite, right.FirstSprite) != 8)
        {
            return null;
        }

        return SpriteClassifier.IsUnrepurposedName(left.Name) ||
               SpriteClassifier.IsUnrepurposedName(right.Name)
            ? left.FrameRate ?? right.FrameRate
            : null;
    }

    /// <summary>
    /// <c>N-1</c> is the left half and <c>N-2</c> the right.
    /// </summary>
    /// <remarks>
    /// One side is enough. Variants in a slot the client cannot address are ignored, since
    /// the whole line is dropped from the output anyway.
    /// </remarks>
    private static RunPair? FromDashVariants(Sprite sprite)
    {
        var left = sprite.Actions.FirstOrDefault(
            a => a.DashVariant == 1 && a.BaseAction < WalkActions.SlotLimit);

        var right = sprite.Actions.FirstOrDefault(
            a => a.DashVariant == 2 && a.BaseAction < WalkActions.SlotLimit);

        return left is null && right is null
            ? null
            : new RunPair(left?.Content, right?.Content, FrameRate: null, sprite.ImageCount);
    }

    /// <summary>
    /// Actions named <c>RunL</c> and <c>RunR</c>, whatever slot they are in.
    /// </summary>
    /// <remarks>
    /// The slot is not constrained, because tables in the wild put these in slots as high
    /// as 137. Anything past what the client can address is dropped when the file is
    /// written out, not here — the frames are still the right frames for slots 98 and 99.
    /// </remarks>
    private static RunPair? FromNames(Sprite sprite)
    {
        var left = sprite.Actions.FirstOrDefault(
            a => a.DashVariant is null && a.Name.StartsWith("runl", StringComparison.Ordinal));

        var right = sprite.Actions.FirstOrDefault(
            a => a.DashVariant is null && a.Name.StartsWith("runr", StringComparison.Ordinal));

        return left is null && right is null
            ? null
            : new RunPair(
                left?.Content, right?.Content, left?.FrameRate ?? right?.FrameRate, sprite.ImageCount);
    }

    /// <summary>
    /// Actions 32 and 33 drawing from rows exactly eight apart.
    /// </summary>
    /// <remarks>
    /// Which of the two is the left half is decided by which draws from the lower row, not
    /// by which action number it has: both orders occur.
    /// </remarks>
    private static RunPair? FromActions32And33(Sprite sprite)
    {
        if (Find(sprite, 32) is not { } first || Find(sprite, 33) is not { } second)
        {
            return null;
        }

        if (first.Direction != 1 || second.Direction != 1 ||
            first.FrameCount != 8 || second.FrameCount != 8)
        {
            return null;
        }

        var (left, right) =
            second.FirstSprite == first.FirstSprite + 8 ? (first, second) :
            first.FirstSprite == second.FirstSprite + 8 ? (second, first) :
            default((SpriteAction?, SpriteAction?));

        return left is null
            ? null
            : new RunPair(
                left.Content, right!.Content, AdjacentStripFrameRate(sprite), sprite.ImageCount);
    }

    /// <summary>
    /// Actions 0 and 4 as a run cycle rather than as two walks.
    /// </summary>
    /// <remarks>
    /// The weakest convention and the most heavily guarded, because these are the slots
    /// every sprite has. A sprite reaches the interleaved and single-action paths only once
    /// something else has already established that it runs.
    /// </remarks>
    private static RunPair? FromActions0And4(Sprite sprite, SpriteRole role)
    {
        var left = Find(sprite, 0);
        var right = Find(sprite, 4);

        if (left is null && right is null)
        {
            return null;
        }

        if (left is null || right is null)
        {
            return FromSingleAction(sprite, role, left ?? right!, isLeft: left is not null);
        }

        var frameRate = left.FrameRate ?? right.FrameRate;

        // Interleaved frames say what they are outright, so the name is not consulted — but
        // only for a sprite already known to run, or every four-frame walk pair in the file
        // would qualify.
        if (role == SpriteRole.Run && MorphTextSyntax.IsInterleavedRunPair(left.Content, right.Content))
        {
            return new RunPair(left.Content, right.Content, frameRate, sprite.ImageCount);
        }

        if (left.Direction != 1 || right.Direction != 1 ||
            left.FrameCount != 8 || right.FrameCount != 8 ||
            SpriteClassifier.AbsoluteDifference(left.FirstSprite, right.FirstSprite) != 8)
        {
            return null;
        }

        var leftIsClean = SpriteClassifier.IsUnrepurposedName(left.Name);
        var rightIsClean = SpriteClassifier.IsUnrepurposedName(right.Name);

        if (!leftIsClean && !rightIsClean)
        {
            return null;
        }

        // Only the side whose name has not been repurposed is taken. Half a run cycle in
        // slot 98 with slot 99 left empty is what the client falls back from gracefully;
        // a slot holding a spellcast because it happened to sit eight rows away is not.
        return new RunPair(
            leftIsClean ? left.Content : null,
            rightIsClean ? right.Content : null,
            frameRate,
            sprite.ImageCount);
    }

    private static RunPair? FromSingleAction(
        Sprite sprite, SpriteRole role, SpriteAction action, bool isLeft)
    {
        if (role != SpriteRole.Run || action.Direction != 1 || action.FrameCount != 8 ||
            !SpriteClassifier.IsUnrepurposedName(action.Name))
        {
            return null;
        }

        return new RunPair(
            isLeft ? action.Content : null,
            isLeft ? null : action.Content,
            action.FrameRate,
            sprite.ImageCount);
    }

    /// <summary>
    /// A run cycle a sprite carries in its own actions 0 and 4, by name alone.
    /// </summary>
    /// <remarks>
    /// The last resort, applied only to sprites that nothing else has matched. It asks
    /// nothing of the frame layout — two actions with different starting rows and the same
    /// length, one of them named for running — so it is far too loose to run earlier.
    /// </remarks>
    internal static RunPair? FromNamedWalkPair(Sprite sprite)
    {
        if (Find(sprite, 0) is not { } left || Find(sprite, 4) is not { } right)
        {
            return null;
        }

        var name = left.Name;

        if (!name.StartsWith("walkfast", StringComparison.OrdinalIgnoreCase) &&
            !name.StartsWith("run", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (left.FirstSprite == right.FirstSprite || left.FrameCount != right.FrameCount)
        {
            return null;
        }

        return new RunPair(
            left.Content, right.Content, left.FrameRate ?? right.FrameRate, sprite.ImageCount);
    }

    private static SpriteAction? Find(Sprite sprite, uint action) =>
        SpriteClassifier.FindAction(sprite, action);
}
