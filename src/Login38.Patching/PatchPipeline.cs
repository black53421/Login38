using Login38.Interop;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Login38.Patching;

/// <summary>Why a patch did not run, or how it failed.</summary>
public enum PatchStatus
{
    Applied,

    /// <summary>The operator's configuration turned it off.</summary>
    Skipped,

    /// <summary>It threw. The launch continues without it.</summary>
    Failed,
}

/// <param name="Name">The patch's identifier.</param>
/// <param name="Status">What happened.</param>
/// <param name="Duration">How long it took.</param>
/// <param name="Error">The failure, when <see cref="Status"/> is <see cref="PatchStatus.Failed"/>.</param>
public sealed record PatchOutcome(string Name, PatchStatus Status, TimeSpan Duration, Exception? Error = null);

/// <summary>
/// Runs the patch set against a launched game.
/// </summary>
/// <remarks>
/// Deliberately fail-soft. These patches match byte signatures in a client this
/// launcher does not control; a client update can invalidate any one of them. Losing a
/// cosmetic feature is acceptable, refusing to start the game is not — so a patch that
/// throws is logged and the rest still run.
/// </remarks>
public sealed class PatchPipeline
{
    private static readonly TimeSpan PhasePollInterval = TimeSpan.FromMilliseconds(50);

    private readonly IReadOnlyList<IGamePatch> _patches;
    private readonly ILogger<PatchPipeline> _logger;

    public PatchPipeline(IEnumerable<IGamePatch> patches, ILogger<PatchPipeline> logger)
    {
        _patches = [.. patches];
        _logger = logger;
    }

    /// <summary>
    /// Applies every patch belonging to <paramref name="phase"/>, in registration order.
    /// </summary>
    /// <remarks>
    /// Registration order is the contract. Several of these patches depend on an earlier
    /// one having run — the client's code is packed until the protection bypass reports
    /// decryption finished, and every signature scan after that reads bytes it produced.
    /// </remarks>
    public IReadOnlyList<PatchOutcome> Apply(
        GamePatchContext context, PatchPhase phase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var outcomes = new List<PatchOutcome>();
        var total = Stopwatch.StartNew();

        foreach (var patch in _patches.Where(p => p.Phase == phase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(ApplyOne(patch, context, cancellationToken));
        }

        var applied = outcomes.Count(o => o.Status == PatchStatus.Applied);
        var failed = outcomes.Count(o => o.Status == PatchStatus.Failed);
        _logger.LogInformation(
            "{Phase} patching finished in {Elapsed}ms: {Applied} applied, {Skipped} skipped, {Failed} failed",
            phase, total.ElapsedMilliseconds, applied, outcomes.Count - applied - failed, failed);

        return outcomes;
    }

    /// <summary>
    /// Waits for the client to reach the state <paramref name="phase"/> describes.
    /// </summary>
    /// <returns>False if the client exited or the wait timed out first.</returns>
    public static async Task<bool> WaitForPhaseAsync(
        GamePatchContext context,
        PatchPhase phase,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (phase == PatchPhase.Startup)
        {
            return true;
        }

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (!context.Process.IsRunning)
            {
                return false;
            }

            if (IsReached(context, phase))
            {
                return true;
            }

            await Task.Delay(PhasePollInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool IsReached(GamePatchContext context, PatchPhase phase)
    {
        if (phase == PatchPhase.WindowVisible)
        {
            // Recorded on the context, because the patches that wait for it need the
            // handle and finding it again by title would pick the wrong copy of the game.
            context.Window ??= GameWindow.Find(context.Process.Id);
            return context.Window is not null;
        }

        return context.Process.TryRead<uint>(GameAddresses.GameState, out var state) &&
               state == GameAddresses.InWorld;
    }

    private PatchOutcome ApplyOne(IGamePatch patch, GamePatchContext context, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();

        try
        {
            if (!patch.ShouldApply(context))
            {
                _logger.LogDebug("{Patch}: skipped by configuration", patch.Name);
                return new PatchOutcome(patch.Name, PatchStatus.Skipped, elapsed.Elapsed);
            }

            patch.Apply(context, cancellationToken);
            _logger.LogInformation("{Patch}: applied in {Elapsed}ms", patch.Name, elapsed.ElapsedMilliseconds);
            return new PatchOutcome(patch.Name, PatchStatus.Applied, elapsed.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Logged at warning, not error: a signature that no longer matches is an
            // expected consequence of a client update, not a bug in the launcher.
            _logger.LogWarning(e, "{Patch}: failed after {Elapsed}ms; continuing without it",
                patch.Name, elapsed.ElapsedMilliseconds);
            return new PatchOutcome(patch.Name, PatchStatus.Failed, elapsed.Elapsed, e);
        }
    }
}
