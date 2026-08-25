using System.Collections.Frozen;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Game;

/// <summary>One skill, as the client knows it.</summary>
/// <param name="Packed">The id that goes into a cast packet.</param>
/// <param name="Range">How far it reaches, in tiles. Zero for anything cast on oneself.</param>
/// <param name="Handler">Which of the client's casting handlers it goes through.</param>
public readonly record struct Spell(uint Packed, uint Range, byte Handler)
{
    /// <summary>Whether casting this does something to a target rather than to the caster.</summary>
    /// <remarks>
    /// From the client's own dispatch table rather than from the name: skills sharing a
    /// handler byte go through the same code, and the handlers that damage or weaken a
    /// target are a known set. A few read as buffs despite sounding aggressive; covering
    /// the whole table matters more than those.
    /// </remarks>
    public bool IsAttack => AttackHandlers.Contains(Handler);

    /// <summary>
    /// The handlers that act on a target.
    /// </summary>
    /// <remarks>
    /// Established by dumping the client's 220-byte dispatch table against its own spell
    /// list and reading off which skills share each handler. What is excluded: healing,
    /// the large self-buff group, teleports, weapon and armour enchants, summons, the
    /// passives, and the transformations.
    /// </remarks>
    private static readonly FrozenSet<byte> AttackHandlers = new byte[]
    {
        0x02, 0x05, 0x06, 0x08, 0x09, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12,
        0x13, 0x15, 0x16, 0x18, 0x1A, 0x1D, 0x24, 0x26, 0x28, 0x29, 0x2A, 0x2D, 0x2E,
        0x2F, 0x31, 0x32, 0x33, 0x34, 0x35, 0x37, 0x38, 0x3A, 0x3B, 0x3C,
    }.ToFrozenSet();
}

/// <summary>
/// What skills the game knows about, by the name the player writes.
/// </summary>
/// <remarks>
/// <para>
/// Two tables, and both are needed. The spell book is what this character has actually
/// learned, at the level they learned it — that is the id a cast should use. The client's
/// own catalogue holds every skill in the game at level one, and is the fallback for a name
/// the character has not learned, which is the difference between "nothing happens" and
/// "the server says you do not know that".
/// </para>
/// <para>
/// The book belongs to a character and is rebuilt when the character changes, which is
/// noticed by the client's book pointer moving. The catalogue is a fixed table in the
/// client's own data and is built once.
/// </para>
/// </remarks>
public class Spells
{
    /// <summary>The player's spell book, filled in on entering the world.</summary>
    internal static readonly GameAddress BookPointer = new(0x00C31324);

    /// <summary>How many skills the player has, and where they are.</summary>
    internal const uint BookCount = 0x2C;

    /// <summary>The array of skill records.</summary>
    internal const uint BookArray = 0x58;

    /// <summary>A count past this means the structure has been read wrong.</summary>
    internal const uint MostSkillsAnyoneHas = 1024;

    /// <summary>The client's catalogue of every skill.</summary>
    internal static readonly GameAddress CataloguePointer = new(0x009A8ED4);

    /// <summary>How far into the catalogue to look. The client has about 120.</summary>
    internal const uint CatalogueLength = 256;

    /// <summary>The client's casting dispatch table, one byte per skill.</summary>
    internal static readonly GameAddress HandlerTable = new(0x007404AC);

    /// <summary>Its length, from the bound the client's own casting code checks.</summary>
    internal const int HandlerTableLength = 0xDC;

    /// <summary>How much of a skill record's name to read.</summary>
    private const int NameLength = 64;

    /// <summary>Where a record keeps its packed id.</summary>
    private const uint RecordPacked = 0x04;

    /// <summary>And its name.</summary>
    private const uint RecordName = 0x0C;

    private readonly ILegacyTextCodec _codec;
    private readonly ILogger _logger;

    private Dictionary<string, Spell> _book = [];
    private uint _bookPointer;
    private Dictionary<string, uint>? _catalogue;

    public Spells(ILegacyTextCodec codec, ILogger<Spells> logger)
    {
        _codec = codec;
        _logger = logger;
    }

    /// <summary>How many skills the character has learned, as last read.</summary>
    public int Learned => _book.Count;

    /// <summary>
    /// Finds the id to cast a skill by, given the name the player wrote.
    /// </summary>
    /// <remarks>
    /// The character's own version first, so a skill learned at a higher level is cast at
    /// that level.
    /// </remarks>
    public virtual uint? Find(RemoteProcess process, string name)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrEmpty(name);

        Refresh(process);

        if (_book.TryGetValue(name, out var learned))
        {
            return learned.Packed;
        }

        return _catalogue is not null && _catalogue.TryGetValue(name, out var known) ? known : null;
    }

    /// <summary>What the character has learned about a skill, if they have.</summary>
    public virtual Spell? Learn(RemoteProcess process, string name)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrEmpty(name);

        Refresh(process);

        return _book.TryGetValue(name, out var spell) ? spell : null;
    }

    /// <summary>Every skill the character has learned that acts on a target.</summary>
    public IEnumerable<string> AttackNames =>
        _book.Where(pair => pair.Value.IsAttack).Select(pair => pair.Key);

    /// <summary>Names close to one that was not found, for a player who has mistyped.</summary>
    public IEnumerable<string> Near(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return _book.Keys.Concat(_catalogue?.Keys ?? Enumerable.Empty<string>())
            .Where(known => known.StartsWith(name[0]) || known.Contains(name, StringComparison.Ordinal))
            .Distinct()
            .Take(8);
    }

    /// <summary>
    /// Rereads whatever has gone out of date.
    /// </summary>
    /// <remarks>
    /// The book is rebuilt when the client's pointer to it moves, which is what happens
    /// when the player changes character. The catalogue is a fixed table and is built once.
    /// </remarks>
    private void Refresh(RemoteProcess process)
    {
        var pointer = process.TryRead<uint>(BookPointer, out var current) ? current : 0;

        if (pointer != _bookPointer || (pointer != 0 && _book.Count == 0))
        {
            _book = pointer == 0 ? [] : ReadBook(process, pointer);
            _bookPointer = pointer;
        }

        _catalogue ??= ReadCatalogue(process);
    }

    private Dictionary<string, Spell> ReadBook(RemoteProcess process, uint book)
    {
        try
        {
            var count = process.Read<uint>(new GameAddress(book + BookCount));
            var array = process.Read<uint>(new GameAddress(book + BookArray));

            if (count == 0 || array == 0)
            {
                return [];
            }

            if (count > MostSkillsAnyoneHas)
            {
                _logger.LogWarning(
                    "The spell book says it holds {Count} skills, which is not a spell book", count);

                return [];
            }

            // One read for the whole dispatch table rather than a byte per skill. Failing
            // to read it leaves every skill looking like a buff, which shows up as an empty
            // list of attack skills — visible, rather than quietly letting buffs through.
            var handlers = new byte[HandlerTableLength];

            if (!process.TryReadBytes(HandlerTable, handlers))
            {
                _logger.LogWarning(
                    "The client's casting table at {Table} could not be read; no skill will read as an attack",
                    HandlerTable);
            }

            var spells = new Dictionary<string, Spell>(StringComparer.Ordinal);

            foreach (var record in Records(process, array, (int)count))
            {
                var packed = process.Read<uint>(new GameAddress(record + RecordPacked));

                if (BookName(process, record) is not { } full)
                {
                    continue;
                }

                var name = SpellNames.StripSuffix(full);

                if (name.Length == 0)
                {
                    continue;
                }

                var spell = new Spell(
                    packed,
                    SpellNames.RangeFrom(full) ?? 0,
                    packed < handlers.Length ? handlers[packed] : (byte)0);

                // The same skill appears once per level learned. The highest id is the
                // highest level, which is the one the player means.
                if (!spells.TryGetValue(name, out var existing) || packed > existing.Packed)
                {
                    spells[name] = spell;
                }
            }

            _logger.LogInformation(
                "The character has learned {Count} skills (spell book at {Book:X8})", spells.Count, book);

            return spells;
        }
        catch (GameProcessException e)
        {
            _logger.LogInformation(e, "The spell book could not be read; the character may not be in yet");

            return [];
        }
    }

    private Dictionary<string, uint>? ReadCatalogue(RemoteProcess process)
    {
        try
        {
            var array = process.Read<uint>(CataloguePointer);

            if (array == 0)
            {
                // Filled in on entering the world. There is nothing to cache yet, and
                // returning null rather than an empty table means this is tried again.
                return null;
            }

            var known = new Dictionary<string, uint>(StringComparer.Ordinal);

            for (uint packed = 0; packed < CatalogueLength; packed++)
            {
                if (!process.TryRead<uint>(new GameAddress(array + (packed * 4)), out var record) || record == 0)
                {
                    continue;
                }

                if (CatalogueName(process, record) is not { } full)
                {
                    continue;
                }

                var name = SpellNames.StripSuffix(full);

                // The lowest id is level one, which is what a player writing a bare name
                // means when they have not learned it.
                if (name.Length > 0)
                {
                    known.TryAdd(name, packed);
                }
            }

            _logger.LogInformation("The client knows {Count} skills", known.Count);

            return known;
        }
        catch (GameProcessException e)
        {
            _logger.LogInformation(e, "The client's skill list could not be read");

            return null;
        }
    }

    /// <summary>The non-null records in a skill array.</summary>
    private static IEnumerable<uint> Records(RemoteProcess process, uint array, int count)
    {
        var pointers = process.ReadBytes(new GameAddress(array), count * 4);

        for (var i = 0; i < count; i++)
        {
            var record = BitConverter.ToUInt32(pointers, i * 4);

            if (record != 0)
            {
                yield return record;
            }
        }
    }

    /// <summary>
    /// A learned skill's name, which the record points at.
    /// </summary>
    /// <remarks>
    /// The two tables do not hold names the same way, which is worth being explicit about:
    /// a book record has a pointer at <c>+0x0C</c>, a catalogue record starts with the text.
    /// </remarks>
    private string? BookName(RemoteProcess process, uint record) =>
        process.TryRead<uint>(new GameAddress(record + RecordName), out var text) && text != 0
            ? Text(process, text)
            : null;

    /// <summary>A catalogued skill's name, which the record begins with.</summary>
    private string? CatalogueName(RemoteProcess process, uint record) => Text(process, record);

    private string? Text(RemoteProcess process, uint at)
    {
        var buffer = new byte[NameLength];

        return process.TryReadBytes(new GameAddress(at), buffer)
               && _codec.DecodeNullTerminated(buffer) is { Length: > 0 } name
            ? name
            : null;
    }
}
