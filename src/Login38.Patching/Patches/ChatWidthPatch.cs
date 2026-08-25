using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Lets chat lines use the full width of the chat box.
/// </summary>
/// <remarks>
/// <para>
/// The client stores each chat line in a fixed-size ring entry and wraps at 55 bytes,
/// then draws one entry per line. The chat box is wider than that, so about a third of it
/// on the right is permanently blank — the text never reaches it regardless of window
/// size.
/// </para>
/// <para>
/// Raising the wrap to 68 bytes fills the visible width. The entry has 96 bytes of text
/// before its colour and source fields begin, so 68 stays well inside the structure. It
/// is a single immediate at a known address; there is no signature because the
/// instruction around it is unremarkable and would match in several places.
/// </para>
/// </remarks>
public sealed class ChatWidthPatch : IGamePatch
{
    /// <summary>
    /// The immediate of <c>mov dword ptr [ebp-4], 0x37</c> at <c>0x004378BE</c>, inside
    /// <c>AddChatLine</c>.
    /// </summary>
    private static readonly GameAddress WrapLimit = new(0x0043_78C1);

    /// <summary>55 bytes — about 27 full-width characters.</summary>
    private const uint OriginalWrap = 0x37;

    /// <summary>68 bytes — measured against the right edge of the chat box.</summary>
    private const uint WiderWrap = 0x44;

    private readonly ILogger<ChatWidthPatch> _logger;

    public ChatWidthPatch(ILogger<ChatWidthPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "chat-width";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var current = context.Process.Read<uint>(WrapLimit);

        if (current == WiderWrap)
        {
            _logger.LogDebug("Chat wrap at {Address} is already {Wrap}", WrapLimit, WiderWrap);
            return;
        }

        // A fixed address with no signature to confirm it, so the old value is the only
        // evidence that this is the right instruction. Writing regardless would put 68
        // into whatever constant a different client build put there.
        if (current != OriginalWrap)
        {
            throw new GameProcessException(
                $"Chat wrap at {WrapLimit} reads 0x{current:X}, expected 0x{OriginalWrap:X}; " +
                "this is not the instruction the address refers to.");
        }

        context.Process.WriteCode(WrapLimit, BitConverter.GetBytes(WiderWrap));
        _logger.LogInformation("Chat wrap raised from {Old} to {New} bytes", OriginalWrap, WiderWrap);
    }
}
