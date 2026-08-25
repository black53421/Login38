using System.Buffers.Binary;

namespace Login38.Interop;

/// <summary>
/// The five-byte relative jump used to divert game code into a code cave.
/// </summary>
/// <remarks>
/// Every hook in this launcher works the same way: allocate a cave, assemble a
/// trampoline that does the extra work, replays the bytes overwritten at the hook site
/// and jumps back, then overwrite the hook site with <c>jmp cave</c>.
/// </remarks>
public static class InlineHook
{
    /// <summary>The <c>jmp rel32</c> opcode, which is what an installed hook looks like.</summary>
    public const byte JumpOpcode = 0xE9;

    private const byte JmpRel32 = JumpOpcode;

    /// <summary>Size of the <c>jmp rel32</c> the hook site is overwritten with.</summary>
    public const int JumpSize = 5;

    private const byte Nop = 0x90;

    /// <summary>Encodes <c>jmp rel32</c> from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static byte[] BuildJump(GameAddress from, GameAddress to) => BuildJump(from, to, JumpSize);

    /// <summary>
    /// Encodes <c>jmp rel32</c> padded with NOPs to <paramref name="totalSize"/>.
    /// </summary>
    /// <remarks>
    /// A hook site usually straddles more than five bytes, because instructions do not
    /// divide evenly. Padding the remainder leaves the tail of the replaced instruction
    /// as valid NOPs instead of a fragment that decodes as something arbitrary — which
    /// matters if anything else ever disassembles or jumps near the site.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Smaller than a <c>jmp rel32</c>.</exception>
    public static byte[] BuildJump(GameAddress from, GameAddress to, int totalSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalSize, JumpSize);

        var jump = new byte[totalSize];
        jump.AsSpan(JumpSize).Fill(Nop);
        jump[0] = JmpRel32;

        // rel32 is measured from the end of the jump instruction, not from the end of
        // the padded region.
        BinaryPrimitives.WriteUInt32LittleEndian(
            jump.AsSpan(1), unchecked((uint)(to - (from + JumpSize))));

        return jump;
    }

    /// <summary>
    /// Whether a hook is already installed at <paramref name="site"/>.
    /// </summary>
    /// <remarks>
    /// Checked before installing, because these patches run against a live process that
    /// may already have been patched — by a previous launcher run against the same
    /// game instance, for example. Installing twice would capture the first hook's jump
    /// as "original bytes" and produce an infinite loop.
    /// </remarks>
    public static bool IsInstalledAt(RemoteProcess process, GameAddress site)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.TryRead<byte>(site, out var first) && first == JmpRel32;
    }

    /// <summary>
    /// Writes the jump at <paramref name="site"/> with every thread of the process
    /// suspended.
    /// </summary>
    /// <remarks>
    /// The suspension is what makes this safe: without it a thread can be executing
    /// inside the five bytes being replaced, and resumes into the middle of a jump.
    /// </remarks>
    public static void InstallJump(RemoteProcess process, GameAddress site, GameAddress detour)
    {
        ArgumentNullException.ThrowIfNull(process);

        var jump = BuildJump(site, detour);

        using var suspended = process.SuspendThreads();
        process.WriteCode(site, jump);
    }
}
