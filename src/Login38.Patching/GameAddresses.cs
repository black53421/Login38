using Login38.Interop;

namespace Login38.Patching;

/// <summary>
/// Fixed addresses in the client, for the globals more than one feature reads.
/// </summary>
/// <remarks>
/// The client has no relocation table and always loads at its preferred base, so these
/// are absolute. They are gathered here rather than repeated at each use because a wrong
/// one reads plausible-looking garbage instead of failing.
/// </remarks>
public static class GameAddresses
{
    /// <summary>
    /// Which part of the client is running: the title screen, the character list, or the
    /// world.
    /// </summary>
    public static readonly GameAddress GameState = new(0x009A_B5E8);

    /// <summary>
    /// The value <see cref="GameState"/> holds once the player is in the world.
    /// </summary>
    /// <remarks>
    /// The gate for anything that reads the player, the map or the spell tables: before
    /// this, those structures are either unallocated or hold the previous session's data.
    /// </remarks>
    public const uint InWorld = 3;

    /// <summary>
    /// The client's own file name.
    /// </summary>
    /// <remarks>
    /// A constant rather than a discovery, because several of the client's data files are
    /// named after it and have to be found before the process has been started long enough
    /// to ask it anything.
    /// </remarks>
    public const string ExecutableName = "TW13081901.bin";
}
