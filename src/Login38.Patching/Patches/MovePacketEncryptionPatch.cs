using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Stops the client obfuscating and encrypting its movement packets.
/// </summary>
/// <remarks>
/// <para>
/// Movement is the one packet the client treats specially: it scrambles a state byte and
/// then encrypts the packet with a scheme nothing else uses. Emulated servers implement
/// the ordinary path, so unless the operator's server speaks this dialect the client and
/// the server disagree about where the character is. Both behaviours hang off a
/// conditional jump, so both are turned off by making the jump unconditional.
/// </para>
/// <para>
/// Only meaningful once the player is in the world — the code is reached from the
/// movement handler, and patching it earlier would work but tells nothing about whether
/// the client got that far.
/// </para>
/// </remarks>
public sealed class MovePacketEncryptionPatch : IGamePatch
{
    /// <summary>
    /// <c>movsx eax, byte ptr [edx+0x14]</c> / <c>cmp eax, 8</c> / <c>jz</c> — the branch
    /// into the state scrambler.
    /// </summary>
    private const string StateObfuscation = "0F BE 42 14 83 F8 08 ?? 21 8B 0D B8 D2 C2 00";

    private const int StateObfuscationJumpOffset = 7;

    private const byte JumpIfZero = 0x74;

    /// <summary>
    /// <c>movsx edx, byte ptr [mode]</c> / <c>cmp edx, 3</c> / <c>jnz</c> — the branch
    /// around the movement packet's encryption.
    /// </summary>
    private const string PacketEncryption =
        "0F BE 15 E1 AE 9A 00 83 FA 03 ?? 22 A1 B8 D2 C2 00 0F BE 48 15 83 F1 49";

    private const int PacketEncryptionJumpOffset = 10;

    private const byte JumpIfNotZero = 0x75;

    private const byte JumpAlways = 0xEB;

    private readonly ILogger<MovePacketEncryptionPatch> _logger;

    public MovePacketEncryptionPatch(ILogger<MovePacketEncryptionPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "move-packet-no-encrypt";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.InWorld;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.MovePacketNoEncrypt;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Refreshed because everything cached before the player entered the world was
        // read from a client that had not finished loading its code.
        context.InvalidateImage();

        var sites = new[]
        {
            Flip(context, StateObfuscation, "the movement state scrambler",
                StateObfuscationJumpOffset, JumpIfZero),
            Flip(context, PacketEncryption, "the movement packet encryption",
                PacketEncryptionJumpOffset, JumpIfNotZero),
        };

        // Half of this is worse than none: the server would receive packets that are
        // unscrambled but still encrypted, which reads as a protocol error rather than as
        // a patch that half worked.
        if (sites.Any(s => !s.Effective))
        {
            throw new GameProcessException(
                "Only part of the movement packet path could be patched; the two branches have to move together.");
        }

        _logger.LogInformation("Movement packets are now sent unscrambled and unencrypted");
    }

    private SiteEdit Flip(GamePatchContext context, string pattern, string description, int offset, byte original)
    {
        var edit = CodeEdit.Replace(context, pattern, description, offset, original, JumpAlways);
        _logger.LogDebug("{Description}: {Status} at {Address}", description, edit.Status, edit.Address);
        return edit;
    }
}
