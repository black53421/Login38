using Login38.Interop;

namespace Login38.Aux.Actions;

/// <summary>
/// The client's own entry points that the helper calls, and the opcodes it sends.
/// </summary>
/// <remarks>
/// Every one of these was found by watching a real client do the thing by hand. That is
/// worth stating because it is the only reason to trust them: they are not derived from
/// anything, and a client update moves all of them at once.
/// </remarks>
internal static class GameFunctions
{
    /// <summary>
    /// <c>void __cdecl UseItem(item* entry)</c> — what a double-click in the bag reaches.
    /// </summary>
    /// <remarks>Prologue <c>55 8B EC 83 EC 0C</c>, and twenty-odd callers inside the client.</remarks>
    internal static readonly GameAddress UseItem = new(0x004B3EE0);

    /// <summary>
    /// <c>void __cdecl SendPacketData(const char* format, ...)</c>.
    /// </summary>
    /// <remarks>
    /// The format string says what the arguments are: <c>c</c> a byte, <c>h</c> a word,
    /// <c>d</c> a dword, <c>s</c> a NUL-terminated string. Every one occupies a full stack
    /// slot whatever its width, because this is a variadic C function.
    /// </remarks>
    internal static readonly GameAddress SendPacketData = new(0x00580E50);

    /// <summary>
    /// <c>void __thiscall SpellBook::Cast(uint packed, uint manual)</c>, <c>ret 8</c>.
    /// </summary>
    /// <remarks>
    /// The path a player takes by opening the spell book and clicking. It checks mana and
    /// range, fills in the casting state the animation reads, and sends the request — none
    /// of which happens if the lower <c>do_cast</c> is called directly.
    /// </remarks>
    internal static readonly GameAddress SpellBookCast = new(0x0073ECE0);

    /// <summary>The player's spell book, filled in on entering the world.</summary>
    internal static readonly GameAddress SpellBookPointer = new(0x00C31324);

    /// <summary>
    /// What an item or self-cast is aimed at.
    /// </summary>
    /// <remarks>
    /// The client writes this as the mouse moves over things. There are two of these and
    /// they are not interchangeable — see <see cref="AttackTarget"/>.
    /// </remarks>
    internal static readonly GameAddress CastTarget = new(0x0097C910);

    /// <summary>
    /// What an attack skill is aimed at.
    /// </summary>
    /// <remarks>
    /// A separate global because attack skills go out as a longer packet carrying the
    /// target's position as well. Writing the other one instead sends the cast down the
    /// item path, which cannot find a monster in the bag and puts up "choose a target".
    /// </remarks>
    internal static readonly GameAddress AttackTarget = new(0x0097C90C);

    /// <summary>The player's own object id, filled in on entering the world.</summary>
    internal static readonly GameAddress SelfId = new(0x00ABF4B4);

    /// <summary>
    /// The client's own <c>"cccd"</c>, in read-only data.
    /// </summary>
    /// <remarks>
    /// Used rather than an embedded copy because the client already has one and it is what
    /// its own cast path passes. One less thing in the cave.
    /// </remarks>
    internal static readonly GameAddress SkillFormat = new(0x008EF028);

    /// <summary>Use an item, and also "use this on that" for scrolls and tools.</summary>
    internal const byte UseItemOpcode = 0xA4;

    /// <summary>Throw an item away.</summary>
    internal const byte DeleteItemOpcode = 0x8A;

    /// <summary>Answer a teleport the server has offered.</summary>
    internal const byte TeleportOpcode = 0x34;

    /// <summary>Say something.</summary>
    internal const byte ChatOpcode = 0x88;

    /// <summary>Cast a skill.</summary>
    internal const byte SkillOpcode = 0x06;

    /// <summary>
    /// The second argument to <see cref="SpellBookCast"/>: 1 means a player did this.
    /// </summary>
    /// <remarks>Zero is what the client's own casting loop passes itself afterwards.</remarks>
    internal const uint ManualCast = 1;
}

/// <summary>Where a message goes.</summary>
public enum ChatChannel : byte
{
    /// <summary>To whoever is standing nearby.</summary>
    Normal = 0x00,

    /// <summary>To everyone on the map.</summary>
    Shout = 0x02,
}
