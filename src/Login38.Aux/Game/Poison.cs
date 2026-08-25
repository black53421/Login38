using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>
/// Whether the character is poisoned.
/// </summary>
/// <remarks>
/// <para>
/// Read straight out of the player's own record rather than from the effect table, because
/// poison does not appear there: the client's poison handler takes a path that sets a timer
/// without writing an effect byte, so the byte stays zero for the whole time a character is
/// dying of it.
/// </para>
/// <para>
/// Two fields have to agree. A bit in the status word says something is wrong, and a byte
/// beside it says what — only one value has been seen against a real monster's poison, and
/// treating the others as poison would drink an antidote at whatever else that bit means.
/// </para>
/// </remarks>
public static class Poison
{
    /// <summary>The client's pointer to the player's record.</summary>
    internal static readonly GameAddress PlayerPointer = new(0x00C2D2B8);

    /// <summary>The status word within it.</summary>
    internal const uint Status = 0x20;

    /// <summary>The bit in that word that is set while something is wrong.</summary>
    internal const uint Bit = 0x20;

    /// <summary>And the byte saying which.</summary>
    internal const uint Kind = 0x25;

    /// <summary>The one value verified against a real monster's poison.</summary>
    internal const byte Damaging = 1;

    /// <summary>Whether the character is taking damage from poison right now.</summary>
    /// <remarks>Anything unreadable is "no", which is the answer that does nothing.</remarks>
    public static bool IsDamaging(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!process.TryRead<uint>(PlayerPointer, out var player) || player == 0)
        {
            return false;
        }

        if (!process.TryRead<uint>(new GameAddress(player + Status), out var status)
            || (status & Bit) == 0)
        {
            return false;
        }

        return process.TryRead<byte>(new GameAddress(player + Kind), out var kind) && kind == Damaging;
    }
}
