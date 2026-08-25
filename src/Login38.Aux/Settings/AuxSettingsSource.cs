namespace Login38.Aux.Settings;

/// <summary>
/// The settings the helper loop reads and the window writes.
/// </summary>
/// <remarks>
/// <para>
/// One reader running ten times a second and one writer that moves a switch now and then.
/// The window edits its own copy and publishes it; the loop picks the new one up on its
/// next pass. There is nothing to restart and nothing to re-attach, which is the whole
/// point — a player changes a threshold and the next tick uses it.
/// </para>
/// <para>
/// Publishing a whole object rather than letting the window write into the one being read
/// is what makes the lists safe. A scalar being half-written does not matter; a list being
/// rebuilt while the loop walks it does.
/// </para>
/// </remarks>
public sealed class AuxSettingsSource
{
    private AuxSettings _current;

    public AuxSettingsSource(AuxSettings? initial = null) =>
        _current = (initial ?? new AuxSettings()).Normalise();

    /// <summary>What the loop should act on.</summary>
    public AuxSettings Current => Volatile.Read(ref _current);

    /// <summary>
    /// Raised after new settings are published, on whichever thread published them.
    /// </summary>
    /// <remarks>
    /// For the window, which has to show the settings of whichever character walked into
    /// the world — that happens several screens after the game started, and again every
    /// time the player comes back out and picks a different one. The helper loop does not
    /// subscribe: it reads <see cref="Current"/> on its next pass and needs no telling.
    /// </remarks>
    public event EventHandler<AuxSettings>? Published;

    /// <summary>Replaces them, for the next pass onwards.</summary>
    public void Publish(AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalised = settings.Normalise();
        Volatile.Write(ref _current, normalised);

        Published?.Invoke(this, normalised);
    }
}
