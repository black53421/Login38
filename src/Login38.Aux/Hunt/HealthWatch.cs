namespace Login38.Aux.Hunt;

/// <summary>
/// Notices a target the character is swinging at and not hurting.
/// </summary>
/// <remarks>
/// <para>
/// The server drops an attack it will not allow and says nothing: no line of sight round a
/// corner, a monster still burrowed, a target it does not think is there. From inside the
/// client every one of those looks like a fight going well — the blow starts, the animation
/// plays, the cooldown moves on — so nothing the client does can be used to tell them apart.
/// </para>
/// <para>
/// What the server does send is the health bar, and the client keeps it at
/// <see cref="HuntAddresses.EntityHealth"/> as a percentage.
/// <see cref="HuntAddresses.HealthUnknown"/> means it has never sent one for this monster,
/// which is to say nothing has ever hurt it. So health that does not move is the one honest
/// answer to "is any of this working", and it is honest in the direction that matters: a
/// monster taking damage leaves the unknown value on the very first blow that lands, and
/// changes again on every blow after it.
/// </para>
/// <para>
/// That is what keeps this off an ordinary fight. It is not a timer on how long something is
/// taking to die — a tough monster is still losing health while it takes a long time about
/// it, and this is reset by every point of it. Only a target that cannot be hurt at all
/// holds one number.
/// </para>
/// </remarks>
internal sealed class HealthWatch
{
    private uint _id;
    private byte _health;
    private TimeSpan _since;
    private bool _watching;

    /// <summary>Starts again, for a target just picked or not yet in reach.</summary>
    public void Reset() => _watching = false;

    /// <summary>
    /// Takes a reading, and says how long this target's health has stood still.
    /// </summary>
    /// <param name="id">Which monster, so a change of target starts again.</param>
    /// <param name="health">Its health, as the client last heard it.</param>
    /// <param name="now">A reading off a monotonic clock, not the wall clock.</param>
    public TimeSpan Unchanged(uint id, byte health, TimeSpan now)
    {
        if (!_watching || id != _id || health != _health)
        {
            _watching = true;
            _id = id;
            _health = health;
            _since = now;

            return TimeSpan.Zero;
        }

        return now - _since;
    }
}
