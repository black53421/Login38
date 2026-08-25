using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>
/// Where the helper features read the player and their inventory from.
/// </summary>
/// <remarks>
/// <para>
/// The client has no relocation table and always loads at its preferred base, so these
/// are absolute. Gathered here rather than repeated at each use because several features
/// read the same structures, and because a wrong address here reads plausible-looking
/// numbers rather than failing.
/// </para>
/// <para>
/// The reference kept a much larger catalogue with a confidence rating per entry and a
/// rule that anything below four stars must not ship. Only the entries this port actually
/// reads are carried over, and only the ones that were verified against a running client.
/// </para>
/// </remarks>
public static class GameStructures
{
    /// <summary>Current hit points — an object, not a value. See <see cref="ObfuscatedStat"/>.</summary>
    public static readonly GameAddress HitPointsObject = new(0x00BD_C828);

    /// <inheritdoc cref="HitPointsObject"/>
    public static readonly GameAddress ManaPointsObject = new(0x00BD_C834);

    /// <summary>Maximum hit points, as a plain dword.</summary>
    /// <remarks>
    /// Not obfuscated, and widened to thirty-two bits by the hit point expansion patch.
    /// Only the current value is hidden — the maximum is on screen anyway.
    /// </remarks>
    public static readonly GameAddress MaximumHitPoints = new(0x00C3_1E90);

    /// <inheritdoc cref="MaximumHitPoints"/>
    public static readonly GameAddress MaximumManaPoints = new(0x00C3_1E8C);

    /// <summary>How full the character is, before scaling.</summary>
    /// <remarks>
    /// A byte in the character block, immediately after the six ability scores. What the
    /// client shows is <c>raw * 100 / 225</c> — verified against a running client at 127
    /// showing 52% and 130 showing 57%.
    /// </remarks>
    public static readonly GameAddress FoodLevel = new(0x00C3_1E8B);

    /// <inheritdoc cref="FoodLevel"/>
    public const uint FoodLevelFull = 225;

    /// <summary>How loaded the character is, before scaling.</summary>
    /// <inheritdoc cref="FoodLevel" path="/remarks"/>
    public static readonly GameAddress Weight = new(0x00C3_1E8A);

    /// <inheritdoc cref="Weight"/>
    public const uint WeightFull = 240;

    /// <summary>The map the player is standing on.</summary>
    public static readonly GameAddress MapId = new(0x0096_5B60);

    /// <summary>
    /// The inventory object.
    /// </summary>
    /// <remarks>
    /// A pointer to it, which is null until the player is in the world. The reference's
    /// own module comment names a different address than its constant does — the constant
    /// is the one its code uses and the one verified against a running client.
    /// </remarks>
    public static readonly GameAddress InventoryPointer = new(0x009A_9250);

    /// <summary>How many items the inventory holds.</summary>
    public const int InventoryCount = 0x2C;

    /// <summary>The array of pointers to those items.</summary>
    public const int InventoryItems = 0x58;

    /// <summary>
    /// The most items the inventory can plausibly hold.
    /// </summary>
    /// <remarks>
    /// Not the client's own limit — a bound on what is worth believing. A count outside
    /// this is a sign the pointer is not an inventory, and walking it would read arbitrary
    /// memory as item pointers.
    /// </remarks>
    public const int InventoryLimit = 512;

    /// <summary>The lowest address worth treating as a pointer.</summary>
    /// <remarks>
    /// The bottom of the address space is never mapped in a Windows process, so anything
    /// below this is an uninitialised field rather than something to follow.
    /// </remarks>
    public static readonly GameAddress LowestValidPointer = new(0x0010_0000);

    /// <summary>The highest address worth treating as a pointer to a string.</summary>
    public static readonly GameAddress HighestValidPointer = new(0x4000_0000);

    /// <summary>Offsets within one item.</summary>
    public static class Item
    {
        /// <summary>The server's own id for it, which is what a packet about it carries.</summary>
        public const int Param = 0x04;

        /// <summary>Non-zero while the entry holds an item.</summary>
        public const int Valid = 0x08;

        /// <summary>Non-zero while the item is worn or wielded.</summary>
        public const int Equipped = 0x09;

        /// <summary>The name, as a pointer to a NUL-terminated legacy string.</summary>
        public const int Name = 0x0C;

        /// <summary>What kind of item it is, as the client's own dispatcher sees it.</summary>
        public const int Type = 0x98;

        /// <summary>Which icon it draws with.</summary>
        public const int Icon = 0x9A;

        /// <summary>
        /// How many are in the stack.
        /// </summary>
        /// <remarks>
        /// <c>+0xA4</c> looks like this and is not — in this client it is the enchantment
        /// level. Sending a delete with that value deletes the wrong number of items.
        /// </remarks>
        public const int Count = 0xA0;

        /// <summary>
        /// What the client shows when the item is pointed at, as a pointer to text.
        /// </summary>
        /// <remarks>
        /// Null for a while after something is equipped. It is the only place a weapon's
        /// wear shows up — there is no separate field for it.
        /// </remarks>
        public const int Description = 0xA8;

        /// <summary>How much of an item is read in one go, to cover every offset above.</summary>
        public const int HeaderSize = 0x100;

        /// <summary>The most of a name that is read.</summary>
        public const int NameSize = 64;
    }
}
