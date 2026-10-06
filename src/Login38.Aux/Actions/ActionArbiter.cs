namespace Login38.Aux.Actions;

/// <summary>Priority of an action that enters the game client.</summary>
public enum PlayerActionPriority
{
    /// <summary>Automatic HP/MP recovery always wins the next free action slot.</summary>
    Potion = 0,

    /// <summary>Player-driven F1-F4 macros.</summary>
    Hotkey = 1,

    /// <summary>Timers, buffs and the rest of the helper.</summary>
    Normal = 2,
}

/// <summary>
/// Serializes client actions and gives a waiting higher-priority action the next slot.
/// </summary>
/// <remarks>
/// RemoteCall creates a thread inside the game. Two helper threads entering it at once were
/// never part of the original single-loop design, so the fast hotkey worker must share one
/// gate with every existing action. Priority is cooperative: an action already inside the
/// client is allowed to finish, then a waiting potion wins over a hotkey, and a hotkey wins
/// over ordinary helper work.
/// </remarks>
public sealed class ActionArbiter
{
    private readonly object _sync = new();
    private readonly int[] _waiting = new int[3];
    private readonly AsyncLocal<PlayerActionPriority?> _priority = new();
    private bool _busy;

    /// <summary>Temporarily assigns a priority to actions on the current execution flow.</summary>
    public IDisposable WithPriority(PlayerActionPriority priority)
    {
        var previous = _priority.Value;
        _priority.Value = priority;
        return new PriorityScope(this, previous);
    }

    /// <summary>Runs one client action under the shared priority gate.</summary>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var lease = Acquire(_priority.Value ?? PlayerActionPriority.Normal);
        action();
    }

    private Lease Acquire(PlayerActionPriority priority)
    {
        var index = (int)priority;

        lock (_sync)
        {
            _waiting[index]++;

            try
            {
                while (_busy || HigherPriorityWaiting(index))
                {
                    Monitor.Wait(_sync);
                }

                _busy = true;
            }
            finally
            {
                _waiting[index]--;
            }
        }

        return new Lease(this);
    }

    private bool HigherPriorityWaiting(int index)
    {
        for (var i = 0; i < index; i++)
        {
            if (_waiting[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private void Release()
    {
        lock (_sync)
        {
            _busy = false;
            Monitor.PulseAll(_sync);
        }
    }

    private sealed class Lease(ActionArbiter owner) : IDisposable
    {
        private ActionArbiter? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private sealed class PriorityScope(ActionArbiter owner, PlayerActionPriority? previous) : IDisposable
    {
        private ActionArbiter? _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } current)
            {
                current._priority.Value = previous;
            }
        }
    }
}
