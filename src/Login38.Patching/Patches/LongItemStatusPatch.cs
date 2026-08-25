using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Teaches the client a packet that can carry an item description longer than 255 bytes.
/// </summary>
/// <remarks>
/// <para>
/// The item status packet declares its description with a one-byte length, so 255 is the
/// most the wire format can express. Widening that field in the existing packet is not an
/// option — every server and every client would have to change on the same day, and one
/// that did not would read the rest of the stream at the wrong offset.
/// </para>
/// <para>
/// So the existing handler is left exactly as it is, and a second one is added under an
/// opcode the client currently refuses. A server that has a long description to send uses
/// the new opcode; everything else keeps working unchanged, including against a client
/// without this patch.
/// </para>
/// <para>
/// The new handler is the old one, copied out of the running process and altered in three
/// places: the field descriptor gains a two-byte length, the 256-byte stack buffer becomes
/// a 64K one on the heap, and the four instructions that load the length are widened from
/// byte to word. The tail jumps back into the original, which from that point on reads only
/// the fields both versions share — so the rendering, the item store and everything
/// downstream is the client's own code, not a reimplementation of it.
/// </para>
/// <para>
/// Copied at runtime rather than assembled here because the original is encrypted on disk
/// and decrypted only when the player first looks at an item. There is no version of these
/// bytes to hard-code.
/// </para>
/// </remarks>
public sealed class LongItemStatusPatch : IGamePatch
{
    /// <summary>The packet dispatcher, entered once per packet with the packet in hand.</summary>
    private static readonly GameAddress Dispatcher = new(0x0054_4A20);

    /// <summary><c>push ebp; mov ebp, esp; sub esp, 0x1C</c>.</summary>
    private static ReadOnlySpan<byte> DispatcherPrologue => [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x1C];

    /// <summary>Where the dispatcher carries on, once the router has replayed its prologue.</summary>
    private static readonly GameAddress DispatcherContinuation = new(0x0054_4A26);

    /// <summary>
    /// The opcode the long form is sent under.
    /// </summary>
    /// <remarks>
    /// Chosen because the client's opcode table maps it to the bail handler — nothing is
    /// displaced by taking it, and an unpatched client quietly refuses the packet rather
    /// than misreading it.
    /// </remarks>
    private const byte LongStatusOpcode = 0xF2;

    /// <summary>
    /// Also accepted, in the reference's own words to stop 241 and 242 from disagreeing.
    /// </summary>
    /// <remarks>
    /// Kept for parity, and it is the one part of this patch worth being uneasy about: 241
    /// is not a free opcode — the client has a real handler for it. Routing it here takes
    /// that handler out of service, and a server that sends 241 for its own reasons will
    /// have those packets read as item status. Preserved rather than dropped because packet
    /// numbering is a contract with a server this port cannot see; changing it unilaterally
    /// is the more expensive mistake.
    /// </remarks>
    private const byte LongStatusOpcodeAlternate = 0xF1;

    /// <summary>The original item status handler, which is what gets copied.</summary>
    private static readonly GameAddress StatusHandler = new(0x0052_8A00);

    /// <summary>
    /// How much of the handler is copied.
    /// </summary>
    /// <remarks>
    /// Up to the point where the parsed fields are handed on. Everything before this reads
    /// the packet; everything after it uses the results, and uses no length.
    /// </remarks>
    private const int CopyLength = 0x118;

    /// <inheritdoc cref="CopyLength"/>
    private static readonly GameAddress StatusDownstream = new(0x0052_8B18);

    /// <summary><c>push imm32</c> of the field descriptor.</summary>
    private const int FormatPush = 0x2E;

    /// <inheritdoc cref="FormatPush"/>
    private const int FormatImmediate = 0x2F;

    /// <summary><c>lea ecx, [ebp-0x420]</c> — the 256-byte buffer on the stack.</summary>
    private const int BufferLea = 0xB8;

    /// <summary>The four <c>movzx r32, byte</c> reads of the length.</summary>
    private static ReadOnlySpan<int> LengthReads => [0xAE, 0xC2, 0xDA, 0xF7];

    /// <summary>Every <c>call rel32</c> in the copied span.</summary>
    /// <remarks>
    /// Relative, so all of them are wrong the moment the code moves. Listed rather than
    /// found by scanning: an <c>E8</c> byte inside an immediate would disassemble as a call
    /// to a scan and be relocated into nonsense.
    /// </remarks>
    private static ReadOnlySpan<int> Calls => [0x3A, 0x5F, 0x72, 0x88, 0x9E, 0xD1, 0xEE, 0x113];

    /// <summary>
    /// The field descriptor the copy uses: <c>h</c> where the original has <c>c</c>.
    /// </summary>
    /// <remarks>
    /// The parser reads its fields from this string, one letter each. <c>c</c> is a byte
    /// count and <c>h</c> a two-byte one — the whole widening of the wire format, in one
    /// character.
    /// </remarks>
    private static ReadOnlySpan<byte> LongFormat => "dsdh\0"u8;

    /// <summary>Room for the longest description a two-byte length can describe.</summary>
    private const int BufferSize = 0x1_0000;

    private const int HandlerCaveSize = 0x140;

    private const int RouterCaveSize = 0x40;

    private readonly ILogger<LongItemStatusPatch> _logger;

    public LongItemStatusPatch(ILogger<LongItemStatusPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "long-item-status";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;

        var format = process.AllocateExecutable(LongFormat.Length);
        process.WriteCode(format, LongFormat);

        var buffer = process.AllocateExecutable(BufferSize);
        var handler = process.AllocateExecutable(HandlerCaveSize);
        var router = process.AllocateExecutable(RouterCaveSize);

        _logger.LogInformation(
            "Long item status prepared: handler at {Handler}, router at {Router}, {Size} byte buffer at {Buffer}",
            handler, router, BufferSize, buffer);

        // Not awaited. The handler this copies is decrypted the first time the player looks
        // at any item, which may be long after the launch is otherwise finished.
        _ = Task.Run(
            () => WatchAsync(process, format, buffer, handler, router, cancellationToken),
            CancellationToken.None);
    }

    private async Task WatchAsync(
        RemoteProcess process,
        GameAddress format,
        GameAddress buffer,
        GameAddress handler,
        GameAddress router,
        CancellationToken cancellationToken)
    {
        try
        {
            await DeferredSites.WatchAsync(
                process, [Dispatcher],
                _ => Install(process, format, buffer, handler, router),
                "Long item status", _logger,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The launcher is closing.
        }
        catch (GameProcessException e)
        {
            // Reached only when a write fails, which is a real fault rather than code that
            // has not been decrypted yet — so it stops rather than retrying for ten minutes.
            _logger.LogWarning(e, "Long item status was not installed");
        }
    }

    /// <summary>
    /// Copies the handler, builds the router and wires the dispatcher — in that order.
    /// </summary>
    /// <remarks>
    /// The dispatcher is redirected last and only if everything before it succeeded. Doing
    /// it first would leave a window where packets are routed to a cave that is not yet a
    /// handler, which is not a failed patch but a crash.
    /// </remarks>
    private SiteAttempt Install(
        RemoteProcess process, GameAddress format, GameAddress buffer, GameAddress handler, GameAddress router)
    {
        Span<byte> prologue = stackalloc byte[DispatcherPrologue.Length];

        if (!process.TryReadBytes(Dispatcher, prologue))
        {
            return SiteAttempt.NotReady;
        }

        if (prologue[0] == InlineHook.JumpOpcode)
        {
            return SiteAttempt.Settled;
        }

        // Both have to be decrypted: one to copy from, one to hook.
        if (!prologue.SequenceEqual(DispatcherPrologue))
        {
            return SiteAttempt.NotReady;
        }

        Span<byte> source = stackalloc byte[CopyLength];

        if (!process.TryReadBytes(StatusHandler, source) || !IsDecrypted(source))
        {
            return SiteAttempt.NotReady;
        }

        process.WriteCode(handler, BuildHandler(source, handler, format, buffer));
        process.WriteCode(router, BuildRouter(router, handler));
        process.WriteCode(Dispatcher, InlineHook.BuildJump(Dispatcher, router, DispatcherPrologue.Length));

        _logger.LogInformation("Long item descriptions are now accepted on opcode {Opcode}", LongStatusOpcode);
        return SiteAttempt.Settled;
    }

    /// <summary>
    /// Whether the copied span is the handler rather than whatever it is while encrypted.
    /// </summary>
    /// <remarks>
    /// Every byte this patch is about to change is checked to be what it is expected to be.
    /// The alternative is transforming ciphertext into a cave and jumping to it.
    /// </remarks>
    internal static bool IsDecrypted(ReadOnlySpan<byte> source)
    {
        const byte PushImm32 = 0x68;
        const byte LeaOpcode = 0x8D;
        const byte MovzxByte = 0xB6;
        const byte CallRel32 = 0xE8;

        if (source.Length < CopyLength
            || source[0] != 0x55
            || source[FormatPush] != PushImm32
            || source[BufferLea] != LeaOpcode)
        {
            return false;
        }

        foreach (var offset in LengthReads)
        {
            if (source[offset] != MovzxByte)
            {
                return false;
            }
        }

        foreach (var offset in Calls)
        {
            if (source[offset] != CallRel32)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Turns a copy of the original handler into one that reads a two-byte length.
    /// </summary>
    /// <param name="source">The live bytes, already checked by <see cref="IsDecrypted"/>.</param>
    /// <param name="cave">Where the result will be written, which the relocation needs.</param>
    /// <param name="format">The <c>dsdh</c> descriptor.</param>
    /// <param name="buffer">The heap buffer that replaces the one on the stack.</param>
    internal static byte[] BuildHandler(
        ReadOnlySpan<byte> source, GameAddress cave, GameAddress format, GameAddress buffer)
    {
        var code = new byte[CopyLength];
        source[..CopyLength].CopyTo(code);

        BitConverter.GetBytes(format.Value).CopyTo(code, FormatImmediate);

        // lea ecx, [ebp-0x420] → mov ecx, buffer. Six bytes become five, so the sixth is
        // left as a nop rather than shifting everything after it — which would invalidate
        // every offset in this method and every relative jump in the copy.
        code[BufferLea] = MovEcxImm32;
        BitConverter.GetBytes(buffer.Value).CopyTo(code, BufferLea + 1);
        code[BufferLea + 5] = Nop;

        // Moving the buffer off the stack also settles what the wider length field would
        // otherwise do: its high byte lands where the original buffer began.
        foreach (var offset in LengthReads)
        {
            code[offset] = MovzxWord;
        }

        // Every call was relative to where it used to be.
        var delta = unchecked((int)(StatusHandler.Value - cave.Value));

        foreach (var offset in Calls)
        {
            var displacement = BitConverter.ToInt32(code, offset + 1);
            BitConverter.GetBytes(displacement + delta).CopyTo(code, offset + 1);
        }

        // Back into the original, past everything that reads the packet.
        return [.. code, .. InlineHook.BuildJump(cave + CopyLength, StatusDownstream)];
    }

    /// <summary>
    /// The router the dispatcher's entry point is redirected to.
    /// </summary>
    /// <remarks>
    /// It sees the packet before the dispatcher's opcode table does, so it can claim an
    /// opcode without touching the table. Anything it does not claim gets the prologue it
    /// displaced and a jump back to the instruction after it, which is indistinguishable
    /// from never having been here.
    /// </remarks>
    internal static byte[] BuildRouter(GameAddress cave, GameAddress handler)
    {
        var code = new ShellcodeBuilder(cave);

        code.Bytes([0x8B, 0x44, 0x24, 0x04])                 // mov eax, [esp+4] — the packet
            .Bytes([0x8A, 0x08]);                            // mov cl, [eax] — its opcode

        code.Bytes([0x80, 0xF9, LongStatusOpcodeAlternate]); // cmp cl, 0xF1
        var claimed = code.ShortJumpIfZero();

        code.Bytes([0x80, 0xF9, LongStatusOpcode]);          // cmp cl, 0xF2
        var notOurs = code.ShortJumpIfNotEqual();

        code.MarkLabel(claimed)
            .Byte(0x50)                                      // push eax
            .CallTo(handler)
            .Bytes([0x83, 0xC4, 0x04, 0xC3]);                // add esp, 4; ret

        return code.MarkLabel(notOurs)
                   .Bytes(DispatcherPrologue)
                   .JumpTo(DispatcherContinuation)
                   .Build();
    }

    private const byte MovEcxImm32 = 0xB9;
    private const byte MovzxWord = 0xB7;
    private const byte Nop = 0x90;
}
