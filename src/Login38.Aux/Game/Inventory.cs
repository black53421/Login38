using System.Buffers.Binary;
using Login38.Core.Text;
using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>
/// One item in the player's bag, as it was at the moment it was read.
/// </summary>
/// <param name="Entry">Where the item lives in the client's heap.</param>
/// <param name="Param">The server's own id for it, which is what a packet about it carries.</param>
/// <param name="Type">What kind of item it is, as the client's own dispatcher sees it.</param>
/// <param name="Icon">Which icon it draws with.</param>
/// <param name="Equipped">Whether it is worn or wielded.</param>
/// <param name="Count">How many are in the stack.</param>
/// <param name="Name">Its name, already decoded.</param>
public readonly record struct InventoryItem(
    GameAddress Entry, uint Param, byte Type, ushort Icon, bool Equipped, uint Count, string Name)
{
    /// <summary>Whether the name carries one of the client's own state marks.</summary>
    /// <remarks>
    /// The state is in the name because that is where the client puts it — there is no
    /// separate field, and the rules an operator writes talk about the name they can see.
    /// </remarks>
    public bool IsInUse => Name.Contains(ItemNames.InUseMark, StringComparison.Ordinal);

    /// <inheritdoc cref="IsInUse"/>
    public bool IsWielded => Name.Contains(ItemNames.WieldedMark, StringComparison.Ordinal);

    /// <summary>The name with the stack count and any state mark taken off.</summary>
    public string BaseName => ItemNames.Clean(Name);

    /// <inheritdoc/>
    public override string ToString() => Count > 1 ? $"{Name} x{Count}" : Name;
}

/// <summary>
/// Reads the player's bag out of the running client.
/// </summary>
/// <remarks>
/// Walked rather than hooked. It is a read-only snapshot, so nothing has to wait for the
/// client to call out, and any feature that wants to know what the player is carrying can
/// ask at any moment.
/// </remarks>
public static class InventoryReader
{
    /// <summary>Reads every item in the bag.</summary>
    /// <returns>Empty when the player is not in the world yet.</returns>
    /// <exception cref="GameProcessException">
    /// The inventory is there but does not read like one, which means an address has moved.
    /// </exception>
    public static IReadOnlyList<InventoryItem> Read(RemoteProcess process, ILegacyTextCodec codec)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(codec);

        if (!TryFindInventory(process, out var inventory))
        {
            return [];
        }

        if (!process.TryRead<int>(inventory + GameStructures.InventoryCount, out var count)
            || !process.TryRead<uint>(inventory + GameStructures.InventoryItems, out var array))
        {
            return [];
        }

        // Believed only within a range an inventory could actually have. Outside it, the
        // pointer is not an inventory and walking the array would read arbitrary memory as
        // item pointers — which produces items rather than an error.
        if (count is < 0 or > GameStructures.InventoryLimit)
        {
            throw new GameProcessException(
                $"The inventory at {inventory} says it holds {count} items, so it is not an inventory.");
        }

        var items = new GameAddress(array);

        if (items < GameStructures.LowestValidPointer)
        {
            throw new GameProcessException($"The inventory at {inventory} has no item array.");
        }

        var bag = new List<InventoryItem>(count);
        Span<byte> entry = stackalloc byte[GameStructures.Item.HeaderSize];

        for (var i = 0; i < count; i++)
        {
            if (!process.TryRead<uint>(items + (i * sizeof(uint)), out var address)
                || new GameAddress(address) < GameStructures.LowestValidPointer)
            {
                // A hole in the array is ordinary: the client leaves slots behind when an
                // item is used up, and fills them again on the next pickup.
                continue;
            }

            if (process.TryReadBytes(new GameAddress(address), entry))
            {
                bag.Add(Parse(process, codec, new GameAddress(address), entry));
            }
        }

        return bag;
    }

    /// <summary>Finds the first item the predicate accepts.</summary>
    public static InventoryItem? Find(
        IReadOnlyList<InventoryItem> bag, Func<InventoryItem, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (var item in bag)
        {
            if (predicate(item))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an item by the name the player sees.
    /// </summary>
    /// <remarks>
    /// Compared by <see cref="ItemNames.Match"/>, so a rule that says "silver sword" still
    /// matches once the sword is being wielded and the client has renamed it, and a name
    /// whose own brackets are part of it still has to be written in full.
    /// </remarks>
    public static InventoryItem? FindByName(IReadOnlyList<InventoryItem> bag, string name) =>
        Find(bag, item => ItemNames.Match(item.Name, name));

    private static bool TryFindInventory(RemoteProcess process, out GameAddress inventory)
    {
        inventory = default;

        if (!process.TryRead<uint>(GameStructures.InventoryPointer, out var address))
        {
            return false;
        }

        inventory = new GameAddress(address);

        // Null until the player is in the world, which is most of a launch.
        return inventory >= GameStructures.LowestValidPointer;
    }

    private static InventoryItem Parse(
        RemoteProcess process, ILegacyTextCodec codec, GameAddress entry, ReadOnlySpan<byte> header) =>
        new(entry,
            BinaryPrimitives.ReadUInt32LittleEndian(header[GameStructures.Item.Param..]),
            header[GameStructures.Item.Type],
            BinaryPrimitives.ReadUInt16LittleEndian(header[GameStructures.Item.Icon..]),
            header[GameStructures.Item.Equipped] != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(header[GameStructures.Item.Count..]),
            ReadName(process, codec, BinaryPrimitives.ReadUInt32LittleEndian(header[GameStructures.Item.Name..])));

    private static string ReadName(RemoteProcess process, ILegacyTextCodec codec, uint pointer)
    {
        var name = new GameAddress(pointer);

        if (name < GameStructures.LowestValidPointer || name >= GameStructures.HighestValidPointer)
        {
            return string.Empty;
        }

        Span<byte> raw = stackalloc byte[GameStructures.Item.NameSize];

        // Not all of it is readable when the name sits at the end of a page, so a short read
        // is a name rather than a failure — the terminator is inside it either way.
        if (!process.TryReadBytes(name, raw))
        {
            return string.Empty;
        }

        return codec.DecodeNullTerminated(raw);
    }
}
