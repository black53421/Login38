using System.Diagnostics;
using Login38.Aux.Actions;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Game;

/// <summary>Something standing in the world, found in the client's heap.</summary>
/// <param name="Address">Where its record is, which is only good until it moves.</param>
/// <param name="Id">The id a packet aims at.</param>
/// <param name="Name">The name that matched, as the client stores it.</param>
public readonly record struct Entity(GameAddress Address, uint Id, string Name);

/// <summary>
/// Turns a character's name into an id a packet can aim at.
/// </summary>
/// <remarks>
/// <para>
/// For <c>/IT=名字</c> — using a scroll on somebody else. The client keeps no index from
/// names to objects, so the only way is to walk its heap looking for records whose first
/// word is the player class's vtable pointer, and read the names out of the ones found.
/// </para>
/// <para>
/// Everything here was established by dumping a running client: the vtable covers other
/// players, this player's own avatar, and summons alike. Three fields can hold a name and
/// which one is filled depends on what the thing is, so all three are tried.
/// </para>
/// </remarks>
public class EntityScan
{
    /// <summary>
    /// The player class's vtable, which every character, avatar and summon record begins
    /// with. This is what makes them findable at all.
    /// </summary>
    internal static GameAddress Vtable => HeapWalk.PlayerVtable;

    /// <summary>The client's pointer to the record for the person playing.</summary>
    internal static readonly GameAddress LocalPlayer = new(0x00C2D2B8);

    /// <summary>Where the heap starts. Below this is the client's own image.</summary>
    internal static GameAddress HeapStart => HeapWalk.HeapStart;

    /// <summary>And the top of a 32-bit user address space, less a boundary page.</summary>
    internal static GameAddress HeapEnd => HeapWalk.HeapEnd;

    /// <summary>The id to send a packet at.</summary>
    internal const uint RecordId = 0x0C;

    /// <summary>
    /// The three fields that can hold a name.
    /// </summary>
    /// <remarks>
    /// From a real client: <c>+0x60</c> is usually a label, <c>+0x64</c> is a remote
    /// player's name with the possessive attached (<c>某人的</c>), and <c>+0x6C</c> is the
    /// bare name. A summon's <c>+0x6C</c> is its owner's name, because the client builds
    /// what it draws from the owner and the species rather than storing it.
    /// </remarks>
    internal static readonly uint[] NameFields = [0x60, 0x64, 0x6C];

    /// <summary>How much of a record has to be in hand to read all of the above.</summary>
    internal const int RecordLength = 0x70;

    /// <summary>A name is at most sixteen characters, which is thirty-two bytes.</summary>
    private const int NameLength = 64;

    /// <summary>How many candidates to write to the log when nothing matched.</summary>
    private const int MostToLog = 12;

    /// <summary>
    /// The shortest heap name that may be treated as a prefix of what the player typed.
    /// One character would match half the world.
    /// </summary>
    private const int ShortestPrefix = 2;

    private readonly ILegacyTextCodec _codec;
    private readonly ILogger _logger;

    public EntityScan(ILegacyTextCodec codec, ILogger<EntityScan> logger)
    {
        _codec = codec;
        _logger = logger;
    }

    /// <summary>
    /// Finds what to aim at, given the name the player wrote.
    /// </summary>
    /// <remarks>
    /// Null means nothing matched, and the heap has already been written to the log for a
    /// player working out what to type instead.
    /// </remarks>
    public virtual Entity? Find(RemoteProcess process, string name)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var needle = name.Trim();

        if (Self(process, needle) is { } self)
        {
            return self;
        }

        var candidates = All(process);

        if (Match(candidates, needle) is { } found)
        {
            return found;
        }

        Explain(candidates, needle);

        return null;
    }

    /// <summary>
    /// Everything in the heap that looks like a character, with its names read.
    /// </summary>
    /// <remarks>
    /// One walk, and every record's fields come out of the same buffer the walk is already
    /// holding. Only the names themselves are read separately, because they live wherever
    /// the client's string allocator put them.
    /// </remarks>
    public IReadOnlyList<Candidate> All(RemoteProcess process) => All(process, HeapStart, HeapEnd);

    /// <inheritdoc cref="All(RemoteProcess)"/>
    /// <param name="process">The game.</param>
    /// <param name="from">Where to start looking.</param>
    /// <param name="to">And where to stop.</param>
    internal IReadOnlyList<Candidate> All(RemoteProcess process, GameAddress from, GameAddress to)
    {
        ArgumentNullException.ThrowIfNull(process);

        var clock = Stopwatch.StartNew();
        var found = new List<Candidate>();
        var local = process.TryRead<uint>(LocalPlayer, out var player) ? player : 0;

        var read = HeapWalk.Records(process, Vtable, RecordLength, from, to, (address, record) =>
        {
            if (address.Value == local)
            {
                // Handled on its own, with the id the server accepts.
                return;
            }

            if (Read(process, address, record) is { } entity)
            {
                found.Add(entity);
            }
        });

        _logger.LogInformation(
            "Found {Count} characters in {Read} MB of the client's heap in {Elapsed} ms",
            found.Count, read / (1024 * 1024), clock.ElapsedMilliseconds);

        return found;
    }

    /// <summary>
    /// Picks what the player meant out of what the heap holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exact first, over every candidate, before any prefix is considered: a prefix match
    /// on one character must never win over somebody actually called that.
    /// </para>
    /// <para>
    /// The prefix pass is the other way round from how it reads — the heap name is the
    /// prefix and the player's text is longer. It is there because the client draws
    /// <c>某人的 魔熊</c> from two pieces and only stores the first, so a player copying
    /// what they can see types more than the heap holds.
    /// </para>
    /// </remarks>
    internal static Entity? Match(IReadOnlyList<Candidate> candidates, string needle)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (var candidate in candidates)
        {
            foreach (var name in candidate.Names)
            {
                if (string.Equals(name, needle, StringComparison.Ordinal))
                {
                    return new Entity(candidate.Address, candidate.Id, name);
                }
            }
        }

        foreach (var candidate in candidates)
        {
            foreach (var name in candidate.Names)
            {
                if (name.Length >= ShortestPrefix && needle.StartsWith(name, StringComparison.Ordinal))
                {
                    return new Entity(candidate.Address, candidate.Id, name);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Matches the player's own name without walking anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worth a special case because a player's summons carry their owner's name, so
    /// <c>/IT=自己的名字</c> would otherwise find a summon rather than the person who
    /// wrote it.
    /// </para>
    /// <para>
    /// The id sent is <b>not</b> the one in the local record. That field is a client-side
    /// number the server does not know, and a packet aimed at it comes back refused — the
    /// reference notes exactly this and then returns it anyway. What goes out is the id the
    /// client itself uses when it aims at the player, which is what <c>/IME</c> sends.
    /// </para>
    /// </remarks>
    private Entity? Self(RemoteProcess process, string needle)
    {
        if (!process.TryRead<uint>(LocalPlayer, out var record) || record == 0)
        {
            return null;
        }

        var id = process.TryRead<uint>(GameFunctions.SelfId, out var self) ? self : 0;
        var found = Yourself(Read(process, new GameAddress(record)), id, needle);

        if (found is null && id == 0)
        {
            _logger.LogInformation(
                "{Name} may be this character, but the client has not been told its own id yet", needle);
        }

        return found;
    }

    /// <summary>
    /// Decides whether the name written is this character's own, and what to aim at if so.
    /// </summary>
    /// <param name="local">The record the client points at as the player, if it could be read.</param>
    /// <param name="selfId">The id the client itself aims at when it means the player.</param>
    /// <param name="needle">What the player wrote.</param>
    internal static Entity? Yourself(Candidate? local, uint selfId, string needle)
    {
        if (local is not { } candidate || selfId == 0)
        {
            return null;
        }

        return candidate.Names.Any(name => string.Equals(name, needle, StringComparison.Ordinal))
            ? new Entity(candidate.Address, selfId, needle)
            : null;
    }

    /// <summary>
    /// Writes what the heap actually holds to the log.
    /// </summary>
    /// <remarks>
    /// The one thing that makes this feature usable. A name that does not match is nearly
    /// always a name written slightly differently from how the client stores it, and this
    /// is how a player finds out what to write instead.
    /// </remarks>
    private void Explain(IReadOnlyList<Candidate> candidates, string needle)
    {
        _logger.LogInformation(
            "Nothing in the heap is called {Name}. Of {Count} characters found, the first {Shown} are:",
            needle, candidates.Count, Math.Min(candidates.Count, MostToLog));

        foreach (var candidate in candidates.Take(MostToLog))
        {
            _logger.LogInformation(
                "  {Address} id {Id:X8}: {Names}",
                candidate.Address, candidate.Id, string.Join(" | ", candidate.Names));
        }
    }

    /// <summary>Reads a record whose fixed part is not already in hand.</summary>
    private Candidate? Read(RemoteProcess process, GameAddress address)
    {
        Span<byte> record = stackalloc byte[RecordLength];

        return process.TryReadBytes(address, record) ? Read(process, address, record) : null;
    }

    /// <summary>
    /// Turns a record into a candidate, following its name pointers.
    /// </summary>
    /// <remarks>
    /// A record with no id is a slot the client has finished with. The reference keeps
    /// those and will happily return one, which sends a packet aimed at nothing.
    /// </remarks>
    private Candidate? Read(RemoteProcess process, GameAddress address, ReadOnlySpan<byte> record)
    {
        var id = BitConverter.ToUInt32(record[(int)RecordId..]);

        if (id == 0)
        {
            return null;
        }

        var names = new List<string>(NameFields.Length);

        foreach (var field in NameFields)
        {
            var pointer = BitConverter.ToUInt32(record[(int)field..]);

            if (pointer != 0 && Text(process, pointer) is { } name)
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? null : new Candidate(address, id, names);
    }

    /// <summary>
    /// Reads a name the client stores in its own code page.
    /// </summary>
    /// <remarks>
    /// A read that runs off the end of a page fails outright rather than returning what it
    /// could, so a name near a boundary is tried again shorter before being given up on.
    /// </remarks>
    private string? Text(RemoteProcess process, uint at)
    {
        var buffer = new byte[NameLength];

        for (var length = NameLength; length >= 8; length /= 2)
        {
            if (process.TryReadBytes(new GameAddress(at), buffer.AsSpan(0, length)))
            {
                return _codec.DecodeNullTerminated(buffer.AsSpan(0, length)) is { Length: > 0 } name
                    ? name.Trim()
                    : null;
            }
        }

        return null;
    }

    /// <summary>
    /// One record found in the heap, with every name it holds.
    /// </summary>
    /// <param name="Address">Where the record is.</param>
    /// <param name="Id">The id a packet aims at.</param>
    /// <param name="Names">Whichever of the three name fields were filled in.</param>
    public readonly record struct Candidate(GameAddress Address, uint Id, IReadOnlyList<string> Names);
}
