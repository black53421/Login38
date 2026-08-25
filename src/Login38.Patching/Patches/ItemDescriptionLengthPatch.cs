using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Stops long item descriptions from crashing the client.
/// </summary>
/// <remarks>
/// <para>
/// The client wraps a description by calling a helper that writes one line break per line
/// into a table. The table is thirty-two entries on the caller's stack, immediately below
/// the stack cookie; the helper has no idea how big it is. A description that wraps past
/// thirty-two lines writes over the cookie, and the client exits with
/// <c>0xC0000409</c> — no message, no dump, and no obvious connection to the item the
/// player was looking at.
/// </para>
/// <para>
/// This replaces the wrapper outright. The new one hands the helper a page of its own on
/// the heap, which cannot overflow, and then copies as many breaks back into the client's
/// table as that table can actually hold. The full set stays in the record, for the line
/// rendering to read later.
/// </para>
/// <para>
/// How many fit depends on which caller it is, and the callers disagree: three of them
/// allocate thirty-two slots and the rest twenty. There is nothing in the arguments that
/// says which, so the cave reads its own return address — the only thing on the stack that
/// identifies the caller.
/// </para>
/// </remarks>
public sealed class ItemDescriptionLengthPatch : IGamePatch
{
    /// <summary>The wrapper: <c>wrap(text, breaks, count)</c>.</summary>
    private static readonly GameAddress Wrapper = new(0x004A_EC90);

    /// <summary><c>push ebp; mov ebp, esp; sub esp, 0x58</c>.</summary>
    private static ReadOnlySpan<byte> WrapperPrologue => [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x58];

    /// <summary>The client's own wrapping algorithm, which the cave still calls.</summary>
    /// <remarks>
    /// Kept rather than reimplemented. It decides where a line breaks, which has to match
    /// what the client draws — and the only thing wrong with it is where it writes.
    /// </remarks>
    private static readonly GameAddress WrapHelper = new(0x0042_AF10);

    private static readonly GameAddress StringLength = new(0x0079_A12C);

    /// <summary>The client's import slot for <c>strdup</c>.</summary>
    private static readonly GameAddress DuplicateStringSlot = new(0x008C_8664);

    /// <summary>Characters per line, as the client's own wrapper sets it.</summary>
    private const uint LineWidth = 0x17;

    /// <summary>
    /// How many breaks fit in the smallest caller's table.
    /// </summary>
    /// <remarks>
    /// The smallest is twenty integers, cleared by a <c>memset</c> of eighty bytes. Filling
    /// all twenty would leave nothing to say the list ended, and writing past them lands in
    /// whatever the owning object keeps next — which is not a crash but a heap that goes
    /// wrong some time later, in code that has nothing to do with descriptions.
    /// </remarks>
    private const uint DefaultCap = 19;

    /// <summary>The same, for the callers whose table is thirty-two.</summary>
    private const uint TooltipCap = 31;

    /// <summary>
    /// Return addresses of the three callers whose table holds thirty-two.
    /// </summary>
    /// <remarks>
    /// Identified by the loop each of them clears the table with — <c>for i &lt; 0x20</c> —
    /// rather than by what the function is called. The inventory tooltip is one of them,
    /// which is where a long description is actually read.
    /// </remarks>
    private static readonly GameAddress[] LargeTableCallers =
        [new(0x004A_F04A), new(0x004A_F1C3), new(0x0045_AA02)];

    /// <summary>Selects the record for a call, from the table it was given.</summary>
    /// <remarks>
    /// A hash rather than a list, because the cave has to find the record again in a few
    /// instructions with no allocator and no lock. A collision costs one description its
    /// full line set, which the rendering falls back from cleanly.
    /// </remarks>
    private const byte RecordMask = 0x3F;

    /// <inheritdoc cref="RecordMask"/>
    private const byte RecordShift = 0x0C;

    private const int RecordStride = 1 << RecordShift;

    private const int RecordCount = RecordMask + 1;

    private const int StateSize = RecordCount * RecordStride;

    private const int CaveSize = 0x200;

    // Record layout.
    private const byte RecordTable = 0x10;

    private readonly ILogger<ItemDescriptionLengthPatch> _logger;

    public ItemDescriptionLengthPatch(ILogger<ItemDescriptionLengthPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "item-description-length";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;
        var state = process.AllocateExecutable(StateSize);
        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave, state);

        if (shellcode.Length > CaveSize)
        {
            throw new GameProcessException(
                $"The description wrapper shellcode is {shellcode.Length} bytes but the cave is {CaveSize}.");
        }

        process.WriteCode(cave, shellcode);

        _logger.LogInformation(
            "Description wrapping replaced by a cave at {Cave}, with {Records} records at {State}",
            cave, RecordCount, state);

        // The wrapper is decrypted only when the client first shows a description, which may
        // be minutes in. Not awaited, for the same reason.
        _ = Task.Run(() => WatchAsync(process, cave, cancellationToken), CancellationToken.None);
    }

    private async Task WatchAsync(RemoteProcess process, GameAddress cave, CancellationToken cancellationToken)
    {
        try
        {
            await DeferredSites.WatchAsync(
                process, [Wrapper], _ => Divert(process, cave),
                "Item description wrapping", _logger,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The launcher is closing.
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning(e, "Stopped waiting for the description wrapper");
        }
    }

    /// <summary>Points the wrapper at the cave, once it is the wrapper.</summary>
    private static SiteAttempt Divert(RemoteProcess process, GameAddress cave)
    {
        Span<byte> prologue = stackalloc byte[WrapperPrologue.Length];

        if (!process.TryReadBytes(Wrapper, prologue))
        {
            return SiteAttempt.NotReady;
        }

        if (prologue[0] == InlineHook.JumpOpcode)
        {
            return SiteAttempt.Settled;
        }

        if (!prologue.SequenceEqual(WrapperPrologue))
        {
            return SiteAttempt.NotReady;
        }

        // The whole prologue is replaced, not just the five bytes of the jump: anything left
        // of it would be reached by nothing and disassemble as a fragment.
        process.WriteCode(Wrapper, InlineHook.BuildJump(Wrapper, cave, WrapperPrologue.Length));
        return SiteAttempt.Settled;
    }

    /// <summary>
    /// Assembles the replacement wrapper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>wrap(text, breaks, count)</c>, <c>cdecl</c>. It keeps the client's own frame shape
    /// and calls the client's own helper — the only differences are where the helper writes
    /// and how much of that comes back.
    /// </para>
    /// <para>
    /// No table on the stack, so no cookie to overrun and none to check. The record is a
    /// page of heap chosen by hashing the caller's own table pointer, which is the only
    /// value both this and the later rendering will have in common.
    /// </para>
    /// </remarks>
    internal static byte[] BuildShellcode(GameAddress cave, GameAddress state)
    {
        var code = new ShellcodeBuilder(cave);

        code.Bytes([0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x04])            // push ebp; mov ebp,esp; sub esp,4
            .Bytes([0x56, 0x57, 0x53]);                             // push esi; push edi; push ebx

        code.Bytes([0x8B, 0x45, 0x08, 0x85, 0xC0]);                 // mov eax,[ebp+8]; test eax,eax
        var haveText = code.ShortJumpIfNotEqual();

        // No text is no lines. The client asks for this on an item with no description.
        code.Bytes([0x8B, 0x4D, 0x0C])                              // mov ecx, [ebp+0xC]
            .Bytes([0xC7, 0x01, 0x00, 0x00, 0x00, 0x00])            // mov dword [ecx], 0
            .Bytes([0x33, 0xC0]);                                   // xor eax, eax
        EmitEpilogue(code);

        code.MarkLabel(haveText);

        code.Bytes([0xC7, 0x45, 0xFC]).Dword(LineWidth)             // mov dword [ebp-4], 0x17
            .Bytes([0x8B, 0x75, 0x0C]);                             // mov esi, [ebp+0xC] — the table

        // record = state + ((table >> 2) & 0x3F) << 0xC
        code.Bytes([0x8B, 0xC6, 0xC1, 0xE8, 0x02])                  // mov eax,esi; shr eax,2
            .Bytes([0x83, 0xE0, RecordMask])                        // and eax, 0x3F
            .Bytes([0xC1, 0xE0, RecordShift])                       // shl eax, 0xC
            .Byte(0xBF).Dword(state.Value)                          // mov edi, state
            .Bytes([0x03, 0xF8]);                                   // add edi, eax

        code.Bytes([0x89, 0x37])                                    // mov [edi], esi — tag
            .Bytes([0x8B, 0x45, 0x08, 0x89, 0x47, 0x04, 0x50])      // mov eax,[ebp+8]; mov [edi+4],eax; push eax
            .CallTo(StringLength)
            .AddEsp(0x04);

        // helper(text, length, record.table, &width, 0, 0), right to left.
        code.PushImm8(0).PushImm8(0)
            .Bytes([0x8D, 0x4D, 0xFC, 0x51])                        // lea ecx,[ebp-4]; push ecx
            .Bytes([0x8D, 0x57, RecordTable, 0x52])                 // lea edx,[edi+0x10]; push edx
            .Bytes([0x50, 0x8B, 0x45, 0x08, 0x50])                  // push eax; mov eax,[ebp+8]; push eax
            .CallTo(WrapHelper)
            .AddEsp(0x18);

        code.Bytes([0x89, 0x47, 0x08, 0x8B, 0xD8]);                 // mov [edi+8],eax; mov ebx,eax

        EmitCallerCap(code);

        code.Bytes([0x89, 0x5F, 0x0C])                              // mov [edi+0xC], ebx
            .Bytes([0x8B, 0x4D, 0x10, 0x89, 0x19, 0x33, 0xC9]);     // mov ecx,[ebp+0x10]; mov [ecx],ebx; xor ecx,ecx

        EmitCopyBreaks(code);
        EmitFlattenNewlines(code);

        code.Bytes([0x8B, 0x45, 0x08, 0x50])                        // mov eax,[ebp+8]; push eax
            .CallIndirect(DuplicateStringSlot)
            .AddEsp(0x04)
            .Bytes([0x89, 0x47, 0x04]);                             // mov [edi+4], eax

        EmitEpilogue(code);

        return code.Build();
    }

    /// <summary>
    /// Leaves the caller's cap in <c>edx</c>.
    /// </summary>
    /// <remarks>
    /// The return address is the only thing that says which caller this is. Three of them
    /// clear thirty-two slots; everything else, known or not, gets the cap that is safe for
    /// the smallest table anyone has.
    /// </remarks>
    private static void EmitCallerCap(ShellcodeBuilder code)
    {
        code.Byte(0xBA).Dword(DefaultCap);                          // mov edx, 19

        var isLarge = new List<ShellcodeBuilder.Label>();

        foreach (var caller in LargeTableCallers)
        {
            code.Bytes([0x81, 0x7D, 0x04]).Dword(caller.Value);     // cmp dword [ebp+4], caller
            isLarge.Add(code.ShortJump(JumpIfEqualShort));
        }

        var capped = code.ShortJumpAlways();

        foreach (var match in isLarge)
        {
            code.MarkLabel(match);
        }

        code.Byte(0xBA).Dword(TooltipCap);                          // mov edx, 31

        code.MarkLabel(capped);
        code.Bytes([0x3B, 0xDA]);                                   // cmp ebx, edx
        var withinCap = code.ShortJump(JumpIfBelowOrEqualShort);
        code.Bytes([0x8B, 0xDA]);                                   // mov ebx, edx
        code.MarkLabel(withinCap);
    }

    /// <summary>
    /// Copies the capped number of breaks into the caller's table.
    /// </summary>
    /// <remarks>
    /// The helper writes sixteen-bit offsets and the client's table holds thirty-two, and
    /// the first entry of the helper's is not a break — so this is a widening copy from the
    /// second element on rather than a block move.
    /// </remarks>
    private static void EmitCopyBreaks(ShellcodeBuilder code)
    {
        var loop = code.Here();

        code.Bytes([0x3B, 0xCB]);                                   // cmp ecx, ebx
        var done = code.ShortJump(JumpIfGreaterOrEqualShort);

        code.Bytes([0x0F, 0xBF, 0x44, 0x4F, RecordTable + 2])       // movsx eax, word [edi+ecx*2+0x12]
            .Bytes([0x8B, 0x55, 0x0C, 0x89, 0x04, 0x8A, 0x41])      // mov edx,[ebp+0xC]; mov [edx+ecx*4],eax; inc ecx
            .ShortJumpBack(JumpAlwaysShort, loop);

        code.MarkLabel(done);
    }

    /// <summary>
    /// Turns newlines in the description into spaces.
    /// </summary>
    /// <remarks>
    /// What the client's own wrapper did, and it does it in place — the text belongs to the
    /// caller. The breaks are the line structure now, so a newline left in the string would
    /// draw as a box and break a line the wrapper did not intend.
    /// </remarks>
    private static void EmitFlattenNewlines(ShellcodeBuilder code)
    {
        code.Bytes([0x33, 0xC9]);                                   // xor ecx, ecx

        var loop = code.Here();

        code.Bytes([0x8B, 0x55, 0x08, 0x8A, 0x04, 0x0A, 0x84, 0xC0]);  // mov edx,[ebp+8]; mov al,[edx+ecx]; test al,al
        var end = code.ShortJumpIfZero();

        code.Bytes([0x3C, 0x0A]);                                   // cmp al, '\n'
        var notANewline = code.ShortJumpIfNotEqual();

        code.Bytes([0xC6, 0x04, 0x0A, 0x20]);                       // mov byte [edx+ecx], ' '

        code.MarkLabel(notANewline);
        code.Byte(0x41)                                             // inc ecx
            .ShortJumpBack(JumpAlwaysShort, loop);

        code.MarkLabel(end);
    }

    private static void EmitEpilogue(ShellcodeBuilder code) =>
        code.Bytes([0x5B, 0x5F, 0x5E, 0x8B, 0xE5, 0x5D, 0xC3]);     // pop ebx/edi/esi; mov esp,ebp; pop ebp; ret

    private const byte JumpIfEqualShort = 0x74;
    private const byte JumpIfBelowOrEqualShort = 0x76;
    private const byte JumpIfGreaterOrEqualShort = 0x7D;
    private const byte JumpAlwaysShort = 0xEB;
}
