using Login38.Core.Servers;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Raises the ceiling on how many <c>img</c> sprite resources the client will load.
/// </summary>
/// <remarks>
/// <para>
/// The sprite system has two independent limits, both compiled in: a resource-id range of
/// 7000 handed to the allocator, and a hard bound of 6295 on the surface array — checked
/// before every indexed access and used to size the array's one allocation. Servers that
/// ship custom art run past both, and the client's response to an id above the bound is
/// to draw nothing.
/// </para>
/// <para>
/// Every site holds its limit as an immediate, so raising it means finding each one and
/// widening the constant. The bound and the allocation must move together: a wider bound
/// over the original allocation indexes off the end of the array.
/// </para>
/// </remarks>
public sealed class ImageLimitPatch : IGamePatch
{
    /// <summary>The resource-id range handed to the allocator.</summary>
    private const uint OriginalRange = 7000;

    /// <summary>The compiled-in surface array bound.</summary>
    private const uint OriginalBound = 6295;

    /// <summary>Four bytes per pointer in the surface array.</summary>
    private const uint BytesPerSlot = 4;

    /// <summary>
    /// <c>push 0</c> / <c>push tag</c> / <c>push range</c> / <c>call allocRange</c>.
    /// </summary>
    private static readonly ImmediateSite[] ResourceRange = [new("6A 00 68 ?? ?? ?? ?? 68 {0} E8", 8)];

    /// <summary>
    /// <c>cmp dword ptr [reg+disp], bound</c> — the bound check as the compiler emitted
    /// it, once per register it happened to keep the index in.
    /// </summary>
    /// <remarks>
    /// The reference searched for the bare constant and then read the preceding bytes
    /// back to decide whether it had found an instruction at all. Spelling the
    /// instruction out does that filtering inside the scan that is already running, and
    /// says what is being matched rather than leaving it to a table of ModR/M values.
    /// </remarks>
    private static readonly ImmediateSite[] BoundChecks =
    [
        new("81 79 ?? {0}", 3),           // [ecx+disp8]
        new("81 7B ?? {0}", 3),           // [ebx+disp8]
        new("81 7D ?? {0}", 3),           // [ebp+disp8]
        new("81 7E ?? {0}", 3),           // [esi+disp8]
        new("81 7F ?? {0}", 3),           // [edi+disp8]
        new("81 BD ?? ?? ?? ?? {0}", 6),  // [ebp+disp32]
    ];

    /// <summary><c>push arraySize</c>, immediately before the allocator call.</summary>
    private static readonly ImmediateSite[] ArrayAllocation = [new("68 {0}", 1)];

    private readonly ILogger<ImageLimitPatch> _logger;

    public ImageLimitPatch(ILogger<ImageLimitPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "img-limit";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.ImgLimitEnabled;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var limit = uint.Clamp(
            context.Aux.ImgLimitValue, AuxConfig.ImgLimitBounds.Min, AuxConfig.ImgLimitBounds.Max);

        if (limit != context.Aux.ImgLimitValue)
        {
            _logger.LogWarning("Configured img limit {Configured} is out of range; using {Limit}",
                context.Aux.ImgLimitValue, limit);
        }

        // The array is widened before the bound that indexes into it, so a client that
        // dies between the two is left over-allocated rather than reading past the end.
        var results = new[]
        {
            Widen(context, ArrayAllocation, OriginalBound * BytesPerSlot, limit * BytesPerSlot, "array allocation"),
            Widen(context, BoundChecks, OriginalBound, limit, "array bound"),
            Widen(context, ResourceRange, OriginalRange, limit, "resource range"),
        };

        var patched = results.Sum(r => r.Patched);
        var alreadyPatched = results.Sum(r => r.AlreadyPatched);

        if (patched + alreadyPatched == 0)
        {
            throw new GameProcessException(
                "None of the img limit sites were found; this client does not match any known layout.");
        }

        _logger.LogInformation(
            "img limit raised to {Limit} across {Patched} sites ({Already} already at that value)",
            limit, patched, alreadyPatched);
    }

    /// <summary>Rewrites every occurrence of an immediate that encodes a limit.</summary>
    private WidenResult Widen(
        GamePatchContext context,
        ImmediateSite[] sites,
        uint original,
        uint replacement,
        string description)
    {
        if (original == replacement)
        {
            _logger.LogDebug("{Description} is already {Value}", description, replacement);
            return new WidenResult(0, Count(context, sites, replacement));
        }

        var replacementBytes = BitConverter.GetBytes(replacement);
        var patched = 0;

        foreach (var site in sites)
        {
            foreach (var hit in Find(context, site, original))
            {
                context.Image.Apply(context.Process, hit + site.ImmediateOffset, replacementBytes);
                _logger.LogDebug("{Description} at {Address}: {Original} to {Replacement}",
                    description, hit, original, replacement);
                patched++;
            }
        }

        // Only worth asking when nothing matched: a mix of both values would mean a
        // half-applied patch, and this scan is what tells those two cases apart.
        var already = patched == 0 ? Count(context, sites, replacement) : 0;

        if (patched == 0)
        {
            _logger.Log(already > 0 ? LogLevel.Debug : LogLevel.Warning,
                "{Description}: {Already} sites already hold {Replacement}", description, already, replacement);
        }

        return new WidenResult(patched, already);
    }

    private static int Count(GamePatchContext context, ImmediateSite[] sites, uint value) =>
        sites.Sum(site => Find(context, site, value).Count);

    private static IReadOnlyList<GameAddress> Find(GamePatchContext context, ImmediateSite site, uint immediate)
    {
        var pattern = BytePattern.Parse(site.Template.Replace(
            ImmediateSite.Placeholder,
            BytePattern.Format(BitConverter.GetBytes(immediate)),
            StringComparison.Ordinal));

        // Searched across the whole image rather than a narrowed code range. The
        // reference needed one because it looked for the bare constant and would have hit
        // it in the data sections; every signature here is a whole instruction, so the
        // shape does the filtering the range used to.
        return context.Image.FindAll(pattern);
    }

    /// <param name="Template">Signature text, with <see cref="Placeholder"/> where the immediate goes.</param>
    /// <param name="ImmediateOffset">Where the immediate starts, from the start of the match.</param>
    private readonly record struct ImmediateSite(string Template, int ImmediateOffset)
    {
        internal const string Placeholder = "{0}";
    }

    private readonly record struct WidenResult(int Patched, int AlreadyPatched);
}
