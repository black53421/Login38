using System.Collections.Frozen;
using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>
/// What the character is currently under the effect of.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps a byte per effect, set when the server says an effect started and
/// cleared when it ends. The numbers are effect <i>classes</i> rather than skills: haste
/// from a potion, from the spell, and from the stronger spell all set the same byte, which
/// is exactly what a feature that keeps buffs up needs to know — "am I hasted", not "did
/// this particular thing land".
/// </para>
/// <para>
/// Without it the helper would recast every buff on a fixed interval whether or not it was
/// still up, which the server answers with an error and the player sees as their character
/// standing still casting nothing.
/// </para>
/// </remarks>
public sealed class BuffState
{
    /// <summary>The table of effect bytes.</summary>
    internal static readonly GameAddress Table = new(0x00ABF4C8);

    /// <summary>
    /// How long each half is.
    /// </summary>
    /// <remarks>
    /// There are two, one directly after the other — the second begins at
    /// <c>0x00ABF6B8</c>, which is exactly <c>Table + 0x1F0</c>. An effect showing in
    /// either counts. The reference reads the first half in one go and then makes a
    /// separate one-byte read into the second for every entry it looks at; one read covers
    /// both.
    /// </remarks>
    internal const int Half = 0x1F0;

    /// <summary>Where the client keeps the character's class.</summary>
    internal static readonly GameAddress ClassAddress = new(0x00C31544);

    /// <summary>Which byte means "transformed".</summary>
    internal const int Transformed = 39;

    /// <summary>
    /// The effects the client numbers differently depending on the character's class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's own <c>apply_buff</c> gates about ten of its callers on the class and
    /// writes a different byte for each — walking haste is byte 24 for a knight, 42 for a
    /// wizard, and 37 for everybody else. A player's settings hold the ordinary number, so
    /// without this a knight's haste never reads as active and the helper recasts it for
    /// ever.
    /// </para>
    /// <para>
    /// From a static sweep of all 522 callers of <c>apply_buff</c> looking for the
    /// class comparison. Everything not listed is the same number for everyone.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<(byte Class, int StateId), int> ByClass =
        new Dictionary<(byte, int), int>
        {
            [(Knight, 37)] = 24,
            [(Wizard, 37)] = 42,
            [(Elf, 14)] = 31,
            [(Elf, 15)] = 30,
            [(Elf, 16)] = 32,
            [(Elf, 17)] = 29,
            [(Elf, 140)] = 121,
            [(Illusionist, 16)] = 116,
        }.ToFrozenDictionary();

    private const byte Knight = 2;
    private const byte Wizard = 3;
    private const byte Elf = 4;
    private const byte Illusionist = 6;

    private readonly byte[] _bytes;

    private BuffState(byte[] bytes, byte characterClass)
    {
        _bytes = bytes;
        Class = characterClass;
    }

    /// <summary>The character's class, as the remapping needs it.</summary>
    public byte Class { get; }

    /// <summary>How many effects a settings entry may name.</summary>
    public static int Effects => Half;

    /// <summary>
    /// Reads the whole table.
    /// </summary>
    /// <returns>Null while it cannot be read, which is the case before the world loads.</returns>
    public static BuffState? Read(RemoteProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var bytes = new byte[Half * 2];

        if (!process.TryReadBytes(Table, bytes))
        {
            return null;
        }

        return new BuffState(bytes, process.TryRead<byte>(ClassAddress, out var name) ? name : (byte)0);
    }

    /// <summary>Builds one from bytes already in hand, for a test.</summary>
    internal static BuffState From(byte[] bytes, byte characterClass) => new(bytes, characterClass);

    /// <summary>
    /// Whether an effect the player's settings name is currently on them.
    /// </summary>
    /// <remarks>
    /// The class remapping is applied here rather than by the caller, so there is one place
    /// that knows a settings number is not always a table index.
    /// </remarks>
    public bool Active(int stateId)
    {
        var index = Remap(Class, stateId);

        if (index < 0 || index >= Half)
        {
            return false;
        }

        return At(index);
    }

    /// <summary>
    /// Whether a byte is set, by its index in the table rather than by a settings number.
    /// </summary>
    /// <remarks>
    /// For the handful of places that know an index outright — the transform flag, which is
    /// the client's own numbering and never a player's. Separate from
    /// <see cref="Active(int)"/> so that adding a class remapping later cannot silently
    /// change what those mean.
    /// </remarks>
    public bool At(int index) =>
        index >= 0 && index < Half && (_bytes[index] != 0 || _bytes[Half + index] != 0);

    /// <summary>Turns the number in a player's settings into this character's table index.</summary>
    internal static int Remap(byte characterClass, int stateId) =>
        ByClass.TryGetValue((characterClass, stateId), out var actual) ? actual : stateId;
}
