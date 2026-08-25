using Login38.Aux.Toggles;

namespace Login38.Aux.Runtime;

/// <summary>
/// Keeps the game matching the switches in the helper window.
/// </summary>
/// <remarks>
/// One task for all of them rather than one per toggle. They do the same thing on the same
/// cadence and none of them is expensive — the reference gave each its own operating-system
/// thread, its own sleep and its own copy of the settings lock, for a comparison and
/// occasionally one byte.
/// </remarks>
public sealed class ToggleTask : IAuxTask
{
    private readonly IReadOnlyList<IGameToggle> _toggles;
    private readonly HelperSwitch _switch;

    public ToggleTask(IEnumerable<IGameToggle> toggles, HelperSwitch helperSwitch)
    {
        _toggles = [.. toggles];
        _switch = helperSwitch;
    }

    /// <inheritdoc/>
    public string Name => "toggles";

    /// <summary>
    /// Runs while the helper is off, unlike the features.
    /// </summary>
    /// <remarks>
    /// Because switching off is itself something to do. A toggle left applied when the
    /// player stopped the helper is the helper still acting on a game it was told to leave
    /// alone, and a task that is not called cannot put anything back.
    /// </remarks>
    public bool RunsWhileOff => true;

    /// <summary>Twice a second, as the reference's own sync loops ran.</summary>
    /// <remarks>
    /// Fast enough that a switch feels immediate, and slow enough that a toggle whose
    /// address has moved is not being retried ten times a second for a whole session.
    /// </remarks>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var toggle in _toggles)
        {
            // Every pass, not only when the switch moves: some of these write data the
            // client rewrites for its own reasons, and putting it back is the point.
            //
            // A helper the player has not started wants none of them, whatever the boxes
            // in the window say — those are what it will do once it is running.
            if (_switch.IsOn)
            {
                toggle.Apply(context.Process, context.Settings);
            }
            else
            {
                toggle.Apply(context.Process, wanted: false);
            }
        }
    }
}
