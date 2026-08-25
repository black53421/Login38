using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching;

/// <summary>What happened when a site was tried.</summary>
public enum SiteAttempt
{
    /// <summary>Still encrypted, or not yet what it will be. Worth trying again.</summary>
    NotReady,

    /// <summary>Patched, or already was. Nothing more to do with it.</summary>
    Settled,
}

/// <summary>
/// Patches code the client decrypts only when it first runs it.
/// </summary>
/// <remarks>
/// <para>
/// Most of the client is decrypted in one pass at startup, which the protection bypass
/// waits for. Some of it is not: panels the player has never opened stay encrypted until
/// they open one, which may be an hour into a session or never.
/// </para>
/// <para>
/// So these sites cannot be patched at a moment — they have to be watched for. This polls
/// until every one has settled or the window closes, and reports what it got either way.
/// A site that never decrypts is a panel the player never opened, which is not a failure.
/// </para>
/// </remarks>
public static class DeferredSites
{
    /// <summary>
    /// How long to keep watching.
    /// </summary>
    /// <remarks>
    /// Long, because that is how long a session is. The cost is one pass over a handful of
    /// addresses every few hundred milliseconds, against a process that is rendering a game.
    /// </remarks>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(10);

    /// <inheritdoc cref="DefaultWindow"/>
    public static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Watches <paramref name="sites"/> until each has settled or the window closes.
    /// </summary>
    /// <param name="process">The game, so that watching stops when it exits.</param>
    /// <param name="sites">The addresses to try.</param>
    /// <param name="attempt">What to do with one site. Must be safe to call repeatedly.</param>
    /// <param name="description">What these sites are, for the log.</param>
    /// <returns>How many settled.</returns>
    public static async Task<int> WatchAsync(
        RemoteProcess process,
        IReadOnlyList<GameAddress> sites,
        Func<GameAddress, SiteAttempt> attempt,
        string description,
        ILogger logger,
        TimeSpan? window = null,
        TimeSpan? poll = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(logger);

        var pending = new List<GameAddress>(sites);
        var settled = 0;
        var deadline = Environment.TickCount64 + (long)(window ?? DefaultWindow).TotalMilliseconds;

        while (pending.Count > 0 && Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Checked every pass. The reference kept polling a dead process for the whole
            // ten minutes, which is ten minutes of failed reads after the player quit.
            if (!process.IsRunning)
            {
                break;
            }

            var before = settled;

            pending.RemoveAll(site =>
            {
                if (attempt(site) != SiteAttempt.Settled)
                {
                    return false;
                }

                settled++;
                return true;
            });

            if (settled != before)
            {
                logger.LogDebug("{Description}: {Settled} of {Total} settled",
                    description, settled, sites.Count);
            }

            if (pending.Count == 0)
            {
                break;
            }

            await Task.Delay(poll ?? DefaultPoll, cancellationToken).ConfigureAwait(false);
        }

        if (pending.Count == 0)
        {
            logger.LogInformation("{Description}: all {Total} settled", description, sites.Count);
        }
        else
        {
            // Not a warning. The usual reason a site is still encrypted is that the player
            // never opened the panel it belongs to.
            logger.LogInformation(
                "{Description}: {Settled} of {Total} settled; the rest belong to screens that were never opened",
                description, settled, sites.Count);
        }

        return settled;
    }

    /// <summary>
    /// The absolute target of an <c>E8</c> or <c>E9</c> instruction.
    /// </summary>
    /// <returns>Null when the bytes are not one — which is what an encrypted site looks like.</returns>
    public static GameAddress? RelativeTarget(GameAddress site, ReadOnlySpan<byte> instruction)
    {
        const int Length = 5;

        if (instruction.Length < Length || instruction[0] is not (0xE8 or 0xE9))
        {
            return null;
        }

        return site + (Length + BitConverter.ToInt32(instruction[1..Length]));
    }

    /// <summary>Encodes <c>call rel32</c> from <paramref name="site"/> to <paramref name="target"/>.</summary>
    public static byte[] BuildCall(GameAddress site, GameAddress target)
    {
        const int Length = 5;
        var call = new byte[Length];

        call[0] = 0xE8;
        BitConverter.GetBytes(unchecked((uint)(target - (site + Length)))).CopyTo(call, 1);

        return call;
    }
}
