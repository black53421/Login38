using System.Text;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Notifications;

/// <summary>
/// Finds a named file inside the client's sprite archives.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps its artwork in <c>Sprite00.pak</c> … <c>Sprite12.pak</c>, each with an
/// <c>.idx</c> beside it saying where in the archive each file starts. The index is a
/// four-byte count and then twenty-eight bytes per entry: a four-byte offset, twenty bytes
/// of NUL-padded name, and a four-byte size.
/// </para>
/// <para>
/// Read from disk, not from the game. This is the client's own installed data, and reading
/// it does not need the game to be running — which is what makes the whole icon path
/// something a test can exercise.
/// </para>
/// </remarks>
public sealed class SpriteIndex
{
    /// <summary>Four bytes of record count.</summary>
    internal const int HeaderSize = 4;

    /// <summary>And then this much per entry.</summary>
    internal const int EntrySize = 28;

    /// <summary>Where in an entry the name is.</summary>
    internal const int NameOffset = 4;

    /// <summary>How much of it there is.</summary>
    internal const int NameLength = 20;

    /// <summary>And where the size is.</summary>
    internal const int SizeOffset = 24;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;

    public SpriteIndex(ILogger<SpriteIndex> logger) => _logger = logger;

    /// <summary>Where one file is.</summary>
    /// <param name="Archive">Which <c>.pak</c> it is in.</param>
    /// <param name="Offset">Where in it the file starts.</param>
    /// <param name="Size">What the index says its length is, which is a hint rather than a fact.</param>
    public readonly record struct Entry(string Archive, uint Offset, uint Size);

    /// <summary>How many files were found.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Reads every index beside the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In name order. A file appearing in more than one archive is taken from the first, and
    /// the client searches <c>Sprite00</c> upwards — so the order the directory happens to
    /// be enumerated in decides which copy is used. The reference relies on that order
    /// without sorting, which is not something a file system promises.
    /// </para>
    /// <para>
    /// One of these per game rather than one per process, so two clients installed in two
    /// directories do not take each other's artwork.
    /// </para>
    /// </remarks>
    /// <param name="gameDirectory">Where the client is installed.</param>
    /// <returns>Whether anything was found.</returns>
    public bool Load(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        _entries.Clear();

        if (!Directory.Exists(gameDirectory))
        {
            _logger.LogWarning("{Directory} is not there, so there are no item icons", gameDirectory);

            return false;
        }

        foreach (var index in Directory.EnumerateFiles(gameDirectory, "Sprite*.idx").Order(StringComparer.OrdinalIgnoreCase))
        {
            var archive = Path.ChangeExtension(index, ".pak");

            if (!File.Exists(archive))
            {
                continue;
            }

            try
            {
                Add(File.ReadAllBytes(index), archive);
            }
            catch (IOException e)
            {
                _logger.LogWarning(e, "{Index} could not be read", index);
            }
        }

        _logger.LogInformation(
            "Found {Count} sprite files in the archives beside {Directory}", _entries.Count, gameDirectory);

        return _entries.Count > 0;
    }

    /// <summary>
    /// Reads the entries out of one index.
    /// </summary>
    /// <remarks>
    /// The declared count is believed. The reference reads it and then works the number out
    /// from the file's length instead, so anything appended past the last entry — which is
    /// how these files are commonly padded — becomes entries with names of whatever bytes
    /// happened to be there.
    /// </remarks>
    internal void Add(ReadOnlySpan<byte> index, string archive)
    {
        if (index.Length < HeaderSize)
        {
            return;
        }

        var declared = BitConverter.ToUInt32(index);
        var available = (index.Length - HeaderSize) / EntrySize;
        var count = (int)Math.Min(declared, (uint)available);

        for (var i = 0; i < count; i++)
        {
            var entry = index.Slice(HeaderSize + (i * EntrySize), EntrySize);
            var name = Name(entry.Slice(NameOffset, NameLength));

            if (name.Length == 0)
            {
                continue;
            }

            // First archive wins, which is the order the client searches them in.
            _entries.TryAdd(
                name,
                new Entry(archive, BitConverter.ToUInt32(entry), BitConverter.ToUInt32(entry[SizeOffset..])));
        }
    }

    /// <summary>Reads a NUL-padded name.</summary>
    private static string Name(ReadOnlySpan<byte> raw)
    {
        var end = raw.IndexOf((byte)0);

        return Encoding.ASCII.GetString(end < 0 ? raw : raw[..end]).Trim();
    }

    /// <summary>Where a named file is, or null if the client does not have it.</summary>
    public Entry? Find(string name) => _entries.TryGetValue(name, out var entry) ? entry : null;

    /// <summary>
    /// Reads a file out of its archive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Up to <paramref name="most"/> bytes, stopping at the end of the archive. The size in
    /// the index is not trusted: the reference's own comments say it is unreliable for the
    /// PNGs and reliable for everything else, in two places, about the same field — so this
    /// reads a window and lets each format's own decoder find its end.
    /// </para>
    /// <para>
    /// A short read is filled rather than accepted. The reference calls <c>read</c> once and
    /// keeps whatever came back, which is allowed to be less than asked for and silently
    /// truncates an icon halfway down.
    /// </para>
    /// </remarks>
    public byte[]? Read(string name, int most)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(most);

        if (Find(name) is not { } entry)
        {
            return null;
        }

        try
        {
            using var archive = File.OpenRead(entry.Archive);

            var length = (int)Math.Min(most, Math.Max(0, archive.Length - entry.Offset));

            if (length == 0)
            {
                return null;
            }

            archive.Seek(entry.Offset, SeekOrigin.Begin);

            var raw = new byte[length];

            archive.ReadExactly(raw);

            return raw;
        }
        catch (IOException e)
        {
            _logger.LogWarning(e, "{Name} could not be read out of {Archive}", name, entry.Archive);

            return null;
        }
    }
}
