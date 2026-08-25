using Login38.Core;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Makes the client render simplified Chinese, for operators who publish in it.
/// </summary>
/// <remarks>
/// <para>
/// The client is a traditional Chinese build and decides its text code page once during
/// startup. Two things have to change for simplified text: the code page itself, and a
/// branch in the status tooltip that takes a traditional-only path and draws the tooltip
/// as noise.
/// </para>
/// <para>
/// The code page cannot simply be written once. The client's own startup writes it several
/// times over the first few seconds, so this holds the value against those writes for as
/// long as they last, then stops.
/// </para>
/// <para>
/// Gated on the operator's <c>text_encoding</c> setting. The reference gated it on an
/// environment variable alone, which meant no operator could turn it on — the setting that
/// selects the encoding for everything else did not reach this. The variable is still
/// honoured, in both directions, as an override.
/// </para>
/// </remarks>
public sealed class SimplifiedChineseTextPatch : IGamePatch
{
    /// <summary>Forces this on or off regardless of the operator's setting.</summary>
    public const string OverrideVariable = "LOGIN38_FORCE_SIMPLIFIED_TEXT_LOCALE";

    /// <summary>The client's text code page.</summary>
    private static readonly GameAddress TextCodePage = new(0x0096_8618);

    /// <summary>936 — the simplified Chinese code page.</summary>
    private const uint SimplifiedCodePage = 0x0000_03A8;

    /// <summary>
    /// A branch in the status tooltip that only takes the traditional path.
    /// </summary>
    private static readonly GameAddress TooltipEncodingBranch = new(0x0051_26ED);

    /// <summary><c>jz +0x14F</c>.</summary>
    private static ReadOnlySpan<byte> TooltipBranch => [0x0F, 0x84, 0x4F, 0x01, 0x00, 0x00];

    private static ReadOnlySpan<byte> TooltipBranchRemoved => [0x90, 0x90, 0x90, 0x90, 0x90, 0x90];

    /// <summary>
    /// Long enough to cover the client's own writes to the code page, which stop once it
    /// has finished loading its fonts.
    /// </summary>
    private static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<SimplifiedChineseTextPatch> _logger;

    public SimplifiedChineseTextPatch(ILogger<SimplifiedChineseTextPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "simplified-chinese-text";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return WantsSimplified(EnvironmentSwitch.Value(OverrideVariable), context.Aux.TextEncoding);
    }

    /// <summary>
    /// Whether the client should be switched to simplified Chinese.
    /// </summary>
    /// <param name="overrideValue">The raw override, or null when it is not set.</param>
    /// <param name="encoding">The encoding the operator publishes in.</param>
    /// <remarks>
    /// The override wins in both directions, which is what makes it useful: an operator
    /// publishing in GBK can still take a traditional client for a moment to check whether
    /// this patch is what broke something.
    /// </remarks>
    internal static bool WantsSimplified(string? overrideValue, TextEncodingMode encoding)
    {
        if (BooleanText.IsTruthy(overrideValue))
        {
            return true;
        }

        return !BooleanText.IsFalsy(overrideValue) && encoding == TextEncodingMode.Gbk;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var before = context.Process.Read<uint>(TextCodePage);
        context.Process.WriteCode(TextCodePage, BitConverter.GetBytes(SimplifiedCodePage));

        _logger.LogInformation("Text code page at {Address}: 0x{Before:X4} to 0x{After:X4}",
            TextCodePage, before, SimplifiedCodePage);

        // Deliberately not awaited. The client rewrites the code page during startup, so
        // this has to keep re-asserting it for a few seconds — and the patches after this
        // one have their own window to land in, which a synchronous hold would eat.
        _ = Task.Run(() => HoldAsync(context, cancellationToken), CancellationToken.None);
    }

    /// <summary>
    /// Keeps the code page set for as long as the client keeps changing it, then removes
    /// the tooltip branch once it has settled.
    /// </summary>
    private async Task HoldAsync(GamePatchContext context, CancellationToken cancellationToken)
    {
        var until = Environment.TickCount64 + (long)HoldFor.TotalMilliseconds;
        var tooltipDone = false;
        var overwritten = 0;

        try
        {
            while (Environment.TickCount64 < until && context.Process.IsRunning)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (context.Process.TryRead<uint>(TextCodePage, out var current) &&
                    current != SimplifiedCodePage)
                {
                    context.Process.WriteCode(TextCodePage, BitConverter.GetBytes(SimplifiedCodePage));
                    overwritten++;
                }

                // Only once the code page has taken: the branch is meaningless while the
                // client is still rendering traditional text, and patching it early would
                // have to be undone if the code page never held.
                if (!tooltipDone && current == SimplifiedCodePage)
                {
                    tooltipDone = TryRemoveTooltipBranch(context);
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "Held the simplified code page against {Count} of the client's own writes", overwritten);
        }
        catch (OperationCanceledException)
        {
            // The launcher is closing.
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning(e, "Stopped holding the simplified code page");
        }
    }

    private bool TryRemoveTooltipBranch(GamePatchContext context)
    {
        var current = context.Process.ReadBytes(TooltipEncodingBranch, TooltipBranch.Length);

        if (current.AsSpan().SequenceEqual(TooltipBranchRemoved))
        {
            return true;
        }

        if (!current.AsSpan().SequenceEqual(TooltipBranch))
        {
            // Still encrypted, or a client this does not know. Worth another look on the
            // next poll rather than a decision now.
            return false;
        }

        context.Process.WriteCode(TooltipEncodingBranch, TooltipBranchRemoved);
        _logger.LogInformation("Removed the traditional-only tooltip branch at {Address}", TooltipEncodingBranch);
        return true;
    }
}
