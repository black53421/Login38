namespace Login38.Aux.Game;

/// <summary>
/// What is in the bag, for the helper window's dropdowns.
/// </summary>
/// <remarks>
/// <para>
/// The window offers the player a list of items to drink, to destroy, to dissolve. A list
/// out of a file is a list of everything the server has; a list out of the bag is a list of
/// what this character can actually use, which is the one worth picking from.
/// </para>
/// <para>
/// Filled by the helper loop rather than read by the window, because reading the bag means
/// walking the game's memory across a process boundary and the loop is already doing that
/// once a pass. Only while <see cref="Wanted"/> is set — a window that is not open costs
/// nothing.
/// </para>
/// </remarks>
public sealed class InventoryWatch
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
    /// What is in the bag, by the name the player would pick from a list.
    /// </summary>
    /// <remarks>
    /// Base names, without the stack count or the worn and wielded marks: those change
    /// while the game is played, and a setting that named one would stop matching the
    /// moment the player picked up one more of them.
    /// </remarks>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Hands over what the loop just read.</summary>
    public void Fill(IReadOnlyList<InventoryItem> bag)
    {
        ArgumentNullException.ThrowIfNull(bag);

        var names = new List<string>(bag.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in bag)
        {
            var name = item.BaseName;

            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        _names = names;
    }

    /// <summary>Forgets the bag, for when nothing is looking any more.</summary>
    public void Clear() => _names = [];
}
