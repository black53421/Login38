namespace Login38.Aux.Runtime;

/// <summary>
/// A one-way message from a task that has turned the hunt off to whatever is drawing the
/// checkbox that says it is on.
/// </summary>
/// <remarks>
/// <para>
/// The setting itself is what the hunt reads, and a task can write it directly; this exists
/// because the settings window holds its own copy and writes the whole lot back whenever the
/// player touches anything. Without a way of telling it, a character that teleported out on
/// low health starts hunting again the next time its owner adjusts a potion row — which is
/// the worst possible moment for it.
/// </para>
/// <para>
/// Taken once. Whoever reads it clears it, so a window that opens later does not act on an
/// escape from an hour ago, and a window that is not open at all costs nothing: the setting
/// is already off and the flag is simply never collected.
/// </para>
/// </remarks>
public sealed class HuntSwitch
{
    private volatile bool _off;

    /// <summary>Says the hunt has been turned off behind the window's back.</summary>
    public void TurnOff() => _off = true;

    /// <summary>Whether that has happened since this was last asked.</summary>
    public bool Taken()
    {
        if (!_off)
        {
            return false;
        }

        _off = false;

        return true;
    }
}
