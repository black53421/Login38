namespace Login38.Aux.Settings;

/// <summary>What a helper entry does, and to what.</summary>
public enum CastKind
{
    /// <summary>Use an item out of the bag, the way a double-click does.</summary>
    Item,

    /// <summary>Cast without naming a target; the server works it out from the session.</summary>
    NoSpec,

    /// <summary>Cast on the player.</summary>
    OnSelf,

    /// <summary>
    /// Cast on whatever the mouse is over.
    /// </summary>
    /// <remarks>
    /// For a skill this behaves as <see cref="NoSpec"/>: this client has no global anyone
    /// writes the hovered entity to, so there is nothing to read. For an item it is the
    /// half-manual path — the client enters its own target-picking mode and the player
    /// clicks.
    /// </remarks>
    HoverTarget,

    /// <summary>Use an item on a named player or summon, found by scanning entities.</summary>
    OnNamedEntity,

    /// <summary>Use an item on the player.</summary>
    /// <remarks>
    /// Not the same as <see cref="Item"/>. A scroll that needs a target only enters the
    /// client's picking mode when it is plainly used, and the server sees an unfinished
    /// cast; this sends the cast outright with the player as its target.
    /// </remarks>
    OnSelfItem,

    /// <summary>On an item that is currently worn — the first one, or one by name.</summary>
    OnInUseItem,

    /// <summary>On a weapon that is currently held — the first one, or one by name.</summary>
    OnWieldedItem,

    /// <summary>On an item with a given name, whatever state it is in.</summary>
    OnNamedItem,

    /// <summary>Destroy the item.</summary>
    DropItem,

    /// <summary>Print what the launcher can see about the entry, for working out a rule.</summary>
    Info,

    /// <summary>Press a function key.</summary>
    Key,

    /// <summary>The same, after a pause.</summary>
    DelayKey,
}

/// <summary>Which family an entry belongs to, which decides how it is delivered.</summary>
public enum EntryKind
{
    /// <summary>An item in the bag.</summary>
    Item,

    /// <summary>A spell the character knows.</summary>
    Skill,

    /// <summary>A key press.</summary>
    Key,
}

/// <summary>
/// Who or what a helper entry acts on.
/// </summary>
/// <param name="Kind">What it does.</param>
/// <param name="Target">
/// The name it acts on, for the kinds that take one. Null where the kind means "the first
/// one you find" — which is a different instruction from "one called nothing".
/// </param>
/// <param name="FunctionKey">
/// Which function key, for <see cref="CastKind.Key"/> and <see cref="CastKind.DelayKey"/>.
/// </param>
/// <remarks>
/// Parsed once, when the operator's settings are read, rather than every time a rule is
/// evaluated. The evaluation happens several times a second against a live game; re-reading
/// the same piece of text on each pass is work with a known answer.
/// </remarks>
public readonly record struct CastTarget(CastKind Kind, string? Target = null, byte FunctionKey = 0)
{
    /// <summary>The default: use the item.</summary>
    public static CastTarget Item { get; } = new(CastKind.Item);

    /// <summary>The lowest and highest function keys an entry can name.</summary>
    public const byte FirstFunctionKey = 1;

    /// <inheritdoc cref="FirstFunctionKey"/>
    public const byte LastFunctionKey = 12;

    /// <summary>On something named.</summary>
    public static CastTarget Named(CastKind kind, string target) => new(kind, target);

    /// <summary>On the first thing that matches, whatever it is called.</summary>
    public static CastTarget Any(CastKind kind) => new(kind);

    /// <summary>A function key press.</summary>
    public static CastTarget FunctionKeyPress(CastKind kind, byte key) => new(kind, null, key);
}
