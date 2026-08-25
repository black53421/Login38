using System.Collections.Concurrent;
using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Aux.Toggles;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Game;

/// <summary>
/// Writes a colour onto every monster in sight, by how far above the player it is.
/// </summary>
/// <remarks>
/// <para>
/// The colour itself goes on the entity, at <c>+0x30</c>, which the client already reads
/// when it draws a name. What it does not do is draw it readably, which is what the detours
/// in <see cref="MonsterNameCave"/> are for — this half only decides what colour each thing
/// should be and remembers what was there before.
/// </para>
/// <para>
/// One walk of the heap per pass and no more. The reference reads three name pointers and
/// up to three ninety-six byte strings for every visible entity on every pass, to answer a
/// question — "is this me" — that three integer comparisons answer first. The pointers are
/// in the record the walk is already holding, and the names are only read for something
/// that is about to be coloured.
/// </para>
/// </remarks>
public sealed class MonsterScan
{
    /// <summary>How much of an entity has to be in hand to read all of the below.</summary>
    internal const int RecordLength = 0x90;

    /// <summary>The kind byte of something spawned into the world as a monster.</summary>
    internal const byte WorldMonster = 0x00;

    /// <summary>
    /// The highest level worth believing from a single byte on an entity.
    /// </summary>
    /// <remarks>
    /// The field is only a level on a monster. On anything else the byte holds something
    /// else entirely, and a bound is the cheapest way to throw out most of the nonsense
    /// before it reaches the colour rule.
    /// </remarks>
    internal const byte HighestTrustedLevel = 120;

    /// <summary>The client's own player status object.</summary>
    private static readonly GameAddress PlayerStatus = new(0x009A8CD0);

    /// <summary>Where in it the level is kept, behind a level of indirection.</summary>
    private const uint LevelField = 0x3FC;

    /// <summary>What the index into the obfuscation table is masked with.</summary>
    /// <remarks>
    /// The client keeps some of its own numbers as an index into a table plus a mask,
    /// rather than as the number. This is the mask on the index.
    /// </remarks>
    private const uint IndexMask = 0xC001_7921;

    /// <summary>The largest index the table is believed to hold.</summary>
    private const uint TableEntries = 0x1000;

    /// <summary>The client's pointer to the record for the person playing.</summary>
    private static readonly GameAddress LocalPlayer = new(0x00C2D2B8);

    private readonly SpriteTypes _sprites;
    private readonly ILegacyTextCodec _codec;
    private readonly ILogger<MonsterScan> _logger;

    /// <summary>What each entity's colour was before this launcher wrote one.</summary>
    /// <remarks>
    /// Keyed by address, which is what the client will reuse for something else the moment
    /// the monster is gone — so nothing here is written back without checking that what is
    /// at that address is still an entity.
    /// </remarks>
    private readonly ConcurrentDictionary<uint, ushort> _touched = new();

    public MonsterScan(ILegacyTextCodec codec, ILogger<MonsterScan> logger)
    {
        _codec = codec;
        _logger = logger;
        _sprites = new SpriteTypes(logger);
    }

    /// <summary>What one pass found.</summary>
    /// <param name="Seen">Entities standing in the world.</param>
    /// <param name="Coloured">How many of them this feature has an opinion about.</param>
    /// <param name="Level">The player's level, as the pass understood it.</param>
    public readonly record struct Report(int Seen, int Coloured, uint Level);

    /// <summary>
    /// Colours everything in the world that is worth colouring, and un-colours the rest.
    /// </summary>
    /// <param name="markerTable">Where to write the list the detours read.</param>
    public Report Pass(RemoteProcess process, GameAddress markerTable)
    {
        ArgumentNullException.ThrowIfNull(process);

        var clock = Stopwatch.StartNew();

        _sprites.Load(process);

        var me = Identity(process);
        var level = PlayerLevel(process);
        var seen = 0;
        List<GameAddress> coloured = [];

        HeapWalk.Records(
            process, HeapWalk.PlayerVtable, RecordLength, HeapWalk.HeapStart, HeapWalk.HeapEnd,
            (address, record) =>
            {
                var parsed = Snapshot.Parse(address, record);

                if (parsed is not { IsVisible: true })
                {
                    return;
                }

                var entity = parsed.Value;

                if (me.Is(entity))
                {
                    return;
                }

                seen++;

                var wanted = Wanted(entity, level);

                // Only now are the names worth the reads: whatever this is, it is about to
                // be coloured, and the one thing that must never be is the player.
                if (wanted is null || me.Is(Names(process, record)))
                {
                    Restore(process, entity);

                    return;
                }

                if (coloured.Count < MonsterNameCave.MarkerCapacity)
                {
                    coloured.Add(address);
                }

                if (entity.Colour != wanted.Value)
                {
                    _touched.TryAdd(address.Value, entity.Colour);
                    Write(process, address, wanted.Value);
                }
            });

        process.WriteBytes(markerTable, MonsterNameCave.MarkerTable(coloured));

        _logger.LogDebug(
            "Coloured {Coloured} of {Seen} entities for a level {Level} character in {Elapsed} ms",
            coloured.Count, seen, level, clock.ElapsedMilliseconds);

        return new Report(seen, coloured.Count, level);
    }

    /// <summary>
    /// Puts every colour this launcher wrote back the way it found it.
    /// </summary>
    /// <remarks>
    /// Each address is checked for still holding an entity first. A monster that died three
    /// seconds ago has had its record freed, and the client has very likely put something
    /// else there — writing two bytes of a remembered colour into the middle of it is how
    /// turning a feature off crashes a game.
    /// </remarks>
    /// <returns>How many were put back.</returns>
    public int RestoreAll(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var restored = 0;

        foreach (var (address, colour) in _touched)
        {
            var at = new GameAddress(address);

            if (process.TryRead<uint>(at, out var vtable) && vtable == HeapWalk.PlayerVtable.Value)
            {
                Write(process, at, colour);
                restored++;
            }
        }

        _touched.Clear();

        return restored;
    }

    /// <summary>Forgets what was written, without touching the game.</summary>
    /// <remarks>For a game that has already gone, where there is nothing to put back.</remarks>
    public void Forget() => _touched.Clear();

    /// <summary>
    /// Which colour an entity should be, or null for "leave it as the client drew it".
    /// </summary>
    internal ushort? Wanted(Snapshot entity, uint playerLevel)
    {
        if (entity.Level is not { } level)
        {
            return null;
        }

        return Colourable(entity, _sprites.For(entity.Sprite), _touched.ContainsKey(entity.Address.Value))
            ? MonsterColours.PatchFor(level, playerLevel)
            : null;
    }

    /// <summary>
    /// Whether the level byte on an entity may be read as a level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sprite table is the good answer: a sprite that draws a monster means the record
    /// is a monster and the byte is its level. Anything else the table names is not, whatever
    /// its object id says.
    /// </para>
    /// <para>
    /// Until the table has been found the object id is the fallback — everything spawned
    /// into a running world has a high one, and everything the map placed does not.
    /// </para>
    /// </remarks>
    /// <param name="spriteType">What the sprite draws, or null while the table is unknown.</param>
    /// <param name="alreadyOurs">
    /// Whether this launcher has already coloured it. A monster in combat has a different
    /// kind byte from one standing still, and losing its colour every time it is hit is
    /// worse than trusting a record this feature has already decided about.
    /// </param>
    internal static bool Colourable(Snapshot entity, byte? spriteType, bool alreadyOurs) => spriteType switch
    {
        SpriteTypes.Monster => entity.Kind == WorldMonster || alreadyOurs,
        not null => false,
        _ => entity.ServerId >= MonsterNameCave.LowestMonsterId
             && (entity.Kind == WorldMonster || alreadyOurs),
    };

    /// <summary>Puts one entity's colour back, if this launcher wrote it.</summary>
    private void Restore(RemoteProcess process, Snapshot entity)
    {
        if (_touched.TryRemove(entity.Address.Value, out var original) && entity.Colour != original)
        {
            Write(process, entity.Address, original);
        }
    }

    private static void Write(RemoteProcess process, GameAddress entity, ushort colour) =>
        process.Write(entity + Snapshot.ColourOffset, colour);

    /// <summary>
    /// Reads the player's level.
    /// </summary>
    /// <remarks>
    /// The client keeps it as an index into a table of masked values rather than as a
    /// number, so reading it is four reads and two exclusive ors. When any of them does not
    /// hold up, the level is worked out from total experience instead — which is a reading
    /// this launcher already has, and is never more than a level out.
    /// </remarks>
    internal static uint PlayerLevel(RemoteProcess process)
    {
        if (Obfuscated(process) is { } level)
        {
            return level;
        }

        var total = Experience.Read(process) ?? 0;

        return Experience.LevelOf(total);
    }

    private static uint? Obfuscated(RemoteProcess process)
    {
        if (!process.TryRead<uint>(PlayerStatus, out var status) || status == 0
            || !process.TryRead<uint>(new GameAddress(status) + LevelField, out var slot) || slot == 0
            || !process.TryRead<uint>(new GameAddress(slot), out var encoded)
            || !process.TryRead<uint>(new GameAddress(slot) + 4u, out var table)
            || !process.TryRead<uint>(new GameAddress(slot) + 8u, out var mask))
        {
            return null;
        }

        var index = encoded ^ IndexMask;

        if (index >= TableEntries
            || !process.TryRead<uint>(new GameAddress(table) + (index * 4), out var stored))
        {
            return null;
        }

        var level = stored ^ mask;

        return level is >= 1 and <= HighestTrustedLevel ? level : null;
    }

    /// <summary>Reads whatever the client knows about the character being played.</summary>
    private Player Identity(RemoteProcess process)
    {
        if (!process.TryRead<uint>(LocalPlayer, out var record) || record == 0)
        {
            return new Player(0, 0, Self(process), []);
        }

        Span<byte> raw = stackalloc byte[RecordLength];
        var read = process.TryReadBytes(new GameAddress(record), raw);

        return new Player(
            record,
            read ? BitConverter.ToUInt32(raw[Snapshot.ServerIdOffset..]) : 0,
            Self(process),
            read ? Names(process, raw) : []);
    }

    private static uint Self(RemoteProcess process) =>
        process.TryRead<uint>(GameFunctions.SelfId, out var id) ? id : 0;

    /// <summary>
    /// Follows an entity's name pointers, which are already in the record.
    /// </summary>
    /// <remarks>
    /// The three fields are the same ones <see cref="EntityScan"/> reads, and for the same
    /// reason: which of them is filled in depends on what the thing is.
    /// </remarks>
    private List<string> Names(RemoteProcess process, ReadOnlySpan<byte> record)
    {
        List<string> names = [];
        Span<byte> buffer = stackalloc byte[Snapshot.NameLength];

        foreach (var field in EntityScan.NameFields)
        {
            var pointer = BitConverter.ToUInt32(record[(int)field..]);

            if (pointer == 0 || !process.TryReadBytes(new GameAddress(pointer), buffer))
            {
                continue;
            }

            if (_codec.DecodeNullTerminated(buffer) is { Length: > 0 } name
                && name.Trim() is { Length: > 0 } trimmed
                && !names.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(trimmed);
            }
        }

        return names;
    }

    /// <summary>Everything the client will answer "that is you" to.</summary>
    /// <param name="Record">The address of the player's own entity.</param>
    /// <param name="TargetId">The id on that record, which is a client-side number.</param>
    /// <param name="SelfId">And the one the client aims at when it means the player.</param>
    /// <param name="Names">Whatever the record is called.</param>
    internal readonly record struct Player(
        uint Record, uint TargetId, uint SelfId, IReadOnlyList<string> Names)
    {
        /// <summary>Whether an entity is the character being played.</summary>
        internal bool Is(Snapshot entity) =>
            (Record != 0 && entity.Address.Value == Record)
            || (TargetId != 0 && entity.ServerId == TargetId)
            || (SelfId != 0 && entity.ServerId == SelfId);

        /// <summary>The same question again, for a record that got past the three above.</summary>
        internal bool Is(IReadOnlyList<string> names)
        {
            var mine = Names;

            return mine.Count > 0
                && names.Any(name => mine.Contains(name, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>One world entity, as much of it as this feature looks at.</summary>
    /// <param name="Address">Where its record is.</param>
    /// <param name="ServerId">The id the server knows it by.</param>
    /// <param name="Kind">What the client thinks it is.</param>
    /// <param name="Sprite">Which sprite draws it.</param>
    /// <param name="Map">The map it is standing on, which is zero for something not in the world.</param>
    /// <param name="NamePointer">Its name, or zero.</param>
    /// <param name="Colour">The colour its name is currently drawn in.</param>
    /// <param name="LevelByte">The byte that is a level on a monster and something else otherwise.</param>
    public readonly record struct Snapshot(
        GameAddress Address,
        uint ServerId,
        byte Kind,
        ushort Sprite,
        uint Map,
        uint NamePointer,
        ushort Colour,
        byte LevelByte)
    {
        internal const int ServerIdOffset = 0x0C;
        internal const int KindOffset = 0x14;
        internal const int SpriteOffset = 0x18;
        internal const int ColourOffset = 0x30;
        internal const int LevelOffset = 0x5A;
        internal const int NameOffset = 0x60;
        internal const int MapOffset = 0x80;

        /// <summary>A name is at most sixteen characters, which is thirty-two bytes.</summary>
        internal const int NameLength = 64;

        /// <summary>Whether this is something actually standing in the world.</summary>
        /// <remarks>
        /// The client keeps freed entity records around and reuses them, so a record with
        /// this vtable is not by itself something on screen.
        /// </remarks>
        public bool IsVisible => Sprite != 0 && Map != 0 && NamePointer != 0;

        /// <summary>Its level, where the byte holds a believable one.</summary>
        public uint? Level => LevelByte is >= 1 and <= HighestTrustedLevel ? LevelByte : null;

        /// <summary>
        /// Reads one out of a record the heap walk is already holding.
        /// </summary>
        /// <remarks>
        /// No vtable check: the walk only ever calls back for records that begin with it,
        /// and checking it again would be reading the same four bytes twice.
        /// </remarks>
        public static Snapshot? Parse(GameAddress address, ReadOnlySpan<byte> record) =>
            record.Length < RecordLength
                ? null
                : new Snapshot(
                    address,
                    BitConverter.ToUInt32(record[ServerIdOffset..]),
                    record[KindOffset],
                    BitConverter.ToUInt16(record[SpriteOffset..]),
                    BitConverter.ToUInt32(record[MapOffset..]),
                    BitConverter.ToUInt32(record[NameOffset..]),
                    BitConverter.ToUInt16(record[ColourOffset..]),
                    record[LevelOffset]);
    }
}
