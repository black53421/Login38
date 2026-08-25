using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>
/// Whether the client is somewhere it can be acted on.
/// </summary>
/// <remarks>
/// <para>
/// Not the same question as "does the player have hit points". Between the character screen
/// and the world, and again on the way out, the client's inventory pointer and the state
/// the item routines expect are either not filled in yet or already released — and calling
/// into them there does not fail, it crashes the game.
/// </para>
/// <para>
/// So everything that acts on the client goes through this first, and it is a separate
/// reading from the player's own numbers because those can still hold the last character's
/// values after the client has moved on.
/// </para>
/// </remarks>
public static class GameState
{
    /// <summary>Where the client keeps what it is currently doing.</summary>
    internal static readonly GameAddress Address = new(0x009AB5E8);

    /// <summary>The value that means a character is standing in the world.</summary>
    internal const uint InWorldValue = 3;

    /// <summary>Whether a character is in the world right now.</summary>
    /// <remarks>Anything unreadable is "no", which is the safe answer.</remarks>
    public static bool InWorld(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return process.TryRead<uint>(Address, out var state) && state == InWorldValue;
    }
}
