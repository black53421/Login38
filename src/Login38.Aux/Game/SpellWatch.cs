namespace Login38.Aux.Game;

/// <summary>
/// What this character has learned, for the window's dropdowns.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Spells"/> already reads the book and already knows which entries act on a
/// target — the hunt asks it that on every cast. What it could not do is answer the window,
/// which has no game process to read through and no business acquiring one. This is the same
/// hand-over <see cref="InventoryWatch"/> does for the bag: the loop reads, this holds, the
/// window binds.
/// </para>
/// <para>
/// Names rather than <see cref="Spell"/> records. A settings file names a skill and the cast
/// looks it up again at the moment it fires, so a record cached here would be a second answer
/// to a question that already has one — and the wrong one, for a character who levelled the
/// skill while the window was open.
/// </para>
/// </remarks>
public sealed class SpellWatch
{
    private volatile IReadOnlyList<string> _names = [];
    private volatile bool _wanted;

    /// <summary>Whether anything is looking. Set by the window while it is open.</summary>
    public bool Wanted
    {
        get => _wanted;
        set => _wanted = value;
    }

    /// <summary>
    /// Every skill this character has learned that acts on a target.
    /// </summary>
    /// <remarks>
    /// Only those, because this fills the rotation's dropdown and a buff put in the rotation
    /// is a buff cast on the monster. Which ones they are is the client's own answer, out of
    /// its dispatch table — see <see cref="Spell.IsAttack"/>.
    /// </remarks>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Hands over what the loop just read.</summary>
    public void Fill(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        _names = [.. names.Order(StringComparer.Ordinal)];
    }

    /// <summary>Forgets the book, for when nothing is looking any more.</summary>
    public void Clear() => _names = [];
}
