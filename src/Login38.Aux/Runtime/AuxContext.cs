using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;

namespace Login38.Aux.Runtime;

/// <summary>
/// What every helper task sees on one pass of the loop.
/// </summary>
/// <remarks>
/// <para>
/// The player and the bag are read at most once per pass and shared. That is not a
/// micro-optimisation: the reference had four separate threads each walking the whole
/// inventory twice a second, across a process boundary, and each seeing a slightly
/// different moment. One read per pass is fewer crossings and one consistent picture.
/// </para>
/// <para>
/// Lazily, because most passes have nothing to do and a pass where every rule is switched
/// off should cost nothing at all.
/// </para>
/// <para>
/// Not sealed, and the four readings are virtual. A helper task is mostly a decision about
/// what the game currently looks like, and the only way to exercise that decision is to be
/// able to say what it looks like.
/// </para>
/// </remarks>
public class AuxContext
{
    private readonly ILegacyTextCodec _codec;
    private PlayerState? _player;
    private IReadOnlyList<InventoryItem>? _bag;
    private string? _character;
    private bool _characterRead;
    private bool? _inWorld;

    /// <param name="process">The running game.</param>
    /// <param name="settings">What the player has asked for, as of this pass.</param>
    /// <param name="codec">For the names that come out of the game.</param>
    public AuxContext(RemoteProcess process, AuxSettings settings, ILegacyTextCodec codec)
    {
        Process = process;
        Settings = settings;
        _codec = codec;
    }

    /// <summary>The running game.</summary>
    public RemoteProcess Process { get; }

    /// <summary>What the player has asked for. Fixed for the length of this pass.</summary>
    public AuxSettings Settings { get; }

    /// <summary>The player, read once and shared.</summary>
    public virtual PlayerState Player => _player ??= PlayerStateReader.Read(Process);

    /// <summary>Whether there is a character in the world to act on.</summary>
    /// <remarks>
    /// <para>
    /// Almost every task starts here. Before it, the structures the rest of this reads are
    /// either unallocated or hold the previous character's numbers — and a rule like "drink
    /// below half" would fire on every pass of it.
    /// </para>
    /// <para>
    /// The client's own answer rather than "does the player have hit points", because the
    /// two disagree exactly where it matters: on the way in and on the way out the player's
    /// numbers still read as the last character's while the inventory and the item routines
    /// are already gone.
    /// </para>
    /// </remarks>
    public virtual bool IsInWorld => _inWorld ??= GameState.InWorld(Process);

    /// <summary>The bag, read once and shared.</summary>
    /// <remarks>Empty when the player is not in the world, which the reader decides.</remarks>
    public virtual IReadOnlyList<InventoryItem> Bag => _bag ??= ReadBag();

    /// <summary>The name of the character playing, or null before there is one.</summary>
    /// <remarks>
    /// Read once and kept, null included: "nobody is playing yet" is the answer for most of
    /// a launch and is worth caching as much as any other.
    /// </remarks>
    public virtual string? Character
    {
        get
        {
            if (!_characterRead)
            {
                _character = CharacterName.Read(Process, _codec);
                _characterRead = true;
            }

            return _character;
        }
    }

    private IReadOnlyList<InventoryItem> ReadBag()
    {
        try
        {
            return InventoryReader.Read(Process, _codec);
        }
        catch (GameProcessException)
        {
            // The bag not reading like a bag is worth reporting once, by whoever asked for
            // it — but not worth failing the pass, because several tasks want it and the
            // rest of them have nothing to do with the bag.
            return [];
        }
    }
}
