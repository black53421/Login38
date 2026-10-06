namespace Login38.Aux.Runtime;

/// <summary>
/// Whether the player has started the helper for this game.
/// </summary>
/// <remarks>
/// <para>
/// Off until somebody presses Home. Launching a client is not asking for a helper — a
/// player may be logging in to look at something, or playing a character they do not want
/// drinking potions on their behalf, and a launcher that starts pouring the moment the
/// world loads has made that decision for them.
/// </para>
/// <para>
/// One per game rather than one per launcher. A launcher driving two clients has two of
/// these, and Home belongs to whichever client is in front.
/// </para>
/// <para>
/// Written from the helper loop and also read by the optional F-key worker. The on/off bit
/// therefore uses volatile access; the event is still raised by the helper loop, and a
/// window subscriber owns getting it onto the thread its windows live on.
/// </para>
/// </remarks>
public sealed class HelperSwitch
{
    private int _isOn;

    /// <summary>Whether the helper's features are running.</summary>
    public bool IsOn => Volatile.Read(ref _isOn) != 0;

    /// <summary>Whether it has ever been on during this game.</summary>
    /// <remarks>
    /// What makes the first Home do two things and every later one do one. A player
    /// reaching for the settings for the first time is a player who wants the helper, so
    /// that press starts it as well as showing them; after that the settings are just the
    /// settings, and Insert is the switch.
    /// </remarks>
    public bool HasBeenOn { get; private set; }

    /// <summary>Raised on the helper's loop thread when the switch moves.</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Turns it on if it was off, and off if it was on.</summary>
    /// <returns>Where it now is.</returns>
    public bool Toggle()
    {
        var isOn = !IsOn;
        Volatile.Write(ref _isOn, isOn ? 1 : 0);
        HasBeenOn |= isOn;

        Changed?.Invoke(this, isOn);

        return isOn;
    }
}
