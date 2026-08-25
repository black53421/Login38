using System.Diagnostics;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Disarms the client's self-protection check, once its packer has finished unpacking.
/// </summary>
/// <remarks>
/// <para>
/// The shipped client is packed: the two branches this rewrites do not exist in the file
/// on disk and are not in memory when the process starts. They appear only after the
/// packer stub has decrypted the real code, which is why this waits rather than patching
/// immediately. Nothing signals when that happens, so the marker itself is the signal —
/// the first branch reads as its final instruction only once the section holding it has
/// been decrypted.
/// </para>
/// <para>
/// Everything downstream of this depends on it: every signature patch searches bytes that
/// are ciphertext until this returns.
/// </para>
/// </remarks>
public sealed class TimeProtectionBypassPatch : IGamePatch
{
    /// <summary>
    /// The conditional jump that reaches the protection check. Doubles as the marker for
    /// "decryption has finished", since reading it as anything else means it is still
    /// ciphertext.
    /// </summary>
    private static readonly GameAddress DecryptedMarker = new(0x004E_204E);

    /// <summary><c>jnz +0x97</c> — the instruction as it appears once decrypted.</summary>
    private const uint MarkerDecrypted = 0x0097_850F;

    /// <summary><c>nop</c> + <c>jmp +0x97</c> — the branch taken unconditionally.</summary>
    private const uint MarkerBypassed = 0x0097_E990;

    /// <summary>The second check, reached later in startup.</summary>
    private static readonly GameAddress SecondCheck = new(0x0072_2761);

    /// <summary><c>mov al, 1</c> + <c>nop</c> + <c>test</c> — forces the passing result.</summary>
    private const uint SecondCheckBypassed = 0x8590_01B0;

    /// <summary>
    /// Generous on purpose. Unpacking is CPU-bound and this also covers a cold start off
    /// a slow disk on a machine with aggressive real-time scanning.
    /// </summary>
    private static readonly TimeSpan DecryptTimeout = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan ProgressLogInterval = TimeSpan.FromSeconds(10);

    private readonly ILogger<TimeProtectionBypassPatch> _logger;

    public TimeProtectionBypassPatch(ILogger<TimeProtectionBypassPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "time-protection-bypass";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var process = context.Process;
        var alreadyBypassed = WaitForDecryption(process, cancellationToken);

        // Written before the suspension, not inside it. The check runs early enough that
        // it can be reached while threads are being suspended one at a time, and a thread
        // that gets there before this lands takes the failing branch. Writing a single
        // aligned dword is atomic with respect to the reader either way.
        if (alreadyBypassed)
        {
            _logger.LogDebug("{Address} was already bypassed", DecryptedMarker);
        }
        else
        {
            process.WriteCode(DecryptedMarker, BitConverter.GetBytes(MarkerBypassed));
        }

        // The second site is not racing anything, so it gets the safe treatment.
        using (var suspension = process.SuspendThreads())
        {
            _logger.LogDebug("Suspended {Count} threads to patch {Address}", suspension.Count, SecondCheck);
            process.WriteCode(SecondCheck, BitConverter.GetBytes(SecondCheckBypassed));

            // Read back inside the suspension: a client that restores its own code would
            // otherwise have a window to do so between the write and the check.
            Verify(process, DecryptedMarker, MarkerBypassed);
            Verify(process, SecondCheck, SecondCheckBypassed);
        }

        // Everything cached before this point was ciphertext.
        context.InvalidateImage();
    }

    /// <summary>Polls until the packer has decrypted the code. Returns whether it was already bypassed.</summary>
    private bool WaitForDecryption(RemoteProcess process, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        uint lastValue = 0;
        var everReadable = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.TryRead<uint>(DecryptedMarker, out var value))
            {
                if (value == MarkerDecrypted)
                {
                    _logger.LogInformation("Client finished decrypting after {Elapsed:0.0}s", elapsed.Elapsed.TotalSeconds);
                    return false;
                }

                if (value == MarkerBypassed)
                {
                    // A previous run against this same process, or the startup hook.
                    _logger.LogInformation("Client was already patched after {Elapsed:0.0}s", elapsed.Elapsed.TotalSeconds);
                    return true;
                }

                lastValue = value;
                everReadable = true;
            }

            // Waiting out the full two minutes on a client that has already died is the
            // difference between a clear message and an inexplicable stall.
            if (!process.IsRunning)
            {
                throw new GameProcessException(
                    $"The client exited after {elapsed.Elapsed.TotalSeconds:0.0}s, before it finished starting up.");
            }

            if (elapsed.Elapsed >= DecryptTimeout)
            {
                var seen = everReadable ? $"0x{lastValue:X8}" : "nothing readable";
                throw new GameProcessException(
                    $"The client did not finish decrypting within {DecryptTimeout.TotalSeconds:0}s " +
                    $"({DecryptedMarker} last read as {seen}).");
            }

            if (elapsed.Elapsed - lastReport >= ProgressLogInterval)
            {
                lastReport = elapsed.Elapsed;
                _logger.LogDebug("Still waiting for decryption after {Elapsed:0}s (last read 0x{Value:X8})",
                    lastReport.TotalSeconds, lastValue);
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static void Verify(RemoteProcess process, GameAddress address, uint expected)
    {
        var actual = process.Read<uint>(address);
        if (actual != expected)
        {
            throw new GameProcessException(
                $"Patch at {address} did not take: read 0x{actual:X8}, expected 0x{expected:X8}.");
        }
    }
}
