using Login38.Interop;

namespace Login38.Aux.Toggles;

/// <summary>
/// Moves the damage numbers from over a monster's head to under its feet.
/// </summary>
/// <remarks>
/// <para>
/// A preference, and a useful one: a row of monsters in front of the player puts every
/// number in the same place, over whatever is nearest. Under their feet they line up with
/// what they belong to.
/// </para>
/// <para>
/// Four detours, all of them small, and all of them turning on one question — is this
/// bubble one of ours. The answer is its colour: the launcher's damage bubbles are red and
/// everything the client shows by itself is not, so a colour test is enough to move ours
/// without touching anybody's speech.
/// </para>
/// <para>
/// Meaningless without <see cref="DamageCave"/>, which is what makes red bubbles in the
/// first place.
/// </para>
/// </remarks>
internal static class FeetCave
{
    /// <summary>How far below the feet the bubble sits.</summary>
    internal const byte Padding = 0x20;

    /// <summary>How much is asked for.</summary>
    internal const int CaveSize = 0x400;

    /// <summary>Where the bubble keeps the height it is anchored at.</summary>
    private const uint Anchor = 0x380;

    /// <summary>Its sprite record.</summary>
    private const uint Sprite = 0x398;

    /// <summary>Which of the frames in that record it is showing.</summary>
    private const byte Frame = 0x1D;

    /// <summary>How many bytes each frame's entry takes.</summary>
    private const byte FrameSize = 0x18;

    /// <summary>Where the frame table is.</summary>
    private const uint Frames = 0x8C;

    /// <summary>And where in a frame its height sits.</summary>
    private const byte Height = 0x14;

    /// <summary>The colour the bubble was asked to draw in, in the caller's frame.</summary>
    private const byte ColourArgument = 0x10;

    /// <summary>The colour the bubble itself carries, once it has been filled in.</summary>
    private const uint ColourField = 0x39C;

    /// <summary>Which way the tail points.</summary>
    private const uint TailField = 0x36A;

    /// <summary>Upwards, which is what a bubble below its target wants.</summary>
    private const byte PointUp = 4;

    /// <summary>
    /// Where the bubble's anchor is written for anything that is not the player.
    /// </summary>
    /// <remarks>
    /// Two sites rather than one because the client has two copies of the same code with
    /// the registers the other way round — one for the player and one for everybody else.
    /// </remarks>
    internal static HookSite Remote => new(
        new GameAddress(0x0042B9FB),
        [0x89, 0x81, 0x80, 0x03, 0x00, 0x00],                 // mov [ecx+0x380], eax
        new GameAddress(0x0042BA01));

    /// <inheritdoc cref="Remote"/>
    internal static HookSite Local => new(
        new GameAddress(0x0042B9D2),
        [0x89, 0x8A, 0x80, 0x03, 0x00, 0x00],                 // mov [edx+0x380], ecx
        new GameAddress(0x0042B9D8));

    /// <summary>Where the bubble's tail is decided, once its layout is done.</summary>
    internal static HookSite Tail => new(
        new GameAddress(0x0042BAFB),
        [0x0F, 0xB6, 0xC0, 0x85, 0xC0],                       // movzx eax, al; test eax, eax
        new GameAddress(0x0042BB00));

    /// <summary>And where the client sets it back to pointing downwards, every frame.</summary>
    internal static HookSite Keep => new(
        new GameAddress(0x0042AE0C),
        [0xC6, 0x81, 0x6A, 0x03, 0x00, 0x00, 0x00],           // mov byte [ecx+0x36A], 0
        new GameAddress(0x0042AE13));

    /// <summary>Where each detour's code begins, relative to the cave.</summary>
    internal readonly record struct Entries(int Remote, int Local, int Tail, int Keep, int Length);

    /// <summary>Builds the whole cave.</summary>
    internal static (byte[] Code, Entries Entries) Build(GameAddress cave)
    {
        var code = new ShellcodeBuilder(cave);

        var remote = code.Length;
        Position(code, Remote, Ecx, Edx);

        var local = code.Length;
        Position(code, Local, Edx, Ecx);

        var tail = code.Length;
        FlipTail(code);

        var keep = code.Length;
        LeaveTailAlone(code);

        return (code.Build(), new Entries(remote, local, tail, keep, code.Length));
    }

    private const byte Eax = 0;
    private const byte Ecx = 1;
    private const byte Edx = 2;

    /// <summary>A mod/reg/rm byte.</summary>
    private static byte ModRm(byte mode, byte register, byte memory) =>
        (byte)((mode << 6) | (register << 3) | memory);

    /// <summary>
    /// Puts the anchor under the target instead of over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client has just written the anchor as the top of the target's sprite. What this
    /// adds, for a red bubble only, is that sprite's own height back on plus a little — so
    /// the anchor ends up below the feet rather than above the head.
    /// </para>
    /// <para>
    /// The colour is read from the argument the drawing routine was called with, not from
    /// the bubble. Bubbles come from a pool and their colour field is not filled in until
    /// after this runs, so reading it there gets whatever the last bubble in that slot was
    /// — which is red often enough that the numbers land over the head every few hits and
    /// under the feet the rest of the time.
    /// </para>
    /// </remarks>
    /// <param name="bubble">The register the bubble is in on this copy of the client's code.</param>
    /// <param name="scratch">And the one that is free.</param>
    private static void Position(ShellcodeBuilder code, HookSite site, byte bubble, byte scratch)
    {
        // The displaced write first: whatever else happens, the client's own anchor has to
        // be there.
        code.Bytes(site.Stock);

        code.Bytes([0x66, 0x81, ModRm(1, 7, 5), ColourArgument])      // cmp word [ebp+0x10], red
            .Word((ushort)DamageCave.TextColour);
        var notOurs = code.ShortJumpIfNotEqual();

        code.Bytes([0x8B, ModRm(2, scratch, bubble)]).Dword(Sprite)   // mov s, [b+0x398]
            .Bytes([0x85, ModRm(3, scratch, scratch)]);               // test s, s
        var noSprite = code.ShortJumpIfZero();

        code.Bytes([0x0F, 0xBE, ModRm(1, Eax, scratch), Frame])       // movsx eax, byte [s+0x1D]
            .Bytes([0x6B, ModRm(3, Eax, Eax), FrameSize])             // imul eax, eax, 0x18
            .Bytes([0x8B, ModRm(2, scratch, scratch)]).Dword(Frames)  // mov s, [s+0x8C]
            .Bytes([0x85, ModRm(3, scratch, scratch)]);               // test s, s
        var noFrames = code.ShortJumpIfZero();

        // mov s, [s + eax + 0x14] — the frame's own height.
        code.Bytes([0x8B, ModRm(1, scratch, 4), Sib(Eax, scratch), Height])
            .Bytes([0xF7, ModRm(3, 3, scratch)])                      // neg s
            .Bytes([0x83, ModRm(3, 0, scratch), Padding])             // add s, 0x20
            .Bytes([0x01, ModRm(2, scratch, bubble)]).Dword(Anchor);  // add [b+0x380], s

        code.MarkLabel(notOurs).MarkLabel(noSprite).MarkLabel(noFrames);
        Resume(code, site);
    }

    /// <summary>A scale/index/base byte, always at scale one.</summary>
    private static byte Sib(byte index, byte @base) => (byte)((index << 3) | @base);

    /// <summary>
    /// Turns a red bubble's tail upside down.
    /// </summary>
    /// <remarks>
    /// A bubble under the target needs its tail pointing up at what it belongs to. The
    /// client draws the tail from one byte, and this is the one place it can be set after
    /// the bubble's layout is finished and before it is drawn.
    /// </remarks>
    private static void FlipTail(ShellcodeBuilder code)
    {
        code.Bytes([0x8B, ModRm(1, Edx, 5), 0xE0]);                   // mov edx, [ebp-0x20]

        // By now the bubble does carry its own colour, and the argument is out of reach.
        code.Bytes([0x66, 0x81, ModRm(2, 7, Edx)]).Dword(ColourField)
            .Word((ushort)DamageCave.TextColour);
        var notOurs = code.ShortJumpIfNotEqual();

        code.Bytes([0xC6, ModRm(2, 0, Edx)]).Dword(TailField).Byte(PointUp);

        code.MarkLabel(notOurs)
            .Bytes([0x0F, 0xB6, ModRm(3, Eax, Eax)])                  // movzx eax, al
            .Bytes([0x85, ModRm(3, Eax, Eax)]);                       // test eax, eax

        Resume(code, Tail);
    }

    /// <summary>
    /// Stops the client putting the tail back the right way up.
    /// </summary>
    /// <remarks>
    /// The routine that lays a bubble out resets the tail every frame, which would undo the
    /// flip a frame after it was made. Red bubbles are left alone; everything else is reset
    /// exactly as before.
    /// </remarks>
    private static void LeaveTailAlone(ShellcodeBuilder code)
    {
        code.Bytes([0x66, 0x81, ModRm(2, 7, Ecx)]).Dword(ColourField)
            .Word((ushort)DamageCave.TextColour);
        var ours = code.ShortJumpIfZero();

        code.Bytes([0xC6, ModRm(2, 0, Ecx)]).Dword(TailField).Byte(0);

        code.MarkLabel(ours);
        Resume(code, Keep);
    }

    /// <summary>Jumps back into the client, past what was displaced.</summary>
    private static void Resume(ShellcodeBuilder code, HookSite site) =>
        code.Byte(0xE9).Dword(unchecked(site.Resume.Value - (code.CurrentAddress.Value + 4)));
}
