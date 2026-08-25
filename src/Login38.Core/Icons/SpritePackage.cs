using System.Buffers.Binary;
using System.Text;

namespace Login38.Core.Icons;

/// <summary>Where a file sits inside a package.</summary>
public readonly record struct PackageEntry(uint Offset, uint Size);

/// <summary>
/// The client's sprite package format: a body file and an index beside it.
/// </summary>
/// <remarks>
/// <para>
/// The index is a count followed by fixed records — offset, a twenty-byte name padded with
/// nulls, and a size. The body is the files, concatenated, with nothing between them. There
/// is no compression and no encryption; the index is what makes it a package.
/// </para>
/// <para>
/// The launcher only reads the icon package it built itself, but it reads it with the same
/// code that writes it, so the two cannot drift apart.
/// </para>
/// </remarks>
public static class SpritePackage
{
    private const int CountSize = 4;
    private const int EntrySize = 28;
    private const int NameOffset = 4;

    /// <summary>The longest name the index can hold.</summary>
    public const int MaxNameLength = 20;

    private const int SizeOffset = 24;

    /// <summary>Finds a file in the index, ignoring case.</summary>
    /// <returns>Null when the index does not name it, or is too short to be an index.</returns>
    public static PackageEntry? Find(ReadOnlySpan<byte> index, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (index.Length < CountSize)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(index);
        var entries = index[CountSize..];

        // A count that does not match the file is a truncated or corrupt index, not a
        // missing file — but it is read the same way, because either way the file is not
        // going to be found and the caller's message says which file it wanted.
        if ((ulong)count * EntrySize > (ulong)entries.Length)
        {
            return null;
        }

        for (var i = 0; i < count; i++)
        {
            var entry = entries.Slice(i * EntrySize, EntrySize);

            if (NameOf(entry).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return new PackageEntry(
                    BinaryPrimitives.ReadUInt32LittleEndian(entry),
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[SizeOffset..]));
            }
        }

        return null;
    }

    /// <summary>Reads a file out of a package.</summary>
    /// <exception cref="DynamicIconException">
    /// The package does not name the file, or names it outside itself.
    /// </exception>
    public static ReadOnlyMemory<byte> Read(ReadOnlyMemory<byte> package, ReadOnlySpan<byte> index, string name)
    {
        if (Find(index, name) is not { } entry)
        {
            throw new DynamicIconException($"The icon package does not contain {name}.");
        }

        // Both are attacker-controlled only in the sense that an operator can hand-edit
        // them, but a bad offset here is an out-of-range read either way.
        if (entry.Offset > (ulong)package.Length || entry.Offset + (ulong)entry.Size > (ulong)package.Length)
        {
            throw new DynamicIconException(
                $"The icon package says {name} is {entry.Size} bytes at {entry.Offset}, which is past its end.");
        }

        return package.Slice((int)entry.Offset, (int)entry.Size);
    }

    /// <summary>Packs files into a package and its index.</summary>
    /// <exception cref="ArgumentException">A name is longer than the index can hold.</exception>
    public static (byte[] Package, byte[] Index) Build(
        IReadOnlyList<(string Name, byte[] Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var index = new byte[CountSize + (files.Count * EntrySize)];
        BinaryPrimitives.WriteUInt32LittleEndian(index, (uint)files.Count);

        using var package = new MemoryStream();

        for (var i = 0; i < files.Count; i++)
        {
            var (name, content) = files[i];
            var encoded = Encoding.ASCII.GetBytes(name);

            if (encoded.Length > MaxNameLength)
            {
                throw new ArgumentException(
                    $"\"{name}\" is {encoded.Length} bytes; the package index holds {MaxNameLength}.",
                    nameof(files));
            }

            var entry = index.AsSpan(CountSize + (i * EntrySize), EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)package.Length);
            encoded.CopyTo(entry[NameOffset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[SizeOffset..], (uint)content.Length);

            package.Write(content);
        }

        return (package.ToArray(), index);
    }

    private static string NameOf(ReadOnlySpan<byte> entry)
    {
        var name = entry.Slice(NameOffset, MaxNameLength);
        var end = name.IndexOf((byte)0);

        return Encoding.ASCII.GetString(end < 0 ? name : name[..end]);
    }
}
