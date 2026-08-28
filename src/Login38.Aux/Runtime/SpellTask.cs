using Login38.Aux.Game;

namespace Login38.Aux.Runtime;

/// <summary>
/// Keeps <see cref="SpellWatch"/> up to date while the helper window is open.
/// </summary>
/// <remarks>
/// <para>
/// Once a second, and only while something is looking. A spell book changes when a character
/// learns something, which is rarely, but it also changes completely when the player swaps
/// character — and <see cref="Spells"/> notices that by itself, from the client's pointer to
/// the book moving, so a pass where nothing has changed costs one read of that pointer.
/// </para>
/// <para>
/// Out of the world the book reads as the last character's, which is the one thing worth
/// refusing: offering skills this character has not learned is worse than offering none, as
/// the player picks one and the hunt then says it was never learned.
/// </para>
/// </remarks>
public sealed class SpellTask : IAuxTask
{
    private readonly SpellWatch _watch;
    private readonly Spells _spells;

    public SpellTask(SpellWatch watch, Spells spells)
    {
        _watch = watch;
        _spells = spells;
    }

    /// <inheritdoc/>
    public string Name => "spells";

    /// <inheritdoc/>
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_watch.Wanted || !context.IsInWorld)
        {
            _watch.Clear();

            return;
        }

        _watch.Fill(_spells.Attacks(context.Process));
    }
}
