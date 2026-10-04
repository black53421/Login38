namespace Login38.Aux.Runtime;

/// <summary>
/// Wakes the helper loop before its normal cadence when a latency-sensitive event arrives.
/// </summary>
/// <remarks>
/// Signals are coalesced. The helper only needs to know that something changed; a burst of
/// keyboard events must not turn into a backlog of helper passes after the key is released.
/// </remarks>
public sealed class AuxWakeSignal
{
    private TaskCompletionSource<bool>? _waiter;
    private int _pending;

    /// <summary>Requests an early helper pass.</summary>
    public void Signal()
    {
        // This runs from the low-level keyboard hook, so it deliberately takes no lock and
        // allocates nothing. The pending bit closes the race where a signal lands just
        // before the host publishes its next waiter.
        Interlocked.Exchange(ref _pending, 1);
        Volatile.Read(ref _waiter)?.TrySetResult(true);
    }

    /// <summary>
    /// Waits for either an early signal or the normal helper cadence to expire.
    /// </summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || Interlocked.Exchange(ref _pending, 0) == 1)
        {
            return;
        }

        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _waiter, waiter);

        // A signal may have arrived between the first pending check and publishing the
        // waiter. Consume it here instead of sleeping for the whole cadence.
        if (Interlocked.Exchange(ref _pending, 0) == 1)
        {
            Interlocked.CompareExchange(ref _waiter, null, waiter);
            return;
        }

        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(waiter.Task, timeoutTask).ConfigureAwait(false);

        Interlocked.CompareExchange(ref _waiter, null, waiter);

        if (completed == waiter.Task)
        {
            Interlocked.Exchange(ref _pending, 0);
            await waiter.Task.ConfigureAwait(false);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // If the signal raced the timeout, this cadence pass is already about to run and
        // will consume the queued keyboard edge, so it is safe to coalesce that wake here.
        if (waiter.Task.IsCompletedSuccessfully)
        {
            Interlocked.Exchange(ref _pending, 0);
        }
    }
}
