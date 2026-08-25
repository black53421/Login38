using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Stops the client killing itself over its own memory-integrity checks.
/// </summary>
/// <remarks>
/// <para>
/// The client hashes its sprite tables at startup and compares the result twice: once
/// against a value it recorded during initialisation, and once against a constant
/// compiled into it. A mismatch shows an <c>ERROR</c> box and calls
/// <c>ExitProcess</c>. Every other patch here changes memory the first hash covers, so
/// without this the client refuses to run as soon as anything else works.
/// </para>
/// <para>
/// Both sites are a conditional jump taken when the hash matches. Turning each into an
/// unconditional one skips the detection whatever the hash says, which is a single byte
/// and leaves the surrounding code untouched.
/// </para>
/// </remarks>
public sealed class AntiCheatBypassPatch : IGamePatch
{
    /// <summary>
    /// <c>mov [ebp-4], eax</c> / <c>mov eax, [ebp-4]</c> / <c>cmp eax, [storedHash]</c> /
    /// <c>jz skip</c> / <c>cmp [gameState], 3</c> / <c>jnz skip</c>.
    /// </summary>
    private const string ComputedHashCheck =
        "89 45 FC 8B 45 FC 3B 05 ?? ?? ?? ?? ?? 3C 83 3D ?? ?? ?? ?? 03 75 33";

    private const int ComputedHashJumpOffset = 12;

    /// <summary>
    /// <c>add esp, 8</c> / <c>cmp eax, 0x5967</c> / <c>jz skip</c>, after the hash call.
    /// </summary>
    private const string ConstantHashCheck = "83 C4 08 3D 67 59 00 00 ?? 2A";

    private const int ConstantHashJumpOffset = 8;

    private const byte JumpIfZero = 0x74;
    private const byte JumpAlways = 0xEB;

    private readonly ILogger<AntiCheatBypassPatch> _logger;

    public AntiCheatBypassPatch(ILogger<AntiCheatBypassPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "anti-cheat-bypass";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var computed = Flip(context, ComputedHashCheck, "the computed-hash check", ComputedHashJumpOffset);
        var constant = Flip(context, ConstantHashCheck, "the constant-hash check", ConstantHashJumpOffset);

        // The reference logged a warning and reported success even when it had patched
        // nothing, which reads as "anti-cheat bypassed" in the log of a client that is
        // about to exit on its own integrity check. Neither site found is a failure.
        if (!computed.Effective && !constant.Effective)
        {
            throw new GameProcessException(
                "Neither integrity check was found; the client will exit once any other patch lands.");
        }

        if (!computed.Effective || !constant.Effective)
        {
            _logger.LogWarning(
                "Only one of the two integrity checks was bypassed (computed: {Computed}, constant: {Constant})",
                computed.Status, constant.Status);
        }
    }

    private SiteEdit Flip(GamePatchContext context, string pattern, string description, int offset)
    {
        var edit = CodeEdit.Replace(context, pattern, description, offset, JumpIfZero, JumpAlways);

        if (edit.Status is SiteStatus.NotFound)
        {
            _logger.LogWarning("Could not find {Description}", description);
        }
        else
        {
            _logger.LogDebug("{Description} at {Address}: {Status}", description, edit.Address, edit.Status);
        }

        return edit;
    }
}
