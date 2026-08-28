using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// The client's one-way gates, and how to get past one that has latched.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a flag the client sets on its way into something and clears on its
/// way out — and every one of them is cleared by code that only runs if the client is still
/// going. That is fine while it is: the walk tick fires the queued cast and clears the flag,
/// the pump runs the pending tick and clears that one. It stops being fine the moment both
/// of the client's self-driving chains are dead at once, because then nothing is left to run
/// the code that clears anything.
/// </para>
/// <para>
/// The one that bites is <see cref="HuntAddresses.CastQueued"/>. The replayed click refuses
/// to run the walk engine while a cast is queued — correctly, because firing a half-built
/// cast is what left a character frozen for a whole session before — and the only thing that
/// fires a queued cast is the walk engine's own tail. Queued with no tick scheduled, the two
/// wait for each other for ever. The character stands still, takes the beating, and no amount
/// of waiting gets it back, which is exactly what it looks like from outside.
/// </para>
/// <para>
/// So: after long enough with the client demonstrably doing nothing at all, whatever is still
/// set is stuck rather than busy, and clearing it is the only way back. Everything read here
/// is named in the log before it is written, because which one latched is the whole question
/// the next time this happens.
/// </para>
/// </remarks>
internal static class ClientGates
{
    /// <summary>One flag the client will not act through.</summary>
    /// <param name="Name">What to call it in the log.</param>
    /// <param name="Address">Where it sits.</param>
    /// <param name="Wide">Whether it is a dword rather than a byte.</param>
    internal readonly record struct Gate(string Name, GameAddress Address, bool Wide);

    /// <summary>
    /// Every gate worth clearing, in the order the client would have cleared them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mode word at <see cref="HuntAddresses.MovementBlocked"/> is not in here. It is
    /// obfuscated rather than plain, and it is the one thing the hunt already handles on its
    /// own timer — see <c>HuntTask.Waiting</c>.
    /// </para>
    /// <para>
    /// Nor is anything the server owns. <see cref="HuntAddresses.AttackBlocked"/> counts a
    /// real paralysis and the server will end it; clearing that locally buys a character that
    /// swings at things the server will not let it hit.
    /// </para>
    /// </remarks>
    internal static readonly Gate[] All =
    [
        new("cast queued", HuntAddresses.CastQueued, Wide: false),
        new("tick pending", HuntAddresses.TickPending, Wide: false),
        new("kick blocked A", HuntAddresses.KickBlockerA, Wide: false),
        new("kick blocked B", HuntAddresses.KickBlockerB, Wide: false),
        new("attack in flight", HuntAddresses.AttackInFlight, Wide: true),
        new("autoattack running", HuntAddresses.AutoAttackRunning, Wide: true),
    ];

    /// <summary>
    /// Opens every shut gate, and says which ones those were.
    /// </summary>
    /// <remarks>
    /// Only the shut ones are written. A gate that already reads zero is a gate the client is
    /// keeping properly, and writing zero over zero would still be a write into another
    /// process for no reason.
    /// </remarks>
    internal static IReadOnlyList<string> Open(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var opened = new List<string>();

        foreach (var gate in All)
        {
            if (!Shut(process, gate))
            {
                continue;
            }

            try
            {
                if (gate.Wide)
                {
                    process.Write<uint>(gate.Address, 0);
                }
                else
                {
                    process.Write<byte>(gate.Address, 0);
                }

                opened.Add(gate.Name);
            }
            catch (GameProcessException)
            {
                // A game on its way out. Whatever was stuck is about to stop mattering, and
                // the caller has nothing useful to do about a write that did not land.
            }
        }

        return opened;
    }

    private static bool Shut(RemoteProcess process, Gate gate) => gate.Wide
        ? process.TryRead<uint>(gate.Address, out var wide) && wide != 0
        : process.TryRead<byte>(gate.Address, out var narrow) && narrow != 0;
}
