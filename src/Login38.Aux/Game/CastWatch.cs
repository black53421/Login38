using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Game;

/// <summary>
/// Watches the client's own chat window for the server saying a cast was refused.
/// </summary>
/// <remarks>
/// <para>
/// The rotation used to find out that a cast had not gone off by watching mana that never
/// moved, which takes a second and a half and says nothing about why. The client already
/// knows: the server answers a refused cast with a numbered message, and the client writes
/// the text of it into its chat window on the same tick it arrives.
/// </para>
/// <para>
/// Two structures make that readable. <c>FUN_00437500</c> is the client's add-a-line entry
/// point; the one that keeps the main window's history writes 0x126-byte records into an
/// array at <see cref="Lines"/> and advances <see cref="Cursor"/> by one per displayed line,
/// wrapping at <see cref="Slots"/>. The numbered messages themselves are a pointer array
/// whose head is the static global at <see cref="MessageTable"/>, one entry per id, each
/// pointing at Big5 text.
/// </para>
/// <para>
/// So nothing here is a hardcoded string: the text to look for is read out of the client at
/// the id the server used. A client whose messages are worded differently still works, and no
/// copy of the server's own table has to be shipped alongside the launcher.
/// </para>
/// <para>
/// One refusal is invisible and always will be: a cast from further than the skill reaches is
/// dropped by the server with no message at all. That one is answered by standing near enough
/// — see <c>SkillVolley</c> — and everything else is answered here.
/// </para>
/// </remarks>
public class CastWatch
{
    /// <summary>The head of the client's numbered-message pointer array.</summary>
    internal static readonly GameAddress MessageTable = new(0x00C2D0B4);

    /// <summary>The main chat window's line history.</summary>
    internal static readonly GameAddress Lines = new(0x00996D00);

    /// <summary>How many lines have been written into it, modulo <see cref="Slots"/>.</summary>
    internal static readonly GameAddress Cursor = new(0x00980EA0);

    /// <summary>The size of one line record.</summary>
    internal const int Stride = 0x126;

    /// <summary>How many the history holds before it wraps.</summary>
    internal const int Slots = 0x96;

    /// <summary>How much of a record is its text.</summary>
    internal const int LongestLine = 0x60;

    /// <summary>How much of a message in the table to read.</summary>
    internal const int LongestMessage = 160;

    /// <summary>
    /// How far back to look when the window has moved on a long way.
    /// </summary>
    /// <remarks>
    /// The hunt reads this five times a second, so the ordinary case is nought or one line. A
    /// long gap means the launcher was paused or the player was talking, and reading a hundred
    /// and fifty records to find out what the server said a minute ago is neither cheap nor
    /// useful.
    /// </remarks>
    internal const int MostAtOnce = 16;

    /// <summary>
    /// How much literal text a message needs before its own placeholders start.
    /// </summary>
    /// <remarks>
    /// A line the server fills in is only matched on the part it cannot change. Some messages
    /// are almost all placeholder — one of them is three literal bytes and two substitutions —
    /// and matching on those would answer every line in the window.
    /// </remarks>
    internal const int ShortestPrefix = 8;

    /// <summary>
    /// The messages that mean the cast did not happen, by the id the server sends.
    /// </summary>
    /// <remarks>
    /// Read off a running client rather than out of a server table. Deliberately not the whole
    /// family of "cannot use" lines: the ones about items and about teleporting are worded
    /// almost the same and standing the attack rotation down for either would be answering a
    /// message that was never about it.
    /// </remarks>
    internal static readonly (int Id, CastRefusal Why)[] Watched =
    [
        (278, CastRefusal.Mana),        // 因魔力不足而無法使用魔法。
        (279, CastRefusal.Health),      // 因體力不足而無法使用魔法。
        (280, CastRefusal.Blocked),     // 施咒失敗。
        (281, CastRefusal.Cancelled),   // 施咒取消。
        (285, CastRefusal.State),       // 在此狀態下無法使用魔法。
        (299, CastRefusal.Reagent),     // 施放魔法所需材料不足。
        (316, CastRefusal.Weight),      // 你攜帶太多物品，因此無法使用法術。
        (352, CastRefusal.Attribute),   // 若要使用這個法術，屬性必須成為 %0。
        (1003, CastRefusal.Unseen),     // 透明狀態無法使用的魔法。
    ];

    private const byte Terminator = 0;

    private const byte Placeholder = 0x25;

    private readonly ILogger<CastWatch> _logger;
    private readonly List<(byte[] Text, CastRefusal Why)> _known = [];

    private uint _table;
    private int? _at;

    public CastWatch(ILogger<CastWatch> logger) => _logger = logger;

    /// <summary>What the server last said about a cast, if it has said anything new.</summary>
    /// <returns>
    /// The reason on the newest line that carries one, or null when the window has not moved
    /// or has moved for some other reason.
    /// </returns>
    public virtual CastRefusal? Refused(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!Learn(process) || !process.TryRead<uint>(Cursor, out var written))
        {
            return null;
        }

        var cursor = (int)(written % Slots);

        // The first pass takes the mark and nothing else. Everything already in the window was
        // said before the hunt started, and standing the rotation down for a fight that ended
        // an hour ago would be answering the wrong question.
        if (_at is not { } was)
        {
            _at = cursor;

            return null;
        }

        _at = cursor;

        var moved = ((cursor - was) % Slots + Slots) % Slots;

        if (moved == 0)
        {
            return null;
        }

        CastRefusal? refused = null;
        Span<byte> line = stackalloc byte[LongestLine];

        // Oldest of the new lines first, so what comes back is the newest one carrying a
        // reason. Two in one pass means the older has already been answered by the newer.
        for (var back = Math.Min(moved, MostAtOnce); back >= 1; back--)
        {
            var slot = ((cursor - back) % Slots + Slots) % Slots;

            if (process.TryReadBytes(Lines + (slot * Stride), line) && Match(line) is { } why)
            {
                refused = why;
            }
        }

        return refused;
    }

    /// <summary>Forgets where the window was, for a client this has not watched before.</summary>
    public void Reset()
    {
        _at = null;
        _table = 0;
        _known.Clear();
    }

    /// <summary>Reads the text of the watched messages out of the client's own table.</summary>
    /// <returns>False while the table is not there, which it is not until the world loads.</returns>
    private bool Learn(RemoteProcess process)
    {
        if (!process.TryRead<uint>(MessageTable, out var table) || table == 0)
        {
            return false;
        }

        if (table == _table)
        {
            return _known.Count > 0;
        }

        _table = table;
        _known.Clear();

        foreach (var (id, why) in Watched)
        {
            if (Text(process, table, id) is { } text)
            {
                _known.Add((text, why));
            }
        }

        _logger.LogInformation(
            "read {Count} of the client's {Total} cast messages, so the hunt can tell why a "
            + "cast was refused", _known.Count, Watched.Length);

        return _known.Count > 0;
    }

    /// <summary>One message's literal text, or null when it is not one that can be matched.</summary>
    private static byte[]? Text(RemoteProcess process, uint table, int id)
    {
        if (!process.TryRead<uint>(new GameAddress(table) + (id * 4), out var at) || at == 0)
        {
            return null;
        }

        var raw = new byte[LongestMessage];

        if (!process.TryReadBytes(new GameAddress(at), raw))
        {
            return null;
        }

        var end = raw.AsSpan().IndexOf(Terminator);

        if (end <= 0)
        {
            return null;
        }

        // Only as far as the server's own first substitution: past that, the line the client
        // draws is not the line the table holds.
        var fill = raw.AsSpan(0, end).IndexOf(Placeholder);
        var length = fill < 0 ? end : fill;

        return length < ShortestPrefix ? null : raw[..length];
    }

    private CastRefusal? Match(ReadOnlySpan<byte> line)
    {
        foreach (var (text, why) in _known)
        {
            if (line.StartsWith(text))
            {
                return why;
            }
        }

        return null;
    }
}
