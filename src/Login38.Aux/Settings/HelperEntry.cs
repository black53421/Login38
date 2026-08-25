namespace Login38.Aux.Settings;

/// <summary>
/// One line of an operator's helper list.
/// </summary>
/// <param name="StateId">
/// Which of the client's own status flags says this is already active, or -1 for an entry
/// that has none. The helper skips an entry whose flag is set, which is what stops it from
/// recasting a spell the character is already under.
/// </param>
/// <param name="Name">The item or spell, as the player would name it.</param>
/// <param name="Kind">Which family it belongs to.</param>
/// <param name="Cast">Who or what it acts on.</param>
public sealed record HelperEntry(int StateId, string Name, EntryKind Kind, CastTarget Cast)
{
    /// <summary>The value that means the entry has no status flag to check.</summary>
    public const int NoState = -1;

    /// <summary>An entry that just uses an item.</summary>
    public static HelperEntry ForItem(string name, int stateId = NoState) =>
        new(stateId, name, EntryKind.Item, CastTarget.Item);

    /// <inheritdoc/>
    public override string ToString() => HelperEntrySyntax.ToCommandText(this);
}
