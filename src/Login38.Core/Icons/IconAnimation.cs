namespace Login38.Core.Icons;

/// <summary>
/// One item icon replaced by an animation.
/// </summary>
/// <param name="Icon">
/// The graphic id the client would otherwise draw statically. It is the item's own icon
/// number, which is why nothing needs to be looked up at runtime.
/// </param>
/// <param name="FrameMilliseconds">How long each frame is held.</param>
/// <param name="RestMilliseconds">
/// How long the client's own static icon is shown after a full cycle. Zero plays the
/// animation continuously; a second or two is what makes one icon stand out on a screen
/// full of them.
/// </param>
/// <param name="Frames">
/// The frames in order. What a frame is depends on where the entry came from: the manifest
/// names resources in the package, and by the time the table reaches the game they are
/// addresses of decoded images in its memory.
/// </param>
public sealed record IconAnimation(
    ushort Icon, ushort FrameMilliseconds, uint RestMilliseconds, IReadOnlyList<uint> Frames)
{
    /// <inheritdoc cref="Frames"/>
    /// <remarks>
    /// Copied on the way in, so an entry cannot change under whoever is holding it — the
    /// table written into the game and the manifest it came from have to stay the same
    /// thing.
    /// </remarks>
    public IReadOnlyList<uint> Frames { get; init; } = [.. Frames];

    /// <summary>The most frames one entry can hold.</summary>
    /// <remarks>
    /// Fixed rather than variable because the table the game scans has fixed-size records —
    /// a scan that had to read a length before it could step would be slower and longer to
    /// write in machine code than one that adds a constant.
    /// </remarks>
    public const int MaxFrames = 99;

    /// <summary>One full cycle: the animation, then the rest.</summary>
    public uint CycleMilliseconds => ((uint)Frames.Count * FrameMilliseconds) + RestMilliseconds;

    /// <summary>What should be drawn at a given moment.</summary>
    /// <remarks>
    /// <para>
    /// A pure function of the clock, so every copy of the same icon on screen animates
    /// together without anything having to coordinate them — no timer, no per-widget state,
    /// no list of what is currently visible.
    /// </para>
    /// <para>
    /// This is the specification for the machine code that runs inside the game, not
    /// something the launcher calls at runtime. The two must agree, which is what the tests
    /// on both sides are for.
    /// </para>
    /// </remarks>
    /// <returns>The frame index to draw, or null to leave the client's own icon alone.</returns>
    public int? FrameAt(uint milliseconds)
    {
        var animation = (uint)Frames.Count * FrameMilliseconds;
        var cycle = animation + RestMilliseconds;

        // A cycle of zero would divide by zero in the game, which is a crash rather than a
        // missing animation. Rejected when the manifest is read and guarded again there;
        // this is the third place because it is the one that defines what the other two mean.
        if (cycle == 0)
        {
            return null;
        }

        var offset = milliseconds % cycle;

        return offset < animation ? (int)(offset / FrameMilliseconds) : null;
    }

    /// <summary>
    /// Two entries are the same when they say the same thing.
    /// </summary>
    /// <remarks>
    /// A record compares its members with the default comparer, and for a list that is
    /// reference equality — so without this, an entry never equals a copy of itself, which
    /// is exactly what a round trip through the manifest produces.
    /// </remarks>
    public bool Equals(IconAnimation? other) =>
        other is not null
        && Icon == other.Icon
        && FrameMilliseconds == other.FrameMilliseconds
        && RestMilliseconds == other.RestMilliseconds
        && Frames.SequenceEqual(other.Frames);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Icon);
        hash.Add(FrameMilliseconds);
        hash.Add(RestMilliseconds);

        foreach (var frame in Frames)
        {
            hash.Add(frame);
        }

        return hash.ToHashCode();
    }
}
