using Login38.Aux.Toggles;
using Login38.Interop;

namespace Login38.Aux.Notifications;

/// <summary>
/// Catches the two packets the client receives and does not draw.
/// </summary>
/// <remarks>
/// <para>
/// The client's packet dispatcher range-checks the opcode and sends everything above 183 to
/// a do-nothing epilogue with one <c>ja</c>. That branch is the hook: its four-byte
/// displacement is pointed at a cave instead, which looks at the opcode, keeps a copy of
/// the two worth keeping, and then jumps on to where the branch was going. Opcodes at or
/// below 183 never reach it at all, so the client's real handlers are untouched.
/// </para>
/// <para>
/// The copy goes into a ring in the cave rather than into a call back to the launcher.
/// These are two processes: a <c>call</c> to a launcher address from inside the game lands
/// on whatever the game happens to have at that address.
/// </para>
/// <para>
/// The reference's shellcode carries three counters and a sixteen-entry opcode ring, which
/// is how the opcodes above were found in the first place — the module's own history
/// records two wrong guesses before them. They are diagnostics rather than the feature, and
/// they run before the registers are saved, so every opcode above 183 reaches the client's
/// epilogue with <c>eax</c> and <c>ecx</c> holding something else.
/// </para>
/// </remarks>
internal static class NotificationHookCave
{
    /// <summary>How many packets the ring holds before the oldest is lost.</summary>
    internal const int RingLength = 16;

    /// <summary>And how big each slot is.</summary>
    internal const int SlotSize = 80;

    /// <summary>How far into the cave the code goes.</summary>
    internal const uint CodeOffset = 0;

    /// <summary>How much is reserved for it.</summary>
    internal const int CodeSize = 0x100;

    /// <summary>Where the count of packets kept sits.</summary>
    internal const uint TailOffset = 0x100;

    /// <summary>And where the ring starts.</summary>
    internal const uint RingOffset = 0x200;

    /// <summary>How much is asked for.</summary>
    internal const int CaveSize = 0x800;

    /// <summary>Where in a slot the payload starts.</summary>
    internal const int PayloadOffset = 4;

    /// <summary>Where the sequence number of the write sits, at the end.</summary>
    internal const int SequenceOffset = SlotSize - 4;

    /// <summary>How much of the payload is kept.</summary>
    /// <remarks>
    /// The longest of the two is a pickup: two bytes of sprite id, sixty-four of name and a
    /// terminator. Anything past that is a packet this does not show.
    /// </remarks>
    internal const int PayloadBytes = SequenceOffset - PayloadOffset;

    /// <summary>Where the dispatcher keeps the opcode it is handling.</summary>
    private const int OpcodeLocal = -0x60F4;

    /// <summary>And the packet, already advanced past the opcode.</summary>
    private const byte PacketArgument = 8;

    /// <summary>Where the branch this hook takes over was going.</summary>
    private static readonly GameAddress Epilogue = new(0x00541596);

    /// <summary>
    /// The <c>ja</c> that sends every unhandled opcode to the epilogue.
    /// </summary>
    /// <remarks>
    /// Six bytes, and the detour keeps all six: the condition stays exactly as the client
    /// wrote it and only the displacement moves. That is what makes this invisible to every
    /// opcode the client does handle — they never take the branch.
    /// </remarks>
    internal static HookSite Site => new(
        new GameAddress(0x0053938E),
        [0x0F, 0x87, 0x02, 0x82, 0x00, 0x00],                 // ja 0x00541596
        new GameAddress(0x00539394));

    /// <summary>Points the branch at the cave instead of the epilogue.</summary>
    internal static byte[] Redirect(GameAddress cave)
    {
        var patch = new byte[Site.Length];

        Site.Stock.AsSpan(0, 2).CopyTo(patch);
        BitConverter.TryWriteBytes(patch.AsSpan(2), cave.Value - (Site.Address.Value + 6));

        return patch;
    }

    /// <summary>Lays out an empty cave.</summary>
    internal static byte[] Empty() => new byte[CaveSize];

    private const byte Eax = 0;
    private const byte Ecx = 1;
    private const byte Ebx = 3;
    private const byte Ebp = 5;
    private const byte Esi = 6;
    private const byte Edi = 7;

    /// <summary>
    /// Builds the recorder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is saved before anything is looked at, so an opcode this does not want
    /// reaches the client's epilogue with exactly the registers and flags the branch left.
    /// </para>
    /// <para>
    /// The slot is claimed with a locked exchange-and-add, and the sequence number of the
    /// claim is written into it last. The reference claims with a plain unlocked increment
    /// after the write — two of the client's threads receiving at once write over each
    /// other — and marks the slot with a constant, which a slot from a previous lap of the
    /// ring carries too.
    /// </para>
    /// </remarks>
    internal static byte[] Build(GameAddress cave)
    {
        var code = new ShellcodeBuilder(cave + CodeOffset);

        code.PushAd().PushFd().Cld();

        CmpLocal(code, OpcodeLocal, PacketBox.ItemBoard);
        var keep = code.NearJump(Jz);
        CmpLocal(code, OpcodeLocal, PacketBox.ShowDrop);
        var ignore = code.NearJump(Jnz);

        code.MarkLabel(keep)
            .MovEax(1)
            .Bytes([0xF0, 0x0F, 0xC1, 0x05]).Dword((cave + TailOffset).Value)   // lock xadd
            .Bytes([0x89, ModRm(3, Eax, Ebx)])                    // mov ebx, eax   the sequence
            .Bytes([0x83, ModRm(3, 4, Eax), RingLength - 1])      // and eax, 0x0F
            .Bytes([0x6B, ModRm(3, Eax, Eax), SlotSize])          // imul eax, eax, 80
            .Byte(0x05).Dword((cave + RingOffset).Value);         // add eax, ring

        // The opcode itself, which is what says how to read the rest.
        code.Bytes([0x8B, ModRm(2, Ecx, Ebp)]).Dword(unchecked((uint)OpcodeLocal))
            .Bytes([0x88, ModRm(0, Ecx, Eax)]);                   // mov [eax], cl

        // The client has already dereferenced this, so it is all but certainly good — but
        // "all but" inside somebody else's network code is a fault the player sees.
        code.Bytes([0x8B, ModRm(1, Esi, Ebp), PacketArgument])    // mov esi, [ebp+8]
            .Bytes([0x85, ModRm(3, Esi, Esi)]);                   // test esi, esi
        var noPacket = code.ShortJumpIfZero();

        code.Bytes([0x8D, ModRm(1, Edi, Eax), PayloadOffset])     // lea edi, [eax+4]
            .Bytes([0xB9]).Dword(PayloadBytes)                    // mov ecx, 72
            .RepMovsb()
            .Bytes([0x89, ModRm(1, Ebx, Eax), SequenceOffset]);   // mov [eax+76], ebx

        // A slot claimed and not filled in keeps whatever sequence it had, which is not the
        // one the reader is expecting — so it is passed over rather than read as a packet.
        code.MarkLabel(noPacket).MarkLabel(ignore)
            .PopFd()
            .PopAd()
            .JumpTo(Epilogue);

        return code.Build();
    }

    private const byte Jz = 0x84;
    private const byte Jnz = 0x85;

    /// <summary><c>cmp dword ptr [ebp+displacement], imm32</c>.</summary>
    private static void CmpLocal(ShellcodeBuilder code, int displacement, uint value) =>
        code.Bytes([0x81, ModRm(2, 7, Ebp)]).Dword(unchecked((uint)displacement)).Dword(value);

    /// <summary>A mod/reg/rm byte.</summary>
    private static byte ModRm(byte mode, byte register, byte memory) =>
        (byte)((mode << 6) | (register << 3) | memory);
}
