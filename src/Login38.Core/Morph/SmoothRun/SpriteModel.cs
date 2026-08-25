namespace Login38.Core.Morph.SmoothRun;

/// <summary>
/// The two extra action slots a preprocessed morph table carries.
/// </summary>
/// <remarks>
/// Running is left foot, right foot, left foot. The client has no notion of that: it plays
/// one walk animation per action slot. So the preprocessor writes the two halves of a run
/// cycle into slots the client does not otherwise use, and a hook inside the client
/// alternates between them while a speed effect is active.
/// </remarks>
public enum RunSlot
{
    /// <summary>Left foot — slot 98.</summary>
    Left = 98,

    /// <summary>Right foot — slot 99.</summary>
    Right = 99,
}

/// <summary>Slot arithmetic the runtime hook depends on.</summary>
public static class RunSlotExtensions
{
    /// <summary>
    /// Where the slot's frame pointer sits in the client's action table.
    /// </summary>
    /// <remarks>
    /// Each entry is eight bytes and the pointer is the second field. This is the one place
    /// the relationship is written down; the reference had it only as a comment on the
    /// constant <c>0x0314</c> inside the hook.
    /// </remarks>
    public static uint FrameDataOffset(this RunSlot slot) => ((uint)slot * 8) + 4;
}

/// <summary>What a sprite contributes to the smooth-run mapping.</summary>
public enum SpriteRole
{
    /// <summary>Neither a walk nor a run cycle.</summary>
    None,

    /// <summary>Walks, with no run cycle of its own — something to map a run cycle onto.</summary>
    Walk,

    /// <summary>Carries a run cycle and no walk — a source to map from.</summary>
    Run,

    /// <summary>Carries both, so it needs no partner.</summary>
    Both,
}

/// <summary>
/// One action line of a sprite.
/// </summary>
/// <param name="LineIndex">Which raw line it came from.</param>
/// <param name="BaseAction">The action slot, before any dash suffix.</param>
/// <param name="DashVariant">1 or 2 for the <c>N-1</c> / <c>N-2</c> syntax, otherwise null.</param>
/// <param name="Name">The action's name, lowercased and trimmed.</param>
/// <param name="Content">Everything between the parentheses.</param>
/// <param name="Direction">The first field of the content.</param>
/// <param name="FrameCount">The second field of the content.</param>
/// <param name="FirstSprite">The image index the first frame draws from.</param>
/// <param name="FrameRate">
/// The most recent <c>110</c> line seen in this sprite before this action, if any.
/// </param>
public sealed record SpriteAction(
    int LineIndex,
    uint BaseAction,
    uint? DashVariant,
    string Name,
    string Content,
    uint Direction,
    uint FrameCount,
    uint FirstSprite,
    string? FrameRate);

/// <summary>One sprite entry, from its <c>#id</c> header to the next.</summary>
/// <param name="Id">The sprite id.</param>
/// <param name="HeaderLineIndex">Which raw line the header is.</param>
/// <param name="ImageCount">The image count declared in the header.</param>
/// <param name="GraphicId">The shared graphic id, when the header carries <c>=n</c>.</param>
/// <param name="Actions">Every action line, in the order they were read.</param>
public sealed record Sprite(
    ushort Id,
    int HeaderLineIndex,
    uint ImageCount,
    uint? GraphicId,
    IReadOnlyList<SpriteAction> Actions);

/// <summary>A parsed morph table.</summary>
/// <param name="Sprites">Every sprite, in file order.</param>
/// <param name="RawLines">
/// The file split on newlines, kept verbatim so that emitting is a copy with edits rather
/// than a re-render — comments, spacing and unrecognised directives all survive.
/// </param>
/// <param name="EndsWithNewline">Whether the file ended with one.</param>
public sealed record SpriteFile(
    IReadOnlyList<Sprite> Sprites,
    IReadOnlyList<string> RawLines,
    bool EndsWithNewline);

/// <summary>
/// A run cycle, as the two halves that will become slots 98 and 99.
/// </summary>
/// <remarks>
/// Both halves are optional and independently so. A source sprite can have one clean side
/// and one that fails its checks, and half a run cycle still reads better than none.
/// </remarks>
/// <param name="Left">Frame content for slot 98.</param>
/// <param name="Right">Frame content for slot 99.</param>
/// <param name="FrameRate">The <c>110</c> content to emit alongside, if the source had one.</param>
/// <param name="SourceImageCount">
/// The source sprite's image count. A target whose own count is lower has its header
/// widened, or the client stops reading before it reaches the borrowed frames.
/// </param>
public sealed record RunPair(
    string? Left,
    string? Right,
    string? FrameRate,
    uint SourceImageCount)
{
    /// <summary>Whether there is anything to emit.</summary>
    public bool HasFrames => Left is not null || Right is not null;
}

/// <summary>Action numbers that count as walking.</summary>
public static class WalkActions
{
    /// <summary>
    /// The walk slots.
    /// </summary>
    /// <remarks>
    /// One list, because the reference kept a copy in the classifier and another in the
    /// parser's inline scan and had to keep them in step by hand.
    /// </remarks>
    public static ReadOnlySpan<uint> All => [0, 4, 11, 20, 24, 40, 46, 50, 54, 58, 62, 83, 88, 119];

    /// <summary>
    /// Extra slots the parser's inline scan also looks for.
    /// </summary>
    /// <remarks>
    /// Scanned after <see cref="All"/>, and the order matters: it decides the order actions
    /// end up in, which later stages read positionally.
    /// </remarks>
    public static ReadOnlySpan<uint> InlineExtra => [32, 33];

    /// <summary>
    /// The first action slot this client cannot address.
    /// </summary>
    /// <remarks>
    /// The 3.8 client's action table stops at 120. A table built for a later client can
    /// carry higher numbers, and they are dropped rather than written past the end of it.
    /// </remarks>
    public const uint SlotLimit = 121;

    /// <summary>Whether an action number is one of the walk slots.</summary>
    public static bool IsWalk(uint action) => All.Contains(action);
}
