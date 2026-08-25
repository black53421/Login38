using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Widens hit points and mana from 16-bit fields to 32-bit ones, everywhere the client
/// touches them.
/// </summary>
/// <remarks>
/// <para>
/// The server sends these as 32-bit values on six packets; the client was built to read
/// 16-bit ones. A character with 70,000 HP reads back as 4,464 — and worse, the packet
/// parser goes on to read the rest of the packet from the wrong offset, so every field
/// after it is nonsense too.
/// </para>
/// <para>
/// There is no single place to fix. The values pass through packet format strings, a
/// shared 16-bit read helper, stack locals in three packet handlers, the character-select
/// structure, and a pair of global variables read and written from all over the client.
/// This widens each of those in turn; anything missed shows up as one wrong number rather
/// than as a crash, because every replacement is the same length as what it replaces.
/// </para>
/// <para>
/// This runs before the AC/MR expansion, which replaces the same two character-select
/// setters with versions that carry armour class as well as these values. That is
/// deliberate: the later patch supersedes the setters installed here and keeps writing
/// the same two globals, so the getters this installs stay correct either way.
/// </para>
/// </remarks>
public sealed class HitPointExpansionPatch : IGamePatch
{
    // ---- globals -----------------------------------------------------------------------

    private static readonly GameAddress MaxHitPoints = new(0x00C3_1E90);
    private static readonly GameAddress MaxManaPoints = new(0x00C3_1E8C);

    /// <summary>What the health bar renders from, separate from the value itself.</summary>
    private static readonly GameAddress DisplayedHitPoints = new(0x00C2_FDE0);

    private static readonly GameAddress DisplayedManaPoints = new(0x00C2_FDDC);

    /// <summary>
    /// The client's code. Narrower than the shared scan range because the globals are
    /// searched for as bare addresses, and the data sections are full of those.
    /// </summary>
    private static readonly GameAddress CodeStart = new(0x0040_1000);

    private static readonly GameAddress CodeEnd = new(0x008C_0000);

    // ---- packet format strings -----------------------------------------------------------

    /// <param name="Name">The packet, for logs.</param>
    /// <param name="Address">Where the field letters begin.</param>
    /// <param name="Original">The field letters as built.</param>
    private readonly record struct FormatFields(string Name, GameAddress Address, string Original)
    {
        /// <summary>Each <c>h</c> becomes a <c>d</c>: read four bytes instead of two.</summary>
        internal byte[] Widened => [.. Original.Select(_ => (byte)'d')];

        internal byte[] Expected => [.. Original.Select(c => (byte)c)];
    }

    private static readonly FormatFields[] Formats =
    [
        new("S_STATUS", new GameAddress(0x008D_46FD), "hhhh"),      // cur/max HP, cur/max MP
        new("S_CHARACTER_INFO", new GameAddress(0x008D_4DD1), "hh"), // max HP, max MP
        new("S_CHARSYNACK_1", new GameAddress(0x008D_72CC), "hh"),
        new("S_CHARSYNACK_2", new GameAddress(0x008D_72D6), "hh"),
    ];

    // ---- the 16-bit read helper -------------------------------------------------------

    /// <summary>The client's read-a-word-from-the-packet helper.</summary>
    private static readonly GameAddress ReadWord = new(0x0052_39F0);

    /// <summary>Call sites that must read a dword instead.</summary>
    private static readonly (string Name, GameAddress Site)[] ReadWordCallSites =
    [
        ("S_HIT_POINT current", new GameAddress(0x0052_3990)),
        ("S_HIT_POINT maximum", new GameAddress(0x0052_39AA)),
        ("S_MANA_POINT current", new GameAddress(0x0053_3800)),
        ("S_MANA_POINT maximum", new GameAddress(0x0053_381A)),
    ];

    /// <summary>
    /// <c>movsx edx, ax</c> after each call — pointless once the helper returns a full
    /// register, and destructive because it discards the high half.
    /// </summary>
    private static readonly (string Name, GameAddress Site)[] SignExtensionsAfterRead =
    [
        ("S_HIT_POINT", new GameAddress(0x0052_3998)),
        ("S_MANA_POINT", new GameAddress(0x0053_3808)),
    ];

    private static ReadOnlySpan<byte> SignExtendAxToEdx => [0x0F, 0xBF, 0xD0];

    private static ReadOnlySpan<byte> MoveEaxToEdx => [0x8B, 0xD0, 0x90];

    /// <summary>
    /// Places in the status handler that read a 32-bit local back as a word.
    /// </summary>
    /// <remarks>
    /// The parser writes the full value to the stack correctly; these three reads narrow
    /// it again on the way to the encrypted setter and to the health-percentage
    /// calculation. Their displacements are known, so they are patched directly rather
    /// than found by the general local scan below.
    /// </remarks>
    private static readonly (string Name, GameAddress Site, byte ModRm, byte Displacement)[] StatusLocalReads =
    [
        ("S_STATUS current HP to setter", new GameAddress(0x0052_3547), 0x55, 0xEC),
        ("S_STATUS current MP to setter", new GameAddress(0x0052_3556), 0x45, 0xF0),
        ("S_STATUS current HP to percentage", new GameAddress(0x0052_3585), 0x45, 0xEC),
    ];

    // ---- handler stack locals -----------------------------------------------------------

    /// <summary>ModR/M bytes for <c>[ebp+disp8]</c>, one per register.</summary>
    private static ReadOnlySpan<byte> BasePointerModRm => [0x45, 0x4D, 0x55, 0x5D, 0x65, 0x6D, 0x75, 0x7D];

    /// <summary>ModR/M bytes for a bare <c>[address]</c>, one per register.</summary>
    private static ReadOnlySpan<byte> DirectModRm => [0x05, 0x0D, 0x15, 0x1D, 0x25, 0x2D, 0x35, 0x3D];

    /// <param name="Name">The handler, for logs.</param>
    /// <param name="Start">First byte after the parser call.</param>
    /// <param name="End">Where the handler stops caring about these locals.</param>
    /// <param name="HitPointLocal">Displacement of the max-HP local.</param>
    /// <param name="ManaPointLocal">Displacement of the max-MP local.</param>
    private readonly record struct HandlerBody(
        string Name, GameAddress Start, GameAddress End, byte HitPointLocal, byte ManaPointLocal);

    private static readonly HandlerBody[] Handlers =
    [
        new("S_CHARACTER_INFO", new GameAddress(0x0052_CA78), new GameAddress(0x0052_CDD0), 0xF0, 0xC8),
        new("S_CHARSYNACK_1", new GameAddress(0x0054_39B5), new GameAddress(0x0054_3A28), 0xF8, 0xF4),
        new("S_CHARSYNACK_2", new GameAddress(0x0054_3A64), new GameAddress(0x0054_3C00), 0xEC, 0xE0),
    ];

    // ---- character select -----------------------------------------------------------------

    private static readonly GameAddress CharacterSetter1 = new(0x0054_4910);
    private static readonly GameAddress CharacterSetter2 = new(0x0054_4940);
    private static readonly GameAddress HitPointGetter = new(0x0076_D7D0);
    private static readonly GameAddress ManaPointGetter = new(0x0076_D7F0);

    /// <summary>The getters sit in a run of small functions; this is all the room there is.</summary>
    private const int GetterSize = 18;

    private const int Setter1Size = 48;
    private const int Setter2Size = 120;

    private const int SetterCaveSize = 256;
    private const int Setter1Offset = 0x10;
    private const int Setter2Offset = 0x40;

    /// <summary>
    /// Truncations in the getters' only callers, which narrow the wider value straight
    /// back down.
    /// </summary>
    private static readonly (string Name, GameAddress Site, byte[] Original, byte[] Replacement)[] GetterCallers =
    [
        // movsx ecx, ax → mov ecx, eax
        ("HP getter caller", new GameAddress(0x0076_C85E), [0x0F, 0xBF, 0xC8], [0x8B, 0xC8, 0x90]),
        // cwde → nop; eax is already the whole value
        ("MP getter caller", new GameAddress(0x0076_C8A9), [0x98], [0x90]),
    ];

    private readonly ILogger<HitPointExpansionPatch> _logger;

    public HitPointExpansionPatch(ILogger<HitPointExpansionPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "hp-mp-expansion";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.HpMpLimitEnabled;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var formats = WidenFormatStrings(context);
        var reads = RedirectPacketReads(context);
        var locals = WidenHandlerLocals(context);
        var characterSelect = WidenCharacterSelect(context);
        var globals = WidenGlobals(context);

        // The format strings are the one part nothing else can compensate for: with them
        // unpatched the parser reads two bytes where the server sent four and every
        // remaining field of the packet is off by two.
        if (formats == 0)
        {
            throw new GameProcessException(
                "None of the packet format strings could be widened; the client would misread every field after HP.");
        }

        _logger.LogInformation(
            "HP/MP widened to 32 bits: {Formats} formats, {Reads} packet reads, {Locals} handler locals, " +
            "{CharacterSelect} character-select sites, {Globals} global accesses",
            formats, reads, locals, characterSelect, globals);
    }

    // ---- phase 1: format strings ----------------------------------------------------------

    private int WidenFormatStrings(GamePatchContext context)
    {
        var applied = 0;

        foreach (var format in Formats)
        {
            var edit = CodeEdit.ReplaceAt(
                context, format.Address, $"{format.Name} format", format.Expected, format.Widened);

            if (edit.Effective)
            {
                applied++;
            }
            else
            {
                _logger.LogWarning("{Packet} format at {Address} did not match", format.Name, format.Address);
            }
        }

        return applied;
    }

    // ---- phase 2: the packet read helper -----------------------------------------------------

    private int RedirectPacketReads(GamePatchContext context)
    {
        var readDword = context.Process.AllocateExecutable(ReadDwordCode.Length);
        context.Process.WriteCode(readDword, ReadDwordCode);
        _logger.LogDebug("Dword packet reader at {Address}", readDword);

        var applied = 0;

        foreach (var (name, site) in ReadWordCallSites)
        {
            if (RedirectCall(context, site, ReadWord, readDword))
            {
                applied++;
            }
            else
            {
                _logger.LogWarning("{Name} at {Address} does not call the word reader", name, site);
            }
        }

        foreach (var (name, site) in SignExtensionsAfterRead)
        {
            var edit = CodeEdit.ReplaceAt(context, site, name, SignExtendAxToEdx, MoveEaxToEdx);
            applied += edit.Effective ? 1 : 0;
        }

        foreach (var (name, site, modrm, displacement) in StatusLocalReads)
        {
            var edit = CodeEdit.ReplaceAt(
                context, site, name,
                [0x0F, 0xB7, modrm, displacement],
                [0x8B, modrm, displacement, 0x90]);

            applied += edit.Effective ? 1 : 0;
        }

        return applied;
    }

    /// <summary>Repoints a <c>call</c> that currently goes to <paramref name="from"/>.</summary>
    private static bool RedirectCall(
        GamePatchContext context, GameAddress site, GameAddress from, GameAddress to)
    {
        var current = context.Process.ReadBytes(site, 5);

        if (current[0] != 0xE8)
        {
            return false;
        }

        var target = unchecked((uint)(site.Value + 5 + BitConverter.ToInt32(current, 1)));

        if (target == to.Value)
        {
            return true;
        }

        if (target != from.Value)
        {
            return false;
        }

        context.Image.Apply(context.Process, site, [0xE8, .. BitConverter.GetBytes(to - (site + 5))]);
        return true;
    }

    // ---- phase 2b: handler stack locals ---------------------------------------------------

    private int WidenHandlerLocals(GamePatchContext context)
    {
        var applied = 0;

        foreach (var handler in Handlers)
        {
            foreach (var (field, displacement) in ((string Field, byte Displacement)[])
                     [("max HP", handler.HitPointLocal), ("max MP", handler.ManaPointLocal)])
            {
                var count = WidenLocalAccesses(context, handler, displacement);
                applied += count;

                _logger.LogDebug("{Handler} {Field}: widened {Count} accesses", handler.Name, field, count);
            }
        }

        return applied;
    }

    private static int WidenLocalAccesses(GamePatchContext context, HandlerBody handler, byte displacement)
    {
        var length = handler.End - handler.Start;
        var body = new byte[length];

        if (!context.Image.TryRead(handler.Start, body))
        {
            context.Process.ReadBytes(handler.Start, body);
        }

        var applied = 0;

        for (var offset = 0; offset + 4 <= body.Length; offset++)
        {
            if (WidenLocalAccess(body.AsSpan(offset, 4), displacement) is not { } widened)
            {
                continue;
            }

            context.Image.Apply(context.Process, handler.Start + offset, widened);
            widened.CopyTo(body.AsSpan(offset));
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Rewrites one four-byte access to a stack local so it moves the whole value.
    /// </summary>
    /// <remarks>
    /// Every form here is four bytes before and after, so the instruction that follows
    /// stays where it was. The one byte freed by dropping the operand-size prefix or the
    /// two-byte opcode becomes a <c>nop</c>.
    /// </remarks>
    /// <returns>The replacement, or null if this is not a narrowing access to that local.</returns>
    internal static byte[]? WidenLocalAccess(ReadOnlySpan<byte> instruction, byte displacement)
    {
        if (instruction.Length < 4 || instruction[3] != displacement || !BasePointerModRm.Contains(instruction[2]))
        {
            return null;
        }

        var modrm = instruction[2];

        return (instruction[0], instruction[1]) switch
        {
            // movzx/movsx reg, word [ebp+disp] → mov reg, dword [ebp+disp]
            (0x0F, 0xB7) or (0x0F, 0xBF) => [0x8B, modrm, displacement, 0x90],

            // mov/cmp with the operand-size prefix → the same instruction on dwords
            (0x66, 0x89) or (0x66, 0x8B) or (0x66, 0x3B) or (0x66, 0x39) =>
                [instruction[1], modrm, displacement, 0x90],

            _ => null,
        };
    }

    // ---- phase 2c/2d: character select ---------------------------------------------------

    private int WidenCharacterSelect(GamePatchContext context)
    {
        var applied = 0;

        // The getter's first byte says whether this has run before: patched, it loads from
        // an absolute address; unpatched, it is an ordinary prologue.
        var getterHead = context.Process.Read<byte>(HitPointGetter);

        if (getterHead == 0xA1)
        {
            _logger.LogDebug("Character-select HP/MP is already widened");
            applied += 4;
        }
        else if (getterHead != 0x55)
        {
            _logger.LogWarning("HP getter at {Address} does not look like a function", HitPointGetter);
        }
        else
        {
            var cave = context.Process.AllocateExecutable(SetterCaveSize);
            var setter1 = cave + Setter1Offset;
            var setter2 = cave + Setter2Offset;

            context.Process.WriteCode(setter1, BuildSetter1());
            context.Process.WriteCode(setter2, BuildSetter2());

            // Only a jump goes at the original addresses. They are switch-case entries of
            // 14 and 17 bytes; writing the whole setter over them, as an earlier version of
            // the reference did, destroys the cases that follow.
            context.Image.Apply(context.Process, CharacterSetter1, InlineHook.BuildJump(CharacterSetter1, setter1));
            context.Image.Apply(context.Process, CharacterSetter2, InlineHook.BuildJump(CharacterSetter2, setter2));

            context.Image.Apply(context.Process, HitPointGetter, BuildGetter(MaxHitPoints));
            context.Image.Apply(context.Process, ManaPointGetter, BuildGetter(MaxManaPoints));

            _logger.LogDebug("Character-select setters at {Cave}: F1={Setter1} F2={Setter2}", cave, setter1, setter2);
            applied += 4;
        }

        foreach (var (name, site, original, replacement) in GetterCallers)
        {
            var edit = CodeEdit.ReplaceAt(context, site, name, original, replacement);
            applied += edit.Effective ? 1 : 0;
        }

        return applied;
    }

    // ---- phase 3/4: globals ------------------------------------------------------------------

    private int WidenGlobals(GamePatchContext context)
    {
        var applied = 0;

        foreach (var (name, global) in ((string Name, GameAddress Global)[])
                 [("max HP", MaxHitPoints), ("max MP", MaxManaPoints),
                  ("displayed HP", DisplayedHitPoints), ("displayed MP", DisplayedManaPoints)])
        {
            var count = WidenGlobalAccesses(context, global);
            applied += count;

            _logger.LogDebug("{Global}: widened {Count} accesses", name, count);
        }

        return applied;
    }

    /// <summary>
    /// Finds every narrowing access to a global and widens it.
    /// </summary>
    /// <remarks>
    /// Searched by looking for the address and then checking what precedes it, rather than
    /// by running twenty-five signatures — one per register and instruction form — over
    /// the whole image. The constraint is identical: the same seven bytes have to match
    /// either way.
    /// </remarks>
    private static int WidenGlobalAccesses(GamePatchContext context, GameAddress global)
    {
        var applied = 0;

        foreach (var hit in context.Image.FindAll(BytePattern.Exact(BitConverter.GetBytes(global.Value))))
        {
            // Restricted to code. The address also appears in the data sections as an
            // ordinary pointer, where the bytes in front of it are not an instruction.
            if (hit < CodeStart + 3 || hit >= CodeEnd)
            {
                continue;
            }

            // The operand can start two or three bytes into the instruction depending on
            // whether it carries a ModR/M byte.
            foreach (var prefixLength in (int[])[3, 2])
            {
                var start = hit - prefixLength;
                var instruction = new byte[prefixLength + sizeof(uint) + 1];

                if (!context.Image.TryRead(start, instruction))
                {
                    continue;
                }

                if (WidenGlobalAccess(instruction, global) is not { } widened)
                {
                    continue;
                }

                context.Image.Apply(context.Process, start, widened);
                applied++;
                break;
            }
        }

        return applied;
    }

    /// <summary>
    /// Rewrites one access to a 16-bit global so it moves the whole 32-bit value.
    /// </summary>
    /// <remarks>
    /// Reads are widened from <c>movzx</c>/<c>movsx</c>, which would otherwise clear or
    /// sign-extend over the high half. Writes lose their operand-size prefix. Both are one
    /// byte shorter than what they replace, and the remainder becomes a <c>nop</c> so the
    /// next instruction stays put.
    /// </remarks>
    /// <returns>The replacement, or null if this is not a narrowing access to that global.</returns>
    internal static byte[]? WidenGlobalAccess(ReadOnlySpan<byte> instruction, GameAddress global)
    {
        var address = BitConverter.GetBytes(global.Value);

        // mov word [address], ax — no ModR/M byte, so the operand starts at 2 and the
        // whole instruction is six bytes, not seven. Emitting a seventh would overwrite
        // the first byte of whatever follows.
        if (instruction.Length >= 6 && instruction[0] == 0x66 && instruction[1] == 0xA3 &&
            instruction[2..6].SequenceEqual(address))
        {
            return [0xA3, .. address, 0x90];
        }

        if (instruction.Length < 8 || !instruction[3..7].SequenceEqual(address) ||
            !DirectModRm.Contains(instruction[2]))
        {
            return null;
        }

        var modrm = instruction[2];

        return (instruction[0], instruction[1]) switch
        {
            // movzx/movsx reg, word [address] → mov reg, dword [address]
            (0x0F, 0xB7) or (0x0F, 0xBF) => [0x8B, modrm, .. address, 0x90],

            // mov word [address], reg → mov dword [address], reg
            (0x66, 0x89) => [0x89, modrm, .. address, 0x90],

            _ => null,
        };
    }

    // ---- code this installs --------------------------------------------------------------------

    /// <summary>
    /// The client's packet reader, widened to four bytes.
    /// </summary>
    /// <remarks>
    /// A copy of the original at <c>0x005239F0</c> with three changes: it moves a dword
    /// instead of a word, advances the packet cursor by four instead of two, and returns
    /// the whole register. Kept the same length so the differences are visible.
    /// </remarks>
    internal static ReadOnlySpan<byte> ReadDwordCode =>
    [
        0x55, 0x8B, 0xEC, 0x51,        // push ebp; mov ebp,esp; push ecx
        0x8B, 0x45, 0x08, 0x8B, 0x08,  // mov eax,[ebp+8]; mov ecx,[eax]
        0x8B, 0x11, 0x90,              // mov edx,[ecx]            (was: mov dx,[ecx])
        0x89, 0x55, 0xFC, 0x90,        // mov [ebp-4],edx          (was: mov [ebp-4],dx)
        0x8B, 0x45, 0x08, 0x8B, 0x08,  // mov eax,[ebp+8]; mov ecx,[eax]
        0x83, 0xC1, 0x04,              // add ecx,4                (was: add ecx,2)
        0x8B, 0x55, 0x08, 0x89, 0x0A,  // mov edx,[ebp+8]; mov [edx],ecx
        0x8B, 0x45, 0xFC, 0x90,        // mov eax,[ebp-4]          (was: mov ax,[ebp-4])
        0x8B, 0xE5, 0x5D, 0xC3,        // mov esp,ebp; pop ebp; ret
    ];

    /// <summary>The three-argument character-select setter, storing HP and MP in full.</summary>
    /// <remarks>
    /// <c>__thiscall</c> reached through a jump, so the arguments are still where the
    /// caller left them relative to <c>esp</c> and <c>this</c> is still in <c>ecx</c>.
    /// </remarks>
    internal static byte[] BuildSetter1()
    {
        List<byte> code =
        [
            0x8B, 0xC1,                                  // mov eax, ecx           (this)
            0x8B, 0x54, 0x24, 0x04,                      // mov edx, [esp+4]       (max HP)
            0x89, 0x15, .. Address(MaxHitPoints),        // mov [maxHp], edx
            0x8B, 0x54, 0x24, 0x08,                      // mov edx, [esp+8]       (max MP)
            0x89, 0x15, .. Address(MaxManaPoints),       // mov [maxMp], edx
            0x66, 0x8B, 0x54, 0x24, 0x0C,                // mov dx, [esp+0xC]      (class)
            0x66, 0x89, 0x50, 0x0E,                      // mov [eax+0xE], dx
            0xC2, 0x0C, 0x00,                            // ret 0xC
        ];

        return Pad(code, Setter1Size);
    }

    /// <summary>The eleven-argument setter, which also carries the elemental resistances.</summary>
    internal static byte[] BuildSetter2()
    {
        List<byte> code =
        [
            0x55, 0x8B, 0xEC, 0x51,                      // push ebp; mov ebp,esp; push ecx
            0x89, 0x4D, 0xFC,                            // mov [ebp-4], ecx
            0x8B, 0x45, 0xFC,                            // mov eax, [ebp-4]       (this)

            0x8A, 0x4D, 0x08, 0x88, 0x48, 0x10,          // byte arg 1 → [eax+0x10]
            0x8A, 0x4D, 0x0C, 0x88, 0x48, 0x11,          // byte arg 2 → [eax+0x11]

            0x8B, 0x4D, 0x10,                            // mov ecx, [ebp+0x10]    (max HP)
            0x89, 0x0D, .. Address(MaxHitPoints),
            0x8B, 0x4D, 0x14,                            // mov ecx, [ebp+0x14]    (max MP)
            0x89, 0x0D, .. Address(MaxManaPoints),

            0x66, 0x8B, 0x4D, 0x18,                      // mov cx, [ebp+0x18]     (class)
            0x66, 0x89, 0x48, 0x0E,                      // mov [eax+0xE], cx
        ];

        foreach (var (argument, field) in ((byte Argument, byte Field)[])
                 [(0x1C, 0x14), (0x20, 0x18), (0x24, 0x1C), (0x28, 0x20), (0x2C, 0x24), (0x30, 0x28)])
        {
            code.AddRange([0x0F, 0xBE, 0x4D, argument]); // movsx ecx, byte [ebp+arg]
            code.AddRange([0x89, 0x48, field]);          // mov [eax+field], ecx
        }

        code.AddRange([0x8B, 0xE5, 0x5D, 0xC2, 0x2C, 0x00]); // mov esp,ebp; pop ebp; ret 0x2C
        return Pad(code, Setter2Size);
    }

    /// <summary>A getter that returns a global outright, replacing one that truncated it.</summary>
    internal static byte[] BuildGetter(GameAddress global)
    {
        List<byte> code = [0xA1, .. Address(global), 0xC3]; // mov eax, [global]; ret

        // Padded with nop rather than int3: this overwrites a function in the middle of a
        // run of them, and the bytes after the ret are still reachable by a bad jump.
        while (code.Count < GetterSize)
        {
            code.Add(0x90);
        }

        return [.. code];
    }

    private static byte[] Address(GameAddress address) => BitConverter.GetBytes(address.Value);

    /// <summary>
    /// Pads to the slot size with <c>int3</c>, which faults immediately if anything ever
    /// runs off the end of the block.
    /// </summary>
    private static byte[] Pad(List<byte> code, int size)
    {
        if (code.Count > size)
        {
            throw new InvalidOperationException($"Generated {code.Count} bytes for a {size}-byte slot.");
        }

        while (code.Count < size)
        {
            code.Add(0xCC);
        }

        return [.. code];
    }
}
