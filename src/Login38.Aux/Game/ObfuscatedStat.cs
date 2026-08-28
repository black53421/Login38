using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>
/// Reads a value the client keeps deliberately hard to find.
/// </summary>
/// <remarks>
/// <para>
/// Current hit points and mana are not stored anywhere a memory scanner would find them.
/// What is at the address is a three-field object: an index, a pointer to an array of
/// keys, and a salt. The real value is one of sixteen slots in that array, XORed with the
/// salt, and the index that says which slot is itself XORed with a constant. The setter
/// re-salts on every write, so the bytes change even when the value does not.
/// </para>
/// <para>
/// Reproduced exactly, because it is the client's arrangement rather than a choice: any
/// other reading of those twelve bytes gives a number that looks like hit points and is
/// not.
/// </para>
/// </remarks>
public static class ObfuscatedStat
{
    /// <summary>What the stored index is XORed with.</summary>
    public const uint IndexKey = 0xC001_7921;

    /// <summary>How many slots the key array holds.</summary>
    public const uint Slots = 16;

    private const int KeyArrayOffset = 4;
    private const int SaltOffset = 8;

    /// <summary>Reads one of these.</summary>
    /// <returns>False when the object has not been set up, or the process could not be read.</returns>
    public static bool TryRead(RemoteProcess process, GameAddress stat, out uint value)
    {
        ArgumentNullException.ThrowIfNull(process);

        value = 0;

        if (!process.TryRead<uint>(stat, out var encodedIndex)
            || !process.TryRead<uint>(stat + KeyArrayOffset, out var keys)
            || !process.TryRead<uint>(stat + SaltOffset, out var salt))
        {
            return false;
        }

        var index = encodedIndex ^ IndexKey;

        // Before the first write the whole object is zero, which decodes to an index far
        // outside the array. Checked rather than masked: reading slot `index & 15` of an
        // array that does not exist yet would return a plausible number.
        if (index >= Slots)
        {
            return false;
        }

        if (!process.TryRead<uint>(new GameAddress(keys) + (int)(index * sizeof(uint)), out var encoded))
        {
            return false;
        }

        value = encoded ^ salt;
        return true;
    }

    /// <summary>
    /// Puts one of these back to zero.
    /// </summary>
    /// <returns>False when the object has not been set up, or could not be written.</returns>
    /// <remarks>
    /// <para>
    /// The salt written into the slot the index already names, which decodes to zero. The
    /// client's own setter re-salts and re-keys the whole array on every write; this does
    /// not, because it has no need to hide anything and every extra write is another way to
    /// disagree with a reader running at the same time.
    /// </para>
    /// <para>
    /// Only worth doing to something the client will not put back itself. It exists for one
    /// word — the mode flags — whose writer only ever ORs bits in.
    /// </para>
    /// </remarks>
    public static bool TryClear(RemoteProcess process, GameAddress stat)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!process.TryRead<uint>(stat, out var encodedIndex)
            || !process.TryRead<uint>(stat + KeyArrayOffset, out var keys)
            || !process.TryRead<uint>(stat + SaltOffset, out var salt))
        {
            return false;
        }

        var index = encodedIndex ^ IndexKey;

        if (index >= Slots || keys == 0)
        {
            return false;
        }

        try
        {
            process.Write(new GameAddress(keys) + (int)(index * sizeof(uint)), salt);

            return true;
        }
        catch (GameProcessException)
        {
            return false;
        }
    }
}
