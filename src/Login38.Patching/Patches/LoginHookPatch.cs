using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Captures the typed account and password, and sends the login packet in the shape
/// the server emulators expect.
/// </summary>
/// <remarks>
/// <para>
/// The client's own login path sends a packet shape that most emulators do not accept.
/// The widely used legacy <c>Login.dll</c> instead hooks the native login point and
/// sends opcode <c>0x77</c> with the compact <c>"cssddddddd"</c> argument list. This
/// reproduces that, which is what makes the launcher work against those servers.
/// </para>
/// <para>Three hooks, all pointing into one code cave:</para>
/// <list type="bullet">
/// <item>
/// <b>Account.</b> The client builds the typed account in a stack buffer; the hook
/// copies 32 bytes of it into the cave as each keystroke lands.
/// </item>
/// <item>
/// <b>Password.</b> The client decodes the password one byte at a time through a
/// converter routine. The hook appends each decoded byte to a buffer in the cave,
/// clearing the buffer when the write position is zero so a re-typed password does not
/// append to the previous one.
/// </item>
/// <item>
/// <b>Send.</b> Replaces the client's own packet construction with a direct call to
/// <c>SendPacketData</c> carrying the captured strings, then resets the password write
/// position for the next attempt.
/// </item>
/// </list>
/// <para>
/// The account and password sit in plain text inside the game's own address space for
/// the moment between capture and send. That is inherent to intercepting them at all,
/// and matches what the client already does with the same data.
/// </para>
/// </remarks>
public sealed class LoginHookPatch : IGamePatch
{
    // ---- hook sites -----------------------------------------------------------------

    /// <summary>Where the client has just built the typed account on the stack.</summary>
    private static readonly GameAddress AccountHookSite = new(0x0077_317D);

    private static readonly GameAddress AccountReturnSite = new(0x0077_3183);

    /// <summary>Where the client has just decoded one password byte.</summary>
    private static readonly GameAddress PasswordHookSite = new(0x004A_A38E);

    private static readonly GameAddress PasswordReturnSite = new(0x004A_A395);

    /// <summary>The client's native login point.</summary>
    private static readonly GameAddress SendHookSite = new(0x0077_2E07);

    /// <summary>
    /// Well past the stolen bytes: the replacement skips the client's own packet
    /// construction entirely rather than resuming after it.
    /// </summary>
    private static readonly GameAddress SendReturnSite = new(0x0077_2E77);

    /// <summary>Turns one keystroke into a password byte.</summary>
    private static readonly GameAddress PasswordByteConverter = new(0x0040_2800);

    private static readonly GameAddress SendPacketData = new(0x0058_0E50);

    // ---- cave layout ----------------------------------------------------------------

    private const int CaveSize = 1024;

    private const int AccountBufferOffset = 0x000;
    private const int PasswordBufferOffset = 0x080;
    private const int PasswordPositionOffset = 0x100;
    private const int FormatStringOffset = 0x110;
    private const int AccountCodeOffset = 0x120;
    private const int PasswordCodeOffset = 0x180;
    private const int SendCodeOffset = 0x280;

    // Space available to each code block, derived from the layout rather than picked by
    // hand: a block that overflows would silently overwrite the next one's first
    // instruction, which is close to impossible to diagnose from a crash.
    private const int AccountCodeCapacity = PasswordCodeOffset - AccountCodeOffset;
    private const int PasswordCodeCapacity = SendCodeOffset - PasswordCodeOffset;
    private const int SendCodeCapacity = CaveSize - SendCodeOffset;

    /// <summary>Bytes of the account buffer copied out on each keystroke.</summary>
    private const uint AccountBufferLength = 32;

    /// <summary>
    /// The <c>SendPacketData</c> argument descriptor: a char, two strings, then seven
    /// dwords.
    /// </summary>
    private static ReadOnlySpan<byte> FormatString => "cssddddddd\0"u8;

    private const uint LoginOpcode = 0x77;

    /// <summary>127.0.0.1 in network byte order, as the packet expects.</summary>
    private const uint LoopbackAddress = 0x0100_007F;

    /// <summary>Trailing constant the legacy client sends; meaning not established.</summary>
    private const uint TrailingFlag = 0x1F;

    private const int SendArgumentCount = 11;

    private readonly ILogger<LoginHookPatch> _logger;

    public LoginHookPatch(ILogger<LoginHookPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "login-hooks";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var process = context.Process;

        if (InlineHook.IsInstalledAt(process, SendHookSite))
        {
            _logger.LogDebug("Login hooks are already installed; leaving them alone");
            return;
        }

        // Executable, never freed: the game jumps into this for the rest of the session.
        var cave = process.AllocateExecutable(CaveSize);
        _logger.LogDebug("Login code cave at {Cave}", cave);

        var accountCode = BuildAccountCapture(cave);
        var passwordCode = BuildPasswordCapture(cave);
        var sendCode = BuildSendPacket(cave);

        // Guard the layout rather than silently writing one block over the next.
        EnsureFits(accountCode, AccountCodeCapacity, "account capture");
        EnsureFits(passwordCode, PasswordCodeCapacity, "password capture");
        EnsureFits(sendCode, SendCodeCapacity, "send packet");

        var caveData = new byte[CaveSize];
        FormatString.CopyTo(caveData.AsSpan(FormatStringOffset));
        accountCode.CopyTo(caveData.AsSpan(AccountCodeOffset));
        passwordCode.CopyTo(caveData.AsSpan(PasswordCodeOffset));
        sendCode.CopyTo(caveData.AsSpan(SendCodeOffset));
        process.WriteCode(cave, caveData);

        // All three jumps go in under a single suspension. Installing them one at a
        // time would leave the client briefly capturing an account with no matching
        // send hook, which sends a malformed packet.
        using (process.SuspendThreads())
        {
            process.WriteCode(
                AccountHookSite,
                InlineHook.BuildJump(AccountHookSite, cave + AccountCodeOffset, StolenBytes(AccountHookSite, AccountReturnSite)));

            process.WriteCode(
                PasswordHookSite,
                InlineHook.BuildJump(PasswordHookSite, cave + PasswordCodeOffset, StolenBytes(PasswordHookSite, PasswordReturnSite)));

            process.WriteCode(
                SendHookSite,
                InlineHook.BuildJump(SendHookSite, cave + SendCodeOffset, SendHookSize));
        }

        _logger.LogInformation(
            "Login hooks installed: account {Account}, password {Password}, send {Send}",
            cave + AccountCodeOffset, cave + PasswordCodeOffset, cave + SendCodeOffset);
    }

    /// <summary>
    /// Ten bytes, not the full distance to the return site: the send hook overwrites
    /// only the entry sequence and jumps past the client's packet construction.
    /// </summary>
    private const int SendHookSize = 10;

    private static int StolenBytes(GameAddress site, GameAddress returnSite) => returnSite - site;

    /// <summary>
    /// Copies the account out of the client's stack buffer.
    /// </summary>
    /// <remarks>
    /// Replays the <c>lea eax, [ebp-0x98]</c> it overwrote, then uses that pointer as
    /// the copy source. <c>pushad</c>/<c>popad</c> around the copy so the client sees
    /// no register change.
    /// </remarks>
    internal static byte[] BuildAccountCapture(GameAddress cave)
    {
        var code = new ShellcodeBuilder(cave + AccountCodeOffset);

        // Stolen: lea eax, [ebp-0x98] — eax now points at the typed account.
        code.Bytes([0x8D, 0x85, 0x68, 0xFF, 0xFF, 0xFF]);

        code.PushAd()
            .Bytes([0x8B, 0xF0])                              // mov esi, eax
            .MovEdi((cave + AccountBufferOffset).Value)
            .MovEcx(AccountBufferLength)
            .Cld()
            .RepMovsb()
            .PopAd()
            .JumpTo(AccountReturnSite);

        return code.Build();
    }

    /// <summary>
    /// Appends each decoded password byte to the cave buffer.
    /// </summary>
    /// <remarks>
    /// The converter is called for its return value in <c>al</c>. The buffer is cleared
    /// when the write position is zero, which is what makes a second login attempt
    /// start clean instead of appending to the first.
    /// </remarks>
    internal static byte[] BuildPasswordCapture(GameAddress cave)
    {
        var passwordBuffer = cave + PasswordBufferOffset;
        var passwordPosition = cave + PasswordPositionOffset;
        var code = new ShellcodeBuilder(cave + PasswordCodeOffset);

        // Stolen: mov edx, [ebp-0x0C] / mov ecx, [edx+ecx*4+0x3C]
        code.Bytes([0x8B, 0x55, 0xF4])
            .Bytes([0x8B, 0x4C, 0x8A, 0x3C]);

        code.PushAd()
            .MovEax(PasswordByteConverter.Value)
            .Bytes([0xFF, 0xD0])                              // call eax -> al = decoded byte
            .Bytes([0x8B, 0x0D]).Dword(passwordPosition.Value) // mov ecx, [position]
            .Bytes([0x85, 0xC9]);                             // test ecx, ecx

        var notFirstByte = code.ShortJumpIfNotEqual();

        // First byte of a fresh attempt: zero the 32-byte buffer.
        code.Byte(0x50)                                       // push eax
            .Byte(0x57)                                       // push edi
            .MovEdi(passwordBuffer.Value)
            .Bytes([0x33, 0xC0])                              // xor eax, eax
            .Bytes([0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB]) // stosd x8 = 32 bytes
            .Byte(0x5F)                                       // pop edi
            .Byte(0x58);                                      // pop eax

        code.MarkLabel(notFirstByte);

        code.MovEdx(passwordBuffer.Value)
            .Bytes([0x88, 0x04, 0x0A])                        // mov [edx+ecx], al
            .Byte(0x41)                                       // inc ecx
            .Bytes([0x89, 0x0D]).Dword(passwordPosition.Value) // mov [position], ecx
            .PopAd()
            .JumpTo(PasswordReturnSite);

        return code.Build();
    }

    /// <summary>
    /// Calls <c>SendPacketData</c> with the captured credentials.
    /// </summary>
    /// <remarks>
    /// cdecl, so arguments are pushed right to left and the caller cleans the stack.
    /// The final <c>jmp</c> lands past the client's own packet construction, replacing
    /// it rather than running it too.
    /// </remarks>
    internal static byte[] BuildSendPacket(GameAddress cave)
    {
        var code = new ShellcodeBuilder(cave + SendCodeOffset);

        // SendPacketData("cssddddddd", 0x77, account, password, 127.0.0.1, 0,0,0,0,0, 0x1F)
        code.PushImm32(TrailingFlag)
            .PushImm32(0)
            .PushImm32(0)
            .PushImm32(0)
            .PushImm32(0)
            .PushImm32(0)
            .PushImm32(LoopbackAddress)
            .PushImm32(cave + PasswordBufferOffset)
            .PushImm32(cave + AccountBufferOffset)
            .PushImm32(LoginOpcode)
            .PushImm32(cave + FormatStringOffset)
            .CallTo(SendPacketData)
            .AddEsp(SendArgumentCount * sizeof(uint));

        // Reset the write position so the next attempt starts a fresh password.
        code.MovDwordPtr(cave + PasswordPositionOffset, 0)
            .JumpTo(SendReturnSite);

        return code.Build();
    }

    private static void EnsureFits(byte[] code, int capacity, string what)
    {
        if (code.Length > capacity)
        {
            throw new InvalidOperationException(
                $"{what} shellcode is {code.Length} bytes but only {capacity} are reserved in the cave.");
        }
    }
}
