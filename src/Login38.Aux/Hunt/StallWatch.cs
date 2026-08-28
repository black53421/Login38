namespace Login38.Aux.Hunt;

/// <summary>
/// Notices when a target has stopped being worth chasing.
/// </summary>
/// <remarks>
/// <para>
/// The obvious test — "has the player moved?" — misfires the moment it matters most,
/// because a character standing next to a monster hitting it does not move either. What
/// separates the two is the client's own attack cooldown at
/// <see cref="HuntAddresses.NextAttackTick"/>, which it pushes forward on every blow it
/// lands.
/// </para>
/// <para>
/// So progress is either: the character got somewhere, or it hit something. Neither of
/// those for a while means the client is doing nothing about a target it says it has, and
/// the honest response is to pick a different one.
/// </para>
/// <para>
/// This matters more here than it would in a design that owned its pathfinding. The
/// collision grid holds map geometry only — nothing dynamic occupies a cell, which was
/// watched — so neither this nor the walk engine can see a body standing in the way. What
/// cannot be predicted has to be noticed.
/// </para>
/// </remarks>
internal sealed class StallWatch
{
    private int _x;
    private int _y;
    private uint _tick;
    private TimeSpan _since;
    private bool _watching;

    /// <summary>Starts again, for a target just picked.</summary>
    public void Reset() => _watching = false;

    /// <summary>
    /// Takes a reading, and says how long nothing has happened.
    /// </summary>
    /// <param name="x">Where the player is now.</param>
    /// <param name="y">Where the player is now.</param>
    /// <param name="tick">The client's attack cooldown, as it stands.</param>
    /// <param name="now">A reading off a monotonic clock, not the wall clock.</param>
    /// <remarks>
    /// Two callers want two different lengths out of the same reading — a short one that
    /// means the walk engine has gone quiet and is worth restarting, a long one that means
    /// this target is not worth any more time. Handing back the duration rather than a
    /// verdict is what lets both ask without either disturbing the other's baseline.
    /// </remarks>
    public TimeSpan Idle(int x, int y, uint tick, TimeSpan now)
    {
        if (!_watching || x != _x || y != _y || tick != _tick)
        {
            _watching = true;
            _x = x;
            _y = y;
            _tick = tick;
            _since = now;

            return TimeSpan.Zero;
        }

        return now - _since;
    }

    /// <inheritdoc cref="Idle"/>
    /// <param name="limit">How long nothing may happen.</param>
    public bool Stalled(int x, int y, uint tick, TimeSpan now, TimeSpan limit) =>
        Idle(x, y, tick, now) >= limit && limit > TimeSpan.Zero;
}
