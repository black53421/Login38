using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Widens the client's fixed pool of PNG surfaces.
/// </summary>
/// <remarks>
/// <para>
/// The surface manager pre-allocates its whole pool during startup: one array of 1564
/// pointers, then a loop that constructs a surface into every slot. Three constants
/// describe that pool — the allocation size, the construction loop's bound and the
/// teardown loop's bound — and all three have to agree. Servers that add art run out of
/// slots long before they run out of anything else.
/// </para>
/// <para>
/// The construction loop runs from the CRT's initialiser list, so this only works inside
/// the same narrow window as the protection bypass: after decryption, before the client's
/// own startup gets that far. Later is not a smaller effect, it is a client that walks
/// off the end of an array it already sized.
/// </para>
/// </remarks>
public sealed class PngLimitPatch : IGamePatch
{
    /// <summary>How many surfaces the client was built with.</summary>
    private const uint OriginalLimit = 0x61C;

    /// <summary>Four bytes per pointer in the array.</summary>
    private const uint BytesPerSlot = 4;

    /// <summary>
    /// Roughly what one surface costs before any pixels are loaded, used only to report
    /// what the new pool is worth in memory.
    /// </summary>
    private const uint BytesPerSurface = 36;

    /// <summary>
    /// Not configurable, and not meant to be. The cost of a slot is a few dozen bytes, so
    /// the pool is sized to be beyond what any server will use rather than tuned.
    /// </summary>
    private const uint TargetLimit = 100_000;

    private readonly ILogger<PngLimitPatch> _logger;

    public PngLimitPatch(ILogger<PngLimitPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "png-limit";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var limit = BitConverter.GetBytes(TargetLimit);
        var originalLimit = BitConverter.GetBytes(OriginalLimit);
        var allocation = BitConverter.GetBytes(TargetLimit * BytesPerSlot);
        var originalAllocation = BitConverter.GetBytes(OriginalLimit * BytesPerSlot);

        // The allocation goes first, so a client that dies part-way through is left with
        // an array bigger than the loops that walk it rather than smaller.
        var sites = new[]
        {
            // push size / call malloc / add esp, 4
            Rewrite(context, "68 {0} E8 ?? ?? ?? ?? 83 C4 04",
                "the surface array allocation", 1, originalAllocation, allocation),

            // mov [ebp-0x10], edx / cmp [ebp-0x10], limit / jge
            Rewrite(context, "89 55 F0 81 7D F0 {0} 7D",
                "the surface construction loop", 6, originalLimit, limit),

            // mov [ebp-4], ecx / cmp [ebp-4], limit / jge
            Rewrite(context, "89 4D FC 81 7D FC {0} 7D",
                "the surface teardown loop", 6, originalLimit, limit),
        };

        var effective = sites.Count(s => s.Effective);

        // Partial application is the dangerous outcome, not merely the useless one: a
        // wider construction loop over the original array writes past its end.
        if (effective != sites.Length)
        {
            throw new GameProcessException(
                $"Only {effective} of {sites.Length} PNG pool sites could be patched; the constants " +
                "have to move together, so a partial result is worse than none.");
        }

        _logger.LogInformation(
            "PNG surface pool raised to {Limit} (about {Cost} KB)",
            TargetLimit, TargetLimit * BytesPerSurface / 1024);
    }

    private SiteEdit Rewrite(
        GamePatchContext context,
        string template,
        string description,
        int immediateOffset,
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> replacement)
    {
        var edit = CodeEdit.ReplaceImmediate(
            context, template, description, immediateOffset, original, replacement);

        _logger.LogDebug("{Description}: {Status} at {Address}", description, edit.Status, edit.Address);
        return edit;
    }
}
