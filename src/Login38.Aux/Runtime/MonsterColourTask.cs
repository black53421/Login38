using Login38.Aux.Game;
using Login38.Aux.Toggles;

namespace Login38.Aux.Runtime;

/// <summary>
/// Keeps the monster colours current as the world moves.
/// </summary>
/// <remarks>
/// <para>
/// The detours are put in once by <see cref="MonsterColourToggle"/> and stay. What has to
/// happen again and again is deciding what colour each thing on screen should be, which
/// means walking the client's heap — so this is the most expensive task in the loop, and
/// the only one that does nothing at all unless its switch is on.
/// </para>
/// <para>
/// The reference gives this its own operating-system thread with its own sleep, which is
/// the eleventh in that process. Here it is a pass of the loop like every other, which also
/// means it stops when the game does rather than spinning on a handle that is gone.
/// </para>
/// </remarks>
public sealed class MonsterColourTask : IAuxTask, IAuxTaskShutdown
{
    private readonly MonsterColourToggle _toggle;
    private readonly MonsterScan _scan;

    public MonsterColourTask(MonsterColourToggle toggle, MonsterScan scan)
    {
        _toggle = toggle;
        _scan = scan;
    }

    /// <inheritdoc/>
    public string Name => "monster-colours";

    /// <summary>Just under a second, as the reference's own scanner ran.</summary>
    /// <remarks>
    /// Slower than anything else in the loop on purpose. A monster's level does not change,
    /// and the only thing a shorter interval buys is that something that has just walked
    /// into view is coloured sooner.
    /// </remarks>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(900);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Before a character is in the world there is nothing standing in it, and a walk of
        // the whole heap to find that out is the most expensive way of asking.
        if (!context.Settings.Misc.MonsterLevelColour || !context.IsInWorld)
        {
            return;
        }

        if (_toggle.MarkerTable is { } markers)
        {
            _scan.Pass(context.Process, markers);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only forgets. The colours live in the game's own heap, and by the time this runs
    /// there is either no game to put them back in or a toggle that has already done it.
    /// </remarks>
    public void Stopping() => _scan.Forget();
}
