using System.Diagnostics.CodeAnalysis;

namespace Login38.Interop;

/// <summary>
/// One claimed slot in the multi-instance limit, released when disposed.
/// </summary>
/// <remarks>
/// <para>
/// A named mutex is what makes this survive a launcher that is killed rather than closed:
/// Windows marks a mutex abandoned when its owning thread dies without releasing it, and
/// the next waiter still acquires it. A counted primitive would leak the slot until every
/// launcher on the machine had exited.
/// </para>
/// <para>
/// Mutex ownership belongs to a thread, not a process, so the slot runs its own: acquiring
/// on the caller's thread would mean releasing on whichever thread happened to dispose it,
/// which throws. Keeping the thread inside makes that impossible to get wrong from
/// outside.
/// </para>
/// </remarks>
public sealed class InstanceSlot : IDisposable
{
    private readonly Thread? _owner;
    private readonly ManualResetEventSlim? _release;

    private bool _released;

    private InstanceSlot(string? name, Thread? owner, ManualResetEventSlim? release)
    {
        Name = name;
        _owner = owner;
        _release = release;
    }

    /// <summary>The mutex held, or null when the limit is unlimited.</summary>
    public string? Name { get; }

    /// <summary>Whether this slot corresponds to a real mutex.</summary>
    public bool IsHeld => Name is not null;

    /// <summary>A slot for an unlimited configuration; holds nothing.</summary>
    internal static InstanceSlot Unlimited() => new(null, null, null);

    /// <summary>
    /// Claims <paramref name="name"/> if it is free, on a thread that stays alive until
    /// the slot is disposed.
    /// </summary>
    internal static InstanceSlot? TryClaim(string name)
    {
        using var acquired = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var claimed = false;

        var owner = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, name, out _);

            try
            {
                claimed = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // The previous holder's process died. The wait succeeded; the exception is
                // Windows reporting that whatever the mutex protected may be inconsistent,
                // which for a launch slot means nothing.
                claimed = true;
            }

            acquired.Set();

            if (!claimed)
            {
                return;
            }

            release.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
            Name = $"instance-slot:{name}",
        };

        owner.Start();
        acquired.Wait();

        if (claimed)
        {
            return new InstanceSlot(name, owner, release);
        }

        release.Dispose();
        return null;
    }

    /// <summary>Gives the slot back.</summary>
    /// <remarks>
    /// Called twice on the ordinary path: once the moment the game exits, because a slot
    /// stands for a running game and there is no longer one, and again when the session it
    /// belongs to is disposed. The second call has to be free — a slot that threw the
    /// second time would take the launcher's own shutdown down with it.
    /// </remarks>
    public void Dispose()
    {
        if (_release is null || _released)
        {
            return;
        }

        _released = true;
        _release.Set();

        // Bounded: a slot that somehow cannot be released should not stop the launcher
        // closing. The mutex is abandoned when the process exits either way.
        _owner?.Join(TimeSpan.FromSeconds(1));
        _release.Dispose();
    }
}

/// <summary>
/// Caps how many copies of the game this launcher will start at once.
/// </summary>
/// <remarks>
/// The slot names carry no server identity, so the cap is per machine rather than per
/// server — which is what an operator asking for it means. Only this launcher touches
/// these names, so a game started some other way is not counted.
/// </remarks>
public static class InstanceLimit
{
    /// <summary>
    /// Session-global, so the cap holds across users on a shared machine. Needs the
    /// elevated token the application manifest already requires.
    /// </summary>
    public const string DefaultPrefix = @"Global\L38MultiSlot_";

    /// <summary>
    /// The cap actually in force.
    /// </summary>
    /// <remarks>
    /// Multi-instance switched off means one copy, not zero. Switched on, the configured
    /// number applies and zero means unlimited.
    /// </remarks>
    public static uint EffectiveLimit(bool multiInstanceAllowed, uint configuredLimit) =>
        multiInstanceAllowed ? configuredLimit : 1;

    /// <summary>
    /// Claims a slot, or returns null if every one is taken.
    /// </summary>
    /// <param name="limit">Zero for unlimited.</param>
    /// <param name="prefix">Slot name prefix; overridden only by tests.</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A slot that cannot be created must read as taken, never as free.")]
    public static InstanceSlot? TryAcquire(uint limit, string prefix = DefaultPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (limit == 0)
        {
            return InstanceSlot.Unlimited();
        }

        for (var index = 0u; index < limit; index++)
        {
            try
            {
                if (InstanceSlot.TryClaim($"{prefix}{index}") is { } slot)
                {
                    return slot;
                }
            }
            catch (Exception)
            {
                // A name that cannot be opened at all — a permissions problem, say — is
                // not the same as one that is taken, but treating it as free would let the
                // cap be bypassed by anyone who could provoke the failure.
            }
        }

        return null;
    }
}
