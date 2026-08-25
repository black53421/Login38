using Login38.Core;
using Login38.Core.Morph;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Feeds the client a morph table from memory instead of from disk.
/// </summary>
/// <remarks>
/// <para>
/// The morph table says which sprite each appearance-changing item shows. The client reads
/// it from a plain file beside itself, which means players can read it too — and it lists
/// content the operator has not released.
/// </para>
/// <para>
/// This replaces the point where the client has just read the file and is about to record
/// the buffer and its length. Both are overwritten with a copy the launcher put in the
/// client's own memory, so the file on disk can be the encoder's package and the client
/// never learns the difference.
/// </para>
/// <para>
/// The site is packed until the client unpacks itself, so this waits for the instructions
/// to appear rather than assuming they are there. It runs immediately after the patch that
/// waits for unpacking, because the client reads its morph table early and the window
/// between the two is not long.
/// </para>
/// </remarks>
public sealed class MorphTablePatch : IGamePatch
{
    /// <summary>Turns the whole thing off; the client reads its file as it always did.</summary>
    public const string DisableVariable = "LOGIN38_DISABLE_FILE_HOOK";

    /// <summary>Turns off the run-cycle pass, leaving the table as the operator wrote it.</summary>
    public const string SmoothRunVariable = "LOGIN38_MORPH_PREPROCESS";

    /// <summary>
    /// Where the client records the buffer it just read.
    /// </summary>
    /// <remarks>
    /// <c>push 1; lea ecx, [ebp-...]</c>, the first four bytes of which are the signature
    /// this waits for.
    /// </remarks>
    private static readonly GameAddress HookSite = new(0x0058_788B);

    /// <summary>Bytes at the site once the client has unpacked itself.</summary>
    private static ReadOnlySpan<byte> SiteBytes => [0x6A, 0x01, 0x8D, 0x4D];

    /// <summary>Where the cave rejoins the client, past the code it replaces.</summary>
    private static readonly GameAddress Rejoin = new(0x0058_794F);

    /// <summary><c>[ebp-0x14]</c> — the buffer length the client is about to store.</summary>
    private const int BufferLength = -0x14;

    /// <summary><c>[ebp-0x23C]</c> — the object whose <c>+8</c> holds the buffer pointer.</summary>
    private const int Owner = -0x23C;

    private const byte OwnerBufferField = 0x08;

    private const int CaveSize = 64;

    /// <summary>
    /// How long to wait for the site to be decrypted.
    /// </summary>
    /// <remarks>
    /// Short, because by the time this runs the patch that waits for unpacking has already
    /// returned and the bytes should be there. The wait is for the case where they are not,
    /// and it has to end well before the client reads its table.
    /// </remarks>
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan WaitPoll = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<MorphTablePatch> _logger;

    private MorphTableSource? _source;

    public MorphTablePatch(ILogger<MorphTablePatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "morph-table";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (EnvironmentSwitch.IsEnabled(DisableVariable))
        {
            return false;
        }

        var chosen = context.Aux.TransformFileName;

        _source = MorphTableFile.Locate(context.GameDirectory, GameAddresses.ExecutableName, chosen);

        if (!MorphTableFile.ShouldHook(context.Aux.TransformFile, _source))
        {
            // Said out loud when a name was asked for, because the operator picked one and
            // a silent nothing looks the same as the feature being switched off.
            if (!string.IsNullOrWhiteSpace(chosen) && _source is null)
            {
                _logger.LogWarning(
                    "No morph table named {Name} in {Directory}", chosen, context.GameDirectory);
            }

            return false;
        }

        _logger.LogInformation("Morph table from {Path}", _source!.Value.Path);
        return true;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var source = _source
            ?? throw new GameProcessException("No morph table was located for this client.");

        // Read before anything is written into the client: a package that fails its tag
        // should leave the game untouched rather than half-hooked.
        var table = MorphTableFile.Load(source, smoothRun: !EnvironmentSwitch.IsDisabled(SmoothRunVariable));
        var process = context.Process;

        if (table.SmoothRun is { } report)
        {
            _logger.LogInformation(
                "Run cycles folded into {Converted} of {Sprites} sprites ({Walking} walking, {Running} running)",
                report.Converted, report.Sprites, report.Walking, report.Running);
        }

        if (!WaitForSite(process, cancellationToken))
        {
            throw new GameProcessException(
                $"The morph load at {HookSite} did not appear within {WaitTimeout.TotalSeconds:0}s.");
        }

        if (InlineHook.IsInstalledAt(process, HookSite))
        {
            _logger.LogDebug("The morph load at {Address} is already diverted", HookSite);
            context.MorphTableInstalled = true;
            return;
        }

        var buffer = process.AllocateExecutable(table.Bytes.Length);
        process.WriteCode(buffer, table.Bytes);

        var cave = process.AllocateExecutable(CaveSize);
        var shellcode = BuildShellcode(cave, Rejoin, buffer, table.Bytes.Length);

        if (shellcode.Length > CaveSize)
        {
            throw new GameProcessException(
                $"The morph shellcode is {shellcode.Length} bytes but the cave is {CaveSize}.");
        }

        process.WriteCode(cave, shellcode);
        InlineHook.InstallJump(process, HookSite, cave);

        context.MorphTableInstalled = true;
        context.MorphTableHasRunCycles = table.SmoothRun is not null;

        _logger.LogInformation(
            "The client now reads its morph table from {Buffer} ({Length} bytes), through a cave at {Cave}",
            buffer, table.Bytes.Length, cave);
    }

    /// <summary>Waits for the site to stop being ciphertext.</summary>
    /// <returns>False if it never appeared, or the client exited first.</returns>
    private static bool WaitForSite(RemoteProcess process, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)WaitTimeout.TotalMilliseconds;
        Span<byte> current = stackalloc byte[SiteBytes.Length];

        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!process.IsRunning)
            {
                return false;
            }

            // Either the instructions this expects, or a jump a previous run left there,
            // which Apply reports as already done rather than installing twice.
            if (process.TryReadBytes(HookSite, current) &&
                (current.SequenceEqual(SiteBytes) || current[0] == InlineHook.JumpOpcode))
            {
                return true;
            }

            Thread.Sleep(WaitPoll);
        }

        return false;
    }

    /// <summary>
    /// Assembles the replacement: point the client at the launcher's buffer and tell it
    /// how long it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entered by a jump, so <c>ebp</c> is still the client's frame and both destinations
    /// are locals of it. <c>eax</c> and <c>edx</c> are scratch across the code this
    /// replaces, so nothing is saved.
    /// </para>
    /// <para>
    /// The pointer skips the buffer's marker byte while the length counts it, which leaves
    /// the client's own bounds one byte long. That is what the client's original code does
    /// with its own read, so it is what the replacement does: the marker is the client's
    /// header convention, not this launcher's.
    /// </para>
    /// </remarks>
    internal static byte[] BuildShellcode(
        GameAddress cave, GameAddress rejoin, GameAddress buffer, int length) =>
        new ShellcodeBuilder(cave)
            .MovEax((uint)length)
            .MovLocalFromEax(BufferLength)
            .MovEax((buffer + 1).Value)
            .MovEdxLocal(Owner)
            .MovEdxPtrFromEax(OwnerBufferField)
            .JumpTo(rejoin)
            .Build();
}
