using Login38.Core.Morph.SmoothRun;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Makes hasted characters run instead of shuffling.
/// </summary>
/// <remarks>
/// <para>
/// The client animates a character by looking one action slot up in a table and playing it.
/// Under a speed effect it plays the walk faster, which reads as a shuffle: the feet move
/// at double speed and the character still leads with the same foot every stride.
/// </para>
/// <para>
/// The morph table this launcher feeds the client carries a real run cycle in slots 98 and
/// 99, one for each foot. This diverts the table lookup: for a walking action, on a
/// character that is hasted, it returns one of those two slots instead of the walk.
/// </para>
/// <para>
/// Which foot is a per-character decision, so the cave keeps a small table of its own keyed
/// on the character's address. The foot flips when that character's animation wraps back to
/// its first frame, which is what makes a stride land on a whole animation rather than
/// switching mid-step.
/// </para>
/// <para>
/// The lookup is on the client's render path, several times a frame per visible character.
/// Everything here is a compare and a branch; the two paths that do no work — not hasted,
/// not a walk — are the first things checked.
/// </para>
/// </remarks>
public sealed class SmoothRunPatch : IGamePatch
{
    /// <summary>
    /// <c>mov eax, [edx+eax*8+4]; pop ebp</c> — the action table lookup and the return.
    /// </summary>
    /// <remarks>
    /// Mid-function rather than at the entry, because this is the one point where the table
    /// base and the action number are both in registers and the result has not been used yet.
    /// </remarks>
    private static readonly GameAddress HookSite = new(0x0044_9776);

    private static ReadOnlySpan<byte> SiteBytes => [0x8B, 0x44, 0xC2, 0x04, 0x5D];

    /// <summary>
    /// The address range the movement code occupies.
    /// </summary>
    /// <remarks>
    /// The lookup is called from several places and only movement should be redirected —
    /// a hasted character's attack animation is still its attack animation. The caller's
    /// return address is what tells them apart.
    /// </remarks>
    private static readonly GameAddress MovementLow = new(0x005A_A000);

    /// <inheritdoc cref="MovementLow"/>
    private static readonly GameAddress MovementHigh = new(0x005A_AA00);

    /// <summary><c>[ebp+0xC]</c> — the action slot being looked up.</summary>
    private const int ActionSlot = 0x0C;

    /// <summary><c>[ebp+4]</c> — the caller's return address.</summary>
    private const int ReturnAddress = 0x04;

    /// <summary><c>[ebp]</c> — the caller's own frame pointer.</summary>
    private const int CallerFrame = 0x00;

    /// <summary>Where the movement code keeps the character it is moving.</summary>
    private const sbyte EntityInCallerFrame = -0x5C;

    /// <summary>The character's haste flag.</summary>
    private const byte HasteFlag = 0x29;

    /// <summary>The character's current animation frame.</summary>
    private const byte AnimationFrame = 0x17;

    /// <summary>
    /// The smallest value a slot's frame pointer can hold and still be a pointer.
    /// </summary>
    /// <remarks>
    /// Slots 98 and 99 are empty in every table this launcher did not preprocess, and in a
    /// preprocessed one they are empty for every sprite that had no run cycle to borrow.
    /// An empty slot holds a small number rather than a pointer, so this tells the two apart
    /// without a second table to consult.
    /// </remarks>
    private const uint LowestPlausiblePointer = 0x0001_0000;

    /// <summary>Where the per-character foot table starts inside the cave.</summary>
    /// <remarks>
    /// Past anything the code reaches, so the two never overlap however the code changes.
    /// </remarks>
    private const int FootTableOffset = 0x200;

    /// <summary>
    /// How many characters the foot table remembers, and the mask that indexes it.
    /// </summary>
    /// <remarks>
    /// Far fewer than can be on screen. A collision costs one character a mistimed foot flip
    /// for one stride, which is not worth a byte of the render path to avoid — and the entry
    /// is retagged and reset the moment the other character uses it.
    /// </remarks>
    private const byte FootTableMask = 0x3F;

    private const int FootTableEntries = FootTableMask + 1;

    private const int FootTableEntrySize = 4;

    private const int CaveSize = FootTableOffset + (FootTableEntries * FootTableEntrySize);

    private readonly ILogger<SmoothRunPatch> _logger;

    public SmoothRunPatch(ILogger<SmoothRunPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "smooth-run";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // There is nothing in slots 98 and 99 unless the morph table was preprocessed, and
        // this reads what actually happened rather than what was configured — the reference
        // gated it on the setting, so a table that failed to load left the hook reading
        // slots the client had never filled.
        return context.MorphTableHasRunCycles;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;

        if (InlineHook.IsInstalledAt(process, HookSite))
        {
            _logger.LogDebug("The action lookup at {Address} is already diverted", HookSite);
            return;
        }

        var current = process.ReadBytes(HookSite, SiteBytes.Length);

        if (!current.AsSpan().SequenceEqual(SiteBytes))
        {
            throw new GameProcessException(
                $"Expected the action lookup at {HookSite} to be {BytePattern.Format(SiteBytes)} " +
                $"but found {BytePattern.Format(current)}.");
        }

        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave);

        if (shellcode.Length > FootTableOffset)
        {
            throw new GameProcessException(
                $"The smooth run shellcode is {shellcode.Length} bytes and would overrun the " +
                $"foot table at {FootTableOffset:X}.");
        }

        process.WriteCode(cave, shellcode);
        InlineHook.InstallJump(process, HookSite, cave);

        _logger.LogInformation(
            "Hasted characters now run, through a cave at {Cave} ({Bytes} bytes of code)", cave, shellcode.Length);
    }

    /// <summary>
    /// Assembles the diverted lookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entered by a jump from the middle of the client's own function, so <c>ebp</c> is that
    /// function's frame and the code ends by replaying the <c>pop ebp; ret</c> it displaced.
    /// <c>eax</c>, <c>ecx</c> and <c>edx</c> are the function's own scratch; <c>edi</c> is
    /// not, and is saved around the one stretch that uses it.
    /// </para>
    /// <para>
    /// Every path leaves through one exit with the answer in <c>eax</c> — either the
    /// original lookup's or a run slot's.
    /// </para>
    /// </remarks>
    internal static byte[] BuildShellcode(GameAddress cave)
    {
        var footTable = cave + FootTableOffset;
        var code = new ShellcodeBuilder(cave);

        // The displaced lookup, so that eax holds the original answer on every path that
        // decides not to change it.
        code.Bytes([0x8B, 0x45, ActionSlot])                        // mov eax, [ebp+0xC]
            .Bytes([0x8B, 0x44, 0xC2, 0x04]);                       // mov eax, [edx+eax*8+4]

        // Nothing in slot 98 means this sprite has no run cycle. Checked first because it
        // is the common case for scenery and for anything the table did not cover.
        code.Bytes([0x81, 0xBA]).Dword(RunSlot.Left.FrameDataOffset()).Dword(LowestPlausiblePointer);
        var noRunCycle = code.NearJump(JumpIfBelow);

        var isWalk = EmitWalkCheck(code);

        // A caller outside the movement code is animating something other than a step.
        code.Bytes([0x8B, 0x4D, ReturnAddress]);                    // mov ecx, [ebp+4]
        code.Bytes([0x81, 0xF9]).Dword(MovementLow.Value);
        var beforeMovement = code.NearJump(JumpIfBelow);
        code.Bytes([0x81, 0xF9]).Dword(MovementHigh.Value);
        var afterMovement = code.NearJump(JumpIfAbove);

        code.Bytes([0x8B, 0x4D, CallerFrame])                       // mov ecx, [ebp]
            .Bytes([0x8B, 0x49, unchecked((byte)EntityInCallerFrame)])  // mov ecx, [ecx-0x5C]
            .Bytes([0x85, 0xC9]);                                   // test ecx, ecx
        var noEntity = code.NearJump(JumpIfZero);

        // Saved because the not-hasted path has to put it back, and because the foot
        // selection below needs eax.
        code.Byte(0x50);                                            // push eax
        code.Bytes([0x80, 0x79, HasteFlag, 0x00]);                  // cmp byte [ecx+0x29], 0
        var notHasted = code.ShortJumpIfZero();

        EmitFootSelection(code, footTable);

        code.Byte(0x5F)                                             // pop edi
            .Bytes([0x85, 0xC0])                                    // test eax, eax
            .Byte(0x59);                                            // pop ecx — discard, flags kept
        var useRight = code.ShortJump(JumpIfNotZeroShort);

        code.Bytes([0x8B, 0x82]).Dword(RunSlot.Left.FrameDataOffset());
        var leftDone = code.NearJumpAlways();

        code.MarkLabel(useRight);
        var rightPaths = EmitRightFoot(code);

        code.MarkLabel(notHasted);
        code.Byte(0x58);                                            // pop eax — the original answer

        foreach (var label in (ShellcodeBuilder.NearLabel[])
                 [noRunCycle, isWalk, beforeMovement, afterMovement, noEntity, leftDone])
        {
            code.MarkLabel(label);
        }

        foreach (var label in rightPaths)
        {
            code.MarkLabel(label);
        }

        return code.Byte(0x5D)                                      // pop ebp
                   .Byte(0xC3)                                      // ret
                   .Build();
    }

    /// <summary>
    /// Emits the check that the action being looked up is a step.
    /// </summary>
    /// <returns>A branch to the exit, taken when it is not.</returns>
    /// <remarks>
    /// One compare per walk slot rather than a table: fourteen compares against an immediate
    /// are shorter than the code to index a table, and this is on the render path.
    /// </remarks>
    private static ShellcodeBuilder.NearLabel EmitWalkCheck(ShellcodeBuilder code)
    {
        code.Bytes([0x8B, 0x4D, ActionSlot])                        // mov ecx, [ebp+0xC]
            .Bytes([0x85, 0xC9]);                                   // test ecx, ecx

        var matches = new List<ShellcodeBuilder.Label> { code.ShortJumpIfZero() };

        foreach (var slot in WalkActions.All)
        {
            if (slot == 0)
            {
                // Already covered by the test above, which is a byte shorter than comparing
                // against zero.
                continue;
            }

            code.Bytes([0x83, 0xF9, (byte)slot]);                   // cmp ecx, slot
            matches.Add(code.ShortJump(JumpIfEqualShort));
        }

        var notAWalk = code.NearJumpAlways();

        foreach (var match in matches)
        {
            code.MarkLabel(match);
        }

        return notAWalk;
    }

    /// <summary>
    /// Emits the per-character foot table lookup, leaving the chosen foot in <c>eax</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each entry is the low half of the character's address as a tag, the animation frame
    /// last seen, and the foot. The tag is what makes a collision self-correcting: the entry
    /// is rebuilt for whichever character reaches it, rather than being read as that
    /// character's own state.
    /// </para>
    /// <para>
    /// The foot flips when the animation frame goes backwards, which happens once per
    /// animation. Flipping on anything else would switch feet mid-stride.
    /// </para>
    /// </remarks>
    private static void EmitFootSelection(ShellcodeBuilder code, GameAddress footTable)
    {
        code.Byte(0x57);                                            // push edi

        // The address shifted down three, because characters are allocated at least eight
        // bytes apart and the low bits are always the same.
        code.Bytes([0x89, 0xC8])                                    // mov eax, ecx
            .Bytes([0xC1, 0xE8, 0x03])                              // shr eax, 3
            .Bytes([0x83, 0xE0, FootTableMask])                     // and eax, 0x3F
            .Bytes([0xC1, 0xE0, 0x02])                              // shl eax, 2
            .Byte(0x05).Dword(footTable.Value)                      // add eax, footTable
            .Bytes([0x89, 0xC7]);                                   // mov edi, eax

        code.Bytes([0x66, 0x39, 0x0F]);                             // cmp word [edi], cx
        var newEntry = code.ShortJumpIfNotEqual();

        code.Bytes([0x0F, 0xB6, 0x41, AnimationFrame])              // movzx eax, byte [ecx+0x17]
            .Bytes([0x3A, 0x47, 0x02]);                             // cmp al, [edi+2]
        var noWrap = code.ShortJump(JumpIfAboveOrEqualShort);

        code.Bytes([0x80, 0x77, 0x03, 0x01]);                       // xor byte [edi+3], 1

        code.MarkLabel(noWrap);
        code.Bytes([0x88, 0x47, 0x02]);                             // mov [edi+2], al
        var stored = code.ShortJumpAlways();

        code.MarkLabel(newEntry);
        code.Bytes([0x66, 0x89, 0x0F])                              // mov word [edi], cx
            .Bytes([0x0F, 0xB6, 0x41, AnimationFrame])              // movzx eax, byte [ecx+0x17]
            .Bytes([0x88, 0x47, 0x02])                              // mov [edi+2], al
            .Bytes([0xC6, 0x47, 0x03, 0x00]);                       // mov byte [edi+3], 0

        // A character first seen mid-stride starts on the foot its action implies, so that
        // it does not visibly reset when it comes into view.
        code.Bytes([0x8B, 0x45, ActionSlot])                        // mov eax, [ebp+0xC]
            .Bytes([0x85, 0xC0]);                                   // test eax, eax
        var startsOnLeft = code.ShortJumpIfZero();
        code.Bytes([0xC6, 0x47, 0x03, 0x01]);                       // mov byte [edi+3], 1

        code.MarkLabel(stored).MarkLabel(startsOnLeft);
        code.Bytes([0x0F, 0xB6, 0x47, 0x03]);                       // movzx eax, byte [edi+3]
    }

    /// <summary>
    /// Emits the right-foot path, which falls back to the left when slot 99 is empty.
    /// </summary>
    /// <remarks>
    /// A table can carry one half of a run cycle and not the other, because the source
    /// sprite had one side whose name said it was something else. Half a run cycle played
    /// on both feet still reads better than a shuffle.
    /// </remarks>
    private static ShellcodeBuilder.Label[] EmitRightFoot(ShellcodeBuilder code)
    {
        code.Bytes([0x81, 0xBA]).Dword(RunSlot.Right.FrameDataOffset()).Dword(LowestPlausiblePointer);
        var empty = code.ShortJump(JumpIfBelowShort);

        code.Bytes([0x8B, 0x82]).Dword(RunSlot.Right.FrameDataOffset());
        var done = code.ShortJumpAlways();

        code.MarkLabel(empty);
        code.Bytes([0x8B, 0x82]).Dword(RunSlot.Left.FrameDataOffset());
        var fallbackDone = code.ShortJumpAlways();

        return [done, fallbackDone];
    }

    private const byte JumpIfBelow = 0x82;
    private const byte JumpIfZero = 0x84;
    private const byte JumpIfAbove = 0x87;
    private const byte JumpIfBelowShort = 0x72;
    private const byte JumpIfAboveOrEqualShort = 0x73;
    private const byte JumpIfEqualShort = 0x74;
    private const byte JumpIfNotZeroShort = 0x75;
}
