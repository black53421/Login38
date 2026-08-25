using Login38.Interop;

namespace Login38.Aux.Toggles;

/// <summary>
/// The detours that let a monster's name be drawn in the colour written on its record.
/// </summary>
/// <remarks>
/// <para>
/// The client already keeps a colour word on every world entity at <c>+0x30</c> and
/// already passes a pair of colours — inner glyph and outline — to its text routine. What
/// it does not do is let the two differ for a monster name: it draws the name in the entity
/// colour throughout, which on a coloured name is unreadable against the world behind it.
/// </para>
/// <para>
/// So the detours do not invent a colour. The scanner writes one onto the entity, and these
/// three sites arrange for it to come out as a coloured outline around a white glyph.
/// </para>
/// <para>
/// Every one of them is guarded by a marker table — a list of the entity addresses this
/// launcher has coloured, written by the scanner and walked by the detour. Without it a
/// colour word that happens to equal one of the four would flip any name in the client,
/// including a player's.
/// </para>
/// </remarks>
internal static class MonsterNameCave
{
    /// <summary>How many coloured entities the detours will look at.</summary>
    /// <remarks>
    /// Walked linearly inside the client's text routine, so this is a per-name cost. A
    /// screen holds nothing like this many monsters; the cap is there so a scan that finds
    /// an unreasonable number cannot turn the render path into a search.
    /// </remarks>
    internal const int MarkerCapacity = 128;

    /// <summary>A count, then that many entity addresses.</summary>
    internal const int MarkerTableBytes = 4 + (MarkerCapacity * 4);

    /// <summary>How much is asked for for the name-render detour.</summary>
    internal const int NameCaveSize = 0x200;

    /// <summary>And for each of the two text-routine detours.</summary>
    internal const int TextCaveSize = 0x500;

    /// <summary>Where a world entity keeps the colour its name is drawn in.</summary>
    internal const byte ColourOffset = 0x30;

    /// <summary>Where it keeps the id the server knows it by.</summary>
    internal const byte ServerIdOffset = 0x0C;

    /// <summary>Where it keeps the pointer to its name.</summary>
    internal const byte NameOffset = 0x60;

    /// <summary>
    /// Below this an object id belongs to something placed by the map rather than spawned.
    /// </summary>
    internal const uint LowestMonsterId = 0x0100_0000;

    /// <summary>The client's text routine, in its full form.</summary>
    private static readonly GameAddress TextDrawFunction = new(0x0046F150);

    /// <summary>The surface names are drawn onto.</summary>
    private static readonly GameAddress DrawSurface = new(0x009A84E0);

    /// <summary>Whatever the player currently has selected.</summary>
    private static readonly GameAddress SelectedEntity = new(0x00ABF440);

    /// <summary>Where the name-render path continues once the name has been drawn.</summary>
    private static readonly GameAddress AfterNameDrawn = new(0x004F2BE2);

    /// <summary>The entity being drawn, in the render frame.</summary>
    private const int EntityLocal = -0x238;

    /// <summary>Its name's length, in the same frame.</summary>
    private const int LengthLocal = -0x258;

    /// <summary>And where the name goes.</summary>
    private const int YLocal = -0x25C;

    /// <inheritdoc cref="YLocal"/>
    private const int XLocal = -0x260;

    /// <summary>The inner colour argument, in the text routine's own frame.</summary>
    private const byte InnerColourArgument = 0x1C;

    /// <summary>And the outline colour beside it.</summary>
    private const byte OuterColourArgument = 0x20;

    /// <summary>The return address, in the frame the text routine has just set up.</summary>
    private const byte ReturnAddress = 0x04;

    /// <summary>How many bytes the replaced call's arguments take.</summary>
    private const byte ArgumentBytes = 0x20;

    /// <summary>
    /// Where the client draws an ordinary world name.
    /// </summary>
    /// <remarks>
    /// The eight bytes are the start of the client's own call setup, which the detour
    /// replays on the way out for every name it decides not to touch.
    /// </remarks>
    internal static HookSite NameRender => new(
        new GameAddress(0x004F2BA0),
        [0x6A, 0x00, 0x8B, 0x15, 0x38, 0xFB, 0x95, 0x00],     // push 0; mov edx, [0x0095FB38]
        new GameAddress(0x004F2BA8));

    /// <summary>The text routine itself, hooked at its prologue.</summary>
    /// <remarks>
    /// Two of them, doing the same job with different stack frames. Which one the client
    /// reaches for depends on the call site, and the selected-name panel uses both.
    /// </remarks>
    internal static HookSite TextDraw => new(
        new GameAddress(0x0046F150),
        [0x55, 0x8B, 0xEC, 0x6A, 0xFF],                       // push ebp; mov ebp, esp; push -1
        new GameAddress(0x0046F155));

    /// <inheritdoc cref="TextDraw"/>
    internal static HookSite CompactTextDraw => new(
        new GameAddress(0x0046F980),
        [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x0C],                 // push ebp; mov ebp, esp; sub esp, 0xC
        new GameAddress(0x0046F986));

    /// <summary>
    /// The call sites in the selected-name panel that reach <see cref="TextDraw"/>.
    /// </summary>
    /// <remarks>
    /// Return addresses rather than call addresses, because that is what the detour has to
    /// hand: it runs inside the callee, where the only thing identifying the caller is what
    /// the call pushed.
    /// </remarks>
    internal static ReadOnlySpan<uint> SelectedReturns =>
    [
        0x004F2E8C, 0x004F307A, 0x004F310C, 0x004F319E,
        0x004F3235, 0x004F3278, 0x004F33D5, 0x004F3535,
        0x004F3650, 0x004F3693, 0x004F38B4, 0x004F39AB,
    ];

    /// <inheritdoc cref="SelectedReturns"/>
    internal static ReadOnlySpan<uint> CompactReturns =>
        [0x004F3039, 0x004F30CB, 0x004F315D, 0x004F31F4];

    /// <summary>
    /// Lays out the table the detours consult.
    /// </summary>
    /// <remarks>
    /// Written whole every pass, count first. Anything past the count is left as it was —
    /// the detours never read it, and rewriting the rest of the page each pass would be
    /// half a kilobyte of cross-process write for nothing.
    /// </remarks>
    internal static byte[] MarkerTable(IReadOnlyList<GameAddress> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var count = Math.Min(entities.Count, MarkerCapacity);
        var raw = new byte[4 + (count * 4)];

        BitConverter.TryWriteBytes(raw, (uint)count);

        for (var i = 0; i < count; i++)
        {
            BitConverter.TryWriteBytes(raw.AsSpan(4 + (i * 4)), entities[i].Value);
        }

        return raw;
    }

    private const byte Eax = 0;
    private const byte Ecx = 1;
    private const byte Edx = 2;
    private const byte Esi = 6;
    private const byte Edi = 7;

    private const byte Push = 0x50;
    private const byte Pop = 0x58;
    private const byte Jb = 0x82;
    private const byte Jz = 0x84;
    private const byte Ja = 0x87;

    /// <summary>
    /// Draws a coloured monster name as a coloured outline around a white glyph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything that is not one is sent back through the client's own path, which is why
    /// the tests are the cheap ones first: a null entity, then a white colour word, then
    /// the four this feature writes, and only then the two that cost a read.
    /// </para>
    /// <para>
    /// The reference compares the colour against white twice — its "client default" and its
    /// own White band are both 0xFFDF — which is twelve bytes of dead comparison in a path
    /// that runs once per visible name per frame.
    /// </para>
    /// </remarks>
    internal static byte[] BuildNameRender(GameAddress cave, GameAddress markerTable)
    {
        var code = new ShellcodeBuilder(cave);
        var untouched = new List<ShellcodeBuilder.NearLabel>();

        MovFromLocal(code, Eax, EntityLocal);
        code.TestEaxEax();
        untouched.Add(code.NearJump(Jz));

        // movzx ecx, word ptr [eax+0x30]
        code.Bytes([0x0F, 0xB7, 0x48, ColourOffset]);
        CmpEcx(code, MonsterColours.White);
        untouched.Add(code.NearJump(Jz));

        var coloured = new List<ShellcodeBuilder.NearLabel>();

        foreach (var colour in MonsterColours.Feature)
        {
            CmpEcx(code, colour);
            coloured.Add(code.NearJump(Jz));
        }

        untouched.Add(code.NearJumpAlways());

        foreach (var label in coloured)
        {
            code.MarkLabel(label);
        }

        // A colour word alone is not enough: the client keeps colours on players too.
        code.Bytes([0x8B, 0x50, ServerIdOffset])              // mov edx, [eax+0x0C]
            .Bytes([0x81, 0xFA]).Dword(LowestMonsterId);      // cmp edx, 0x01000000
        untouched.Add(code.NearJump(Jb));
        untouched.Add(Marked(code, markerTable, Eax));

        // TextDraw(surface, name, length, x, y, white, entity colour, 0).
        code.PushImm8(0)
            .Byte(Push + Ecx)
            .PushImm32(MonsterColours.White);
        MovFromLocal(code, Edx, YLocal);
        code.Byte(Push + Edx);
        MovFromLocal(code, Eax, XLocal);
        code.Byte(Push + Eax);
        MovFromLocal(code, Ecx, LengthLocal);
        code.Byte(Push + Ecx);
        MovFromLocal(code, Edx, EntityLocal);
        code.Bytes([0x8B, 0x42, NameOffset])                  // mov eax, [edx+0x60]
            .Byte(Push + Eax)
            .MovEcxFrom(DrawSurface)
            .Byte(Push + Ecx)
            .CallTo(TextDrawFunction)
            .AddEsp(ArgumentBytes)
            .JumpTo(AfterNameDrawn);

        foreach (var label in untouched)
        {
            code.MarkLabel(label);
        }

        code.Bytes(NameRender.Stock).JumpTo(NameRender.Resume);

        return code.Build();
    }

    /// <summary>
    /// Swaps the two colours a selected monster's name is drawn with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The panel that names what the player has selected calls the text routine with the
    /// entity colour as the glyph colour, which for a coloured monster is the same
    /// unreadable result the render hook exists to avoid. This moves it to the outline and
    /// puts white back in the glyph.
    /// </para>
    /// <para>
    /// It runs at the top of a routine the whole client draws text through, so the first
    /// thing it does is get out of the way. The reference's guard is a chain of twelve
    /// four-byte comparisons against the panel's call sites — a hundred and thirty bytes on
    /// every string the client draws. The call sites all sit within one span of the panel's
    /// code, so a pair of bounds rejects everything else in twelve.
    /// </para>
    /// </remarks>
    /// <param name="callers">Return addresses of the call sites worth acting on.</param>
    internal static byte[] BuildTextColour(
        GameAddress cave, GameAddress markerTable, HookSite site, ReadOnlySpan<uint> callers)
    {
        if (callers.IsEmpty)
        {
            throw new ArgumentException(
                "A text colour detour with no call sites would never fire.", nameof(callers));
        }

        var code = new ShellcodeBuilder(cave);
        var done = new List<ShellcodeBuilder.NearLabel>();

        code.Bytes(site.Stock)
            .Bytes([0x8B, 0x45, ReturnAddress]);              // mov eax, [ebp+4]

        var lowest = uint.MaxValue;
        var highest = uint.MinValue;

        foreach (var caller in callers)
        {
            lowest = Math.Min(lowest, caller);
            highest = Math.Max(highest, caller);
        }

        code.CmpEax(lowest);
        done.Add(code.NearJump(Jb));
        code.CmpEax(highest);
        done.Add(code.NearJump(Ja));

        var ours = new List<ShellcodeBuilder.NearLabel>();

        foreach (var caller in callers)
        {
            code.CmpEax(caller);
            ours.Add(code.NearJump(Jz));
        }

        done.Add(code.NearJumpAlways());

        foreach (var label in ours)
        {
            code.MarkLabel(label);
        }

        code.Bytes([0x8B, 0x45, InnerColourArgument]);        // mov eax, [ebp+0x1C]

        var feature = new List<ShellcodeBuilder.NearLabel>();

        foreach (var colour in MonsterColours.Feature)
        {
            code.CmpEax(colour);
            feature.Add(code.NearJump(Jz));
        }

        done.Add(code.NearJumpAlways());

        foreach (var label in feature)
        {
            code.MarkLabel(label);
        }

        // The panel only ever names what is selected, so that is what the marker is checked
        // against — the entity being drawn is not on this routine's stack to look at.
        code.Bytes([0x8B, 0x15]).Dword(SelectedEntity.Value)  // mov edx, [0x00ABF440]
            .TestEdxEdx();
        done.Add(code.NearJump(Jz));
        done.Add(Marked(code, markerTable, Edx));

        code.Bytes([0x89, 0x45, OuterColourArgument])         // mov [ebp+0x20], eax
            .Bytes([0xC7, 0x45, InnerColourArgument]).Dword(MonsterColours.White);

        foreach (var label in done)
        {
            code.MarkLabel(label);
        }

        code.JumpTo(site.Resume);

        return code.Build();
    }

    /// <summary>
    /// Walks the marker table for the entity in <paramref name="register"/>.
    /// </summary>
    /// <remarks>
    /// Falls through when it is there and takes the returned branch when it is not, which
    /// is the way round every caller wants: "not one of ours" is always the exit.
    /// </remarks>
    /// <returns>The branch to resolve to wherever an unmarked entity should go.</returns>
    private static ShellcodeBuilder.NearLabel Marked(
        ShellcodeBuilder code, GameAddress table, byte register)
    {
        code.Byte(Push + Esi)
            .Byte(Push + Edi)
            .Byte(0xBE).Dword(table.Value)                    // mov esi, table
            .Bytes([0x8B, 0x3E])                              // mov edi, [esi]
            .Bytes([0x83, 0xC6, 0x04])                        // add esi, 4
            .Bytes([0x85, 0xFF]);                             // test edi, edi
        var empty = code.ShortJumpIfZero();

        var next = code.Here();
        code.Bytes([0x39, (byte)((register << 3) | Esi)]);    // cmp [esi], register
        var found = code.ShortJumpIfZero();
        code.Bytes([0x83, 0xC6, 0x04])                        // add esi, 4
            .Byte(0x48 + Edi)                                 // dec edi
            .ShortJumpBack(0x75, next);                       // jnz next

        code.MarkLabel(empty)
            .Byte(Pop + Edi)
            .Byte(Pop + Esi);
        var missing = code.NearJumpAlways();

        code.MarkLabel(found)
            .Byte(Pop + Edi)
            .Byte(Pop + Esi);

        return missing;
    }

    /// <summary><c>mov register, dword ptr [ebp+displacement]</c>.</summary>
    private static void MovFromLocal(ShellcodeBuilder code, byte register, int displacement) =>
        code.Bytes([0x8B, (byte)(0x80 | (register << 3) | 5)]).Dword(unchecked((uint)displacement));

    /// <summary><c>cmp ecx, imm32</c>.</summary>
    private static void CmpEcx(ShellcodeBuilder code, uint value) =>
        code.Bytes([0x81, 0xF9]).Dword(value);
}
