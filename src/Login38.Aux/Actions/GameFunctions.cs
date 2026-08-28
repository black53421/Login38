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
    /// The object id every cast through the client's own path is aimed at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One global, not two. <c>FUN_0073C260</c> is where the client decides what a cast is
    /// for, and it reads this and nothing else: it looks the id up with a binary search over
    /// the object table (<c>FUN_005ADD70</c>), and on finding something that is not a player
    /// pushes the very same word into the cast packet as its target.
    /// </para>
    /// <para>
    /// Everything that goes wrong with a cast goes wrong when that lookup fails. A zero or a
    /// stale id sends the client down its fallback: whatever the mouse is over, if that is
    /// within fifteen tiles, and otherwise the "choose a target" cursor, which arms the skill
    /// and waits for a click that a helper is never going to make. From outside both look the
    /// same — the cast that went nowhere, or the one that went at something nobody chose.
    /// </para>
    /// <para>
    /// There was a second address here for a while, said to be the one attack skills used.
    /// It is not: nothing in the client reads or writes it, and the four bytes that would
    /// name it appear nowhere in the running process. Casts aimed through it landed on
    /// whatever this global happened to be holding.
    /// </para>
    /// </remarks>
    internal static readonly GameAddress CastTarget = new(0x0097C910);

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

    /// <summary>The character the client is playing.</summary>
    internal static readonly GameAddress LocalPlayer = new(0x00C2D2B8);

    /// <summary>
    /// One of the character's derived numbers, by kind.
    /// </summary>
    /// <remarks>
    /// Thiscall on the character, kind on the stack, and it cleans up after itself. Kind
    /// <see cref="CastSpeedKind"/> is the part of a cast's delay that belongs to the
    /// character rather than to the skill.
    /// </remarks>
    internal static readonly GameAddress DerivedStat = new(0x005AE530);

    /// <inheritdoc cref="DerivedStat"/>
    internal const byte CastSpeedKind = 0x13;

    /// <summary>The rest of a cast's delay, a signed word per skill id.</summary>
    internal static readonly GameAddress SkillDelays = new(0x0096D630);

    /// <summary>How long the cast about to go out takes, which the stamp below reads.</summary>
    internal static readonly GameAddress CastDelay = new(0x00C31310);

    /// <summary>
    /// Starts the client's own cast cooldown.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="CastDelay"/> and writes the three words the client shows a cooldown
    /// from: when this cast started, when the next one may go, and how long that is.
    /// </remarks>
    internal static readonly GameAddress StampCastCooldown = new(0x0073BB10);

    /// <summary>
    /// Starts the cooling of one skill's own icon.
    /// </summary>
    /// <remarks>
    /// Fastcall on the skill's record: marks it cooling, clears how far through it is, and
    /// stamps the tick it started at. Purely what the spell book draws — the cooldown that
    /// decides whether a cast may go out is <see cref="CastReadyAt"/>.
    /// </remarks>
    internal static readonly GameAddress StampIconCooldown = new(0x0073B950);

    /// <summary>The byte the client clears on a record before starting its icon.</summary>
    internal const uint RecordCooling = 0xBC;

    /// <summary>When the next cast may go out, on the client's own clock.</summary>
    internal static readonly GameAddress CastReadyAt = new(0x00C31314);

    /// <summary>
    /// Zero while the client's clock is <c>GetTickCount</c>, which is what makes the
    /// cooldown above comparable to a number this process can read.
    /// </summary>
    /// <remarks>
    /// Non-zero puts <c>FUN_00590EA0</c> on a performance counter scaled by two globals of
    /// its own, which is not worth reproducing — the rotation simply stops reading the
    /// cooldown and paces itself on the weapon, as it did before.
    /// </remarks>
    internal static readonly GameAddress CastClockIsTicks = new(0x00C2D208);

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
