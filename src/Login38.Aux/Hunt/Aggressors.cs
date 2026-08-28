using Login38.Interop;

namespace Login38.Aux.Hunt;

/// <summary>
/// Who is currently acting on the character, read out of the client's own bookkeeping.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps this list itself and always has. Every action routine that plays a
/// creature's action — there are eight of them — ends with the same three instructions: take
/// the action's target list, ask it whether it contains <c>[0x00ABF4B4]</c>, and if it does,
/// add the actor's id to the set at <see cref="HuntAddresses.Aggressors"/>. The id it adds is
/// the same one <see cref="HuntAddresses.EntityId"/> reads, so it can be looked straight up in
/// the scan.
/// </para>
/// <para>
/// The launcher used to answer this question with a detour of its own on the melee action
/// routine, and the detour was correct and never fired once. It stood in front of one of the
/// eight, and a monster biting a character does not go through that one. The client's set is
/// not a better version of that hook; it is the union of what eight such hooks would have to
/// say, maintained by the code that already knows.
/// </para>
/// <para>
/// A set, not an event. Entries are added once each — the client's own add refuses duplicates
/// — and removed again when the creature stops, so this answers "who is on the character right
/// now" on every pass rather than "who just landed one" on the pass a blow landed. That is the
/// stronger of the two questions and the one retaliation actually wants: a monster that has
/// been chewing on the character for four seconds is still an answer.
/// </para>
/// </remarks>
public static class Aggressors
{
    /// <summary>
    /// How long a list is worth believing.
    /// </summary>
    /// <remarks>
    /// Nothing puts a bound on this in the client, so the bound is here. A count read out of a
    /// world that is being torn down is a count like 0x8F3A2C00, and the difference between
    /// refusing it and believing it is a quarter of a gigabyte of reads.
    /// </remarks>
    private const uint Most = 64;

    /// <summary>Everything acting on the character, in the order the client learned of it.</summary>
    /// <returns>Empty when nothing is, and empty when the client could not be read.</returns>
    public static IReadOnlyList<uint> Ids(RemoteProcess process) =>
        Ids(process, HuntAddresses.Aggressors);

    /// <inheritdoc cref="Ids(RemoteProcess)"/>
    internal static IReadOnlyList<uint> Ids(RemoteProcess process, GameAddress at)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            if (!process.TryRead<uint>(at + HuntAddresses.AggressorCount, out var count)
                || count == 0
                || count > Most)
            {
                return [];
            }

            if (!process.TryRead<uint>(at + HuntAddresses.AggressorIds, out var buffer)
                || buffer == 0)
            {
                return [];
            }

            var bytes = new byte[count * sizeof(uint)];

            if (!process.TryReadBytes(new GameAddress(buffer), bytes))
            {
                return [];
            }

            var ids = new uint[count];

            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = BitConverter.ToUInt32(bytes, i * sizeof(uint));
            }

            return ids;
        }
        catch (GameProcessException)
        {
            // A game on its way out. Nothing is attacking a character that is not there.
            return [];
        }
    }
}
