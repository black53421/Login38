using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Extends the equipment window from 19 slots to 25.
/// </summary>
/// <remarks>
/// <para>
/// The server sends equipment as an index from 1 to 31; the client translates that
/// through a compiled-in switch that only knows 1 to 21, and builds its window from a
/// loop that only visits 19 slots. Anything the server equips above that is accepted and
/// then not drawn.
/// </para>
/// <para>
/// Three changes, each independent of the others:
/// </para>
/// <list type="number">
/// <item>the index translation becomes a table lookup covering the whole range;</item>
/// <item>the window setup gains six more slots, appended after its own loop rather than
/// by widening it — the new slots live at different child indices, so the loop's
/// arithmetic does not apply to them;</item>
/// <item>the sprite-id bounds check is widened, because the new slots use icons above the
/// range the client was built to accept.</item>
/// </list>
/// <para>
/// The two hooked functions are found by signature rather than by address: they sit in a
/// part of the client that moved between builds, and a wrong fixed address here would
/// hook the middle of an unrelated function.
/// </para>
/// </remarks>
public sealed class EquipmentSlotsPatch : IGamePatch
{
    /// <summary>Where the equipment window's code lives.</summary>
    private static readonly GameAddress ScanStart = new(0x0079_0000);

    private static readonly GameAddress ScanEnd = new(0x007A_0000);

    /// <summary>
    /// <c>sub ecx,1</c> / <c>mov [ebp-0xC],ecx</c> / <c>cmp [ebp-0xC],0x15</c> /
    /// <c>ja default</c> / <c>mov edx,[ebp-0xC]</c> / <c>jmp [edx*4+table]</c> — the
    /// switch that translates a server index into a slot.
    /// </summary>
    private const string IndexSwitchSignature =
        "83 E9 01 89 4D F4 83 7D F4 15 0F 87 ?? ?? ?? ?? 8B 55 F4 FF 24 95";

    /// <summary>
    /// The slot-setup loop. The comparison value is wildcarded because it differs between
    /// builds and is not what identifies the loop.
    /// </summary>
    private const string SetupLoopSignature =
        "C7 45 F8 01 00 00 00 EB 09 8B 4D F8 83 C1 01 89 4D F8 83 7D F8 ??";

    /// <summary>Offsets from the setup-loop signature to the places that are hooked.</summary>
    private const int SetupHelperCallOffset = 0x27;

    private const int SetupExitOffset = 0x2E;

    private const int BackgroundIndexOffset = 0xBB8;

    /// <summary><c>mov esp,ebp</c> / <c>pop ebp</c> / <c>ret</c> / <c>int3</c>.</summary>
    private static ReadOnlySpan<byte> SetupEpilogue => [0x8B, 0xE5, 0x5D, 0xC3, 0xCC];

    /// <summary><c>mov ecx,[ebp+0xC]</c> / <c>add ecx,0x1A</c> / <c>push ecx</c>.</summary>
    private static ReadOnlySpan<byte> BackgroundIndexCalculation => [0x8B, 0x4D, 0x0C, 0x83, 0xC1, 0x1A, 0x51];

    /// <summary>The sprite-id bounds check, verified at a fixed address across builds.</summary>
    private static readonly GameAddress SpriteBoundsCheck = new(0x0043_87DB);

    /// <summary><c>cmp edx, [spriteCount]</c>.</summary>
    private static ReadOnlySpan<byte> SpriteBoundsOriginal => [0x3B, 0x15, 0xB0, 0xD0, 0xC2, 0x00];

    /// <summary><c>cmp edx, 30003</c>.</summary>
    private static ReadOnlySpan<byte> SpriteBoundsWidened => [0x81, 0xFA, 0x33, 0x75, 0x00, 0x00];

    /// <summary>The first child index of the six appended slots.</summary>
    private const byte FirstAppendedSlot = 46;

    private const byte AppendedSlotCount = 6;

    /// <summary>
    /// How far past a slot's own child index its background image sits.
    /// </summary>
    /// <remarks>
    /// The original slots and the appended ones were laid out separately, so they need
    /// different offsets. That is the whole reason the calculation is hooked rather than
    /// having its constant rewritten.
    /// </remarks>
    private const byte AppendedBackgroundOffset = 6;

    private const byte OriginalBackgroundOffset = 0x1A;

    /// <summary>
    /// Server equipment index to slot number in the window, zero where the server has no
    /// such index.
    /// </summary>
    /// <remarks>
    /// Index 25 maps to the same slot as index 9. That is the client's own arrangement,
    /// not a transcription error: arrows and the item that shares that slot are never
    /// equipped together.
    /// </remarks>
    internal static ReadOnlySpan<byte> SlotLookup =>
    [
        0,                       //  0  unused
        2,                       //  1  helm
        5,                       //  2  armour
        4,                       //  3  shirt
        6,                       //  4  cloak
        11,                      //  5  boots
        7,                       //  6  gloves
        9,                       //  7  shield
        14,                      //  8  weapon
        16,                      //  9  arrows
        3,                       // 10  amulet
        8,                       // 11  belt
        1,                       // 12  earring
        0, 0, 0, 0, 0,           // 13-17 unused
        10,                      // 18  ring 1
        12,                      // 19  ring 2
        13,                      // 20  ring 3
        15,                      // 21  ring 4
        17,                      // 22  rune
        18,                      // 23  second rune
        19,                      // 24  second earring
        16,                      // 25  trousers
        46, 47, 48, 49, 50, 51,  // 26-31 the appended slots
    ];

    /// <summary>The largest index the lookup covers.</summary>
    private const byte HighestServerIndex = 31;

    private const int LookupCaveSize = 64;

    /// <summary>The lookup table sits after the code, which is exactly this long.</summary>
    private const int LookupCodeSize = 32;

    private const int SetupCaveSize = 128;

    private const int BackgroundHookOffset = 80;

    private readonly ILogger<EquipmentSlotsPatch> _logger;

    public EquipmentSlotsPatch(ILogger<EquipmentSlotsPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "equipment-slots";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.EquipUiEnabled;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var translation = ReplaceIndexTranslation(context);
        var window = AppendSlotsToWindow(context);
        var sprites = WidenSpriteBounds(context);

        // The appended slots are useless without the translation that routes anything to
        // them, and drawing them needs the widened sprite bounds. Any one missing means
        // the feature does not work, and saying so is better than a window with six empty
        // squares in it.
        if (!translation || !window || !sprites)
        {
            throw new GameProcessException(
                $"Equipment slots only partly extended (translation: {translation}, " +
                $"window: {window}, sprites: {sprites}).");
        }

        _logger.LogInformation("Equipment window extended to {Count} slots", SlotLookup.Length - 7);
    }

    // ---- index translation ---------------------------------------------------------------

    private bool ReplaceIndexTranslation(GamePatchContext context)
    {
        if (FindInWindow(context, IndexSwitchSignature) is not { } signature)
        {
            _logger.LogWarning("Could not find the equipment index switch");
            return false;
        }

        if (FindFunctionEntry(context, signature) is not { } entry)
        {
            _logger.LogWarning("Could not find the entry of the function at {Address}", signature);
            return false;
        }

        if (context.Image.ReadByte(entry) == 0xE9)
        {
            _logger.LogDebug("The equipment index switch at {Address} is already replaced", entry);
            return true;
        }

        var cave = context.Process.AllocateExecutable(LookupCaveSize);
        context.Process.WriteCode(cave, BuildIndexTranslation(cave));

        // Under a suspension: this replaces a function the client calls while it draws,
        // and the five bytes span more than one instruction of the original prologue.
        using (context.Process.SuspendThreads())
        {
            context.Image.Apply(context.Process, entry, InlineHook.BuildJump(entry, cave));
        }

        _logger.LogDebug("Equipment index translation at {Entry} replaced by {Cave}", entry, cave);
        return true;
    }

    /// <summary>
    /// Replaces the switch with a bounds-checked table lookup covering the full range of
    /// server indices.
    /// </summary>
    /// <remarks>
    /// The table is written into the same allocation, immediately after the code, so the
    /// lookup can address it as a literal.
    /// </remarks>
    internal static byte[] BuildIndexTranslation(GameAddress cave)
    {
        var table = cave + LookupCodeSize;
        var code = new ShellcodeBuilder(cave);

        code.Bytes([0x55, 0x8B, 0xEC])                       // push ebp; mov ebp, esp
            .Bytes([0x8B, 0x45, 0x08])                       // mov eax, [ebp+8]   (server index)
            .Bytes([0x83, 0xF8, HighestServerIndex]);        // cmp eax, 31

        var tooHigh = code.ShortJump(0x77);                  // ja unmapped

        code.Bytes([0x85, 0xC0]);                            // test eax, eax
        var zero = code.ShortJump(0x74);                     // jz unmapped

        code.Bytes([0x0F, 0xB6, 0x80]).Dword(table.Value)    // movzx eax, byte [eax+table]
            .Bytes([0x5D, 0xC2, 0x04, 0x00]);                // pop ebp; ret 4

        code.MarkLabel(tooHigh).MarkLabel(zero);
        code.Bytes([0x31, 0xC0])                             // xor eax, eax
            .Bytes([0x5D, 0xC2, 0x04, 0x00]);                // pop ebp; ret 4

        if (code.Length != LookupCodeSize)
        {
            throw new InvalidOperationException(
                $"The lookup is {code.Length} bytes, but the table is addressed at {LookupCodeSize}.");
        }

        return [.. code.Build(), .. SlotLookup];
    }

    /// <summary>
    /// Walks back from a signature to the <c>push ebp; mov ebp, esp</c> that starts the
    /// function containing it.
    /// </summary>
    private static GameAddress? FindFunctionEntry(GamePatchContext context, GameAddress inside)
    {
        const int SearchBack = 0x30;

        var window = new byte[SearchBack];
        var start = inside - SearchBack;

        if (!context.Image.TryRead(start, window))
        {
            return null;
        }

        // Backwards, because the nearest prologue above the signature is the one that
        // owns it; an earlier one belongs to a different function.
        for (var offset = window.Length - 3; offset >= 0; offset--)
        {
            if (window[offset] == 0x55 && window[offset + 1] == 0x8B && window[offset + 2] == 0xEC)
            {
                return start + offset;
            }
        }

        return null;
    }

    // ---- window setup ----------------------------------------------------------------------

    private bool AppendSlotsToWindow(GamePatchContext context)
    {
        if (FindInWindow(context, SetupLoopSignature) is not { } loop)
        {
            _logger.LogWarning("Could not find the equipment window setup loop");
            return false;
        }

        var exit = loop + SetupExitOffset;
        var helperCall = loop + SetupHelperCallOffset;
        var backgroundIndex = loop + BackgroundIndexOffset;

        if (context.Image.ReadByte(exit) == 0xE9)
        {
            _logger.LogDebug("The equipment window setup at {Address} is already hooked", exit);
            return true;
        }

        var epilogue = new byte[SetupEpilogue.Length];
        if (!context.Image.TryRead(exit, epilogue) || !epilogue.AsSpan().SequenceEqual(SetupEpilogue))
        {
            _logger.LogWarning("The setup epilogue at {Address} does not match", exit);
            return false;
        }

        // Taken from the client's own call rather than written down, because it is the one
        // address here that is genuinely internal and has no signature of its own.
        if (ResolveCallTarget(context, helperCall) is not { } helper)
        {
            _logger.LogWarning("No call to the slot helper at {Address}", helperCall);
            return false;
        }

        var calculation = new byte[BackgroundIndexCalculation.Length];
        if (!context.Image.TryRead(backgroundIndex, calculation) ||
            !calculation.AsSpan().SequenceEqual(BackgroundIndexCalculation))
        {
            _logger.LogWarning("The background index calculation at {Address} does not match", backgroundIndex);
            return false;
        }

        var cave = context.Process.AllocateExecutable(SetupCaveSize);
        var appendHook = cave;
        var backgroundHook = cave + BackgroundHookOffset;

        context.Process.WriteCode(appendHook, BuildAppendedSlots(appendHook, helper));
        context.Process.WriteCode(backgroundHook, BuildBackgroundIndex());

        context.Image.Apply(context.Process, exit, InlineHook.BuildJump(exit, appendHook));

        // The calculation is replaced by a call to the replacement plus the push it used
        // to end with, so the seven bytes stay seven bytes.
        context.Image.Apply(context.Process, backgroundIndex,
        [
            .. BuildCall(backgroundIndex, backgroundHook),
            0x51,  // push ecx
            0x90,  // nop
        ]);

        _logger.LogDebug("Equipment window setup hooked at {Exit} and {Background}", exit, backgroundIndex);
        return true;
    }

    /// <summary>
    /// Runs the client's own slot helper six more times, for the appended slots, then
    /// performs the epilogue this replaced.
    /// </summary>
    /// <remarks>
    /// Appending rather than widening the client's loop: the extra slots sit at different
    /// child indices, so the loop's own index arithmetic does not reach them. Running the
    /// helper directly keeps every other part of slot setup identical to the original.
    /// </remarks>
    internal static byte[] BuildAppendedSlots(GameAddress cave, GameAddress helper)
    {
        var code = new ShellcodeBuilder(cave);

        // The client's frame is still live: [ebp-8] is its loop counter, which is finished
        // with, [ebp-4] holds the parent window and [ebp-0xC] holds this.
        code.Bytes([0xC7, 0x45, 0xF8, 0x00, 0x00, 0x00, 0x00]);  // mov [ebp-8], 0

        var top = code.Here();
        code.Bytes([0x83, 0x7D, 0xF8, AppendedSlotCount]);       // cmp [ebp-8], 6
        var done = code.ShortJump(0x7D);                         // jge done

        code.Bytes([0x6A, 0x00])                                 // push 0   (not visible yet)
            .Bytes([0x6A, 0x00])                                 // push 0   (no item)
            .Bytes([0x8B, 0x55, 0xF8])                           // mov edx, [ebp-8]
            .Bytes([0x83, 0xC2, FirstAppendedSlot])              // add edx, 46
            .Byte(0x52)                                          // push edx (child index)
            .Bytes([0x8B, 0x45, 0xFC])                           // mov eax, [ebp-4]
            .Byte(0x50)                                          // push eax (parent window)
            .Bytes([0x8B, 0x4D, 0xF4])                           // mov ecx, [ebp-0xC] (this)
            .CallTo(helper)
            .Bytes([0xFF, 0x45, 0xF8])                           // inc dword [ebp-8]
            .ShortJumpBack(0xEB, top);

        code.MarkLabel(done);
        code.Bytes([0x8B, 0xE5, 0x5D, 0xC3]);                    // mov esp,ebp; pop ebp; ret

        return code.Build();
    }

    /// <summary>
    /// Works out where a slot's background image sits, using one offset for the client's
    /// own slots and another for the appended ones.
    /// </summary>
    internal static byte[] BuildBackgroundIndex() =>
    [
        0x8B, 0x4D, 0x0C,                            // mov ecx, [ebp+0xC]  (child index)
        0x83, 0xF9, FirstAppendedSlot,               // cmp ecx, 46
        0x7C, 0x04,                                  // jl original
        0x83, 0xC1, AppendedBackgroundOffset, 0xC3,  // add ecx, 6; ret
        0x83, 0xC1, OriginalBackgroundOffset, 0xC3,  // original: add ecx, 0x1A; ret
    ];

    // ---- sprite bounds -----------------------------------------------------------------------

    private bool WidenSpriteBounds(GamePatchContext context)
    {
        var edit = CodeEdit.ReplaceAt(
            context, SpriteBoundsCheck, "the equipment sprite bounds check",
            SpriteBoundsOriginal, SpriteBoundsWidened);

        if (!edit.Effective)
        {
            // The reference wrote here regardless of what it found, having only logged a
            // warning. Six bytes over an instruction that is not the one expected is a
            // crash somewhere else entirely.
            _logger.LogWarning("The sprite bounds check at {Address} does not match", SpriteBoundsCheck);
        }

        return edit.Effective;
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static GameAddress? FindInWindow(GamePatchContext context, string signature) =>
        context.Image.FindAll(BytePattern.Parse(signature))
            .Where(hit => hit >= ScanStart && hit < ScanEnd)
            .Cast<GameAddress?>()
            .FirstOrDefault();

    /// <summary>Reads where an existing <c>call rel32</c> goes.</summary>
    private static GameAddress? ResolveCallTarget(GamePatchContext context, GameAddress site)
    {
        var instruction = new byte[5];

        if (!context.Image.TryRead(site, instruction) || instruction[0] != 0xE8)
        {
            return null;
        }

        return new GameAddress(unchecked((uint)(site.Value + 5 + BitConverter.ToInt32(instruction, 1))));
    }

    private static byte[] BuildCall(GameAddress from, GameAddress to) =>
        [0xE8, .. BitConverter.GetBytes(to - (from + 5))];
}
