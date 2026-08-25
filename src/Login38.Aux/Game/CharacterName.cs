using Login38.Core.Text;
using Login38.Interop;
using Login38.Patching;

namespace Login38.Aux.Game;

/// <summary>
/// Reads who is playing.
/// </summary>
/// <remarks>
/// Which matters for one reason: helper settings belong to a character rather than to an
/// installation. What a mage wants topped up is not what a knight wants, and a player who
/// moves between them should not have to set it up twice.
/// </remarks>
public static class CharacterName
{
    /// <summary>The player's own object.</summary>
    /// <remarks>Null until a character has been chosen, which is most of a launch.</remarks>
    public static readonly GameAddress PlayerObject = new(0x00C2_D2B8);

    /// <summary>Where the name hangs off it.</summary>
    private const int NameOffset = 0x60;

    /// <summary>
    /// How much of the name is read.
    /// </summary>
    /// <remarks>
    /// The client shows sixteen bytes; this is twice that, so a name that turns out to be
    /// longer is still terminated inside what was read rather than cut off mid-character.
    /// </remarks>
    private const int NameSize = 32;

    /// <summary>Reads the name of the character currently in the world.</summary>
    /// <returns>Null before there is one, which is not a failure.</returns>
    public static string? Read(RemoteProcess process, ILegacyTextCodec codec)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(codec);

        if (!process.TryRead<uint>(GameAddresses.GameState, out var state)
            || state != GameAddresses.InWorld
            || !process.TryRead<uint>(PlayerObject, out var player)
            || new GameAddress(player) < GameStructures.LowestValidPointer
            || !process.TryRead<uint>(new GameAddress(player) + NameOffset, out var name)
            || new GameAddress(name) < GameStructures.LowestValidPointer)
        {
            return null;
        }

        Span<byte> raw = stackalloc byte[NameSize];

        if (!process.TryReadBytes(new GameAddress(name), raw))
        {
            return null;
        }

        var text = codec.DecodeNullTerminated(raw).Trim();

        // An empty name is a pointer to a field that has not been filled in yet, which
        // happens for a moment between choosing a character and being in the world.
        return text.Length == 0 ? null : text;
    }
}
