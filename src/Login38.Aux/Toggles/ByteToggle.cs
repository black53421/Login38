using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Toggles;

/// <summary>
/// Something the player can switch on and off while they are playing.
/// </summary>
/// <remarks>
/// Not a patch. A patch is applied once at launch and stays; these follow a switch in the
/// helper window, so the same run of the game sees them go on and off again.
/// </remarks>
public interface IGameToggle
{
    /// <summary>What the switch is called, for the log.</summary>
    string Name { get; }

    /// <summary>Whether the player has asked for it.</summary>
    bool WantedBy(AuxSettings settings);

    /// <summary>Makes the game match <paramref name="wanted"/>.</summary>
    /// <remarks>
    /// Called on every pass rather than only when the switch moves, because some of what
    /// these write is data the client rewrites for its own reasons.
    /// </remarks>
    /// <returns>Whether the game now matches. False means it was left alone, and why is logged.</returns>
    bool Apply(RemoteProcess process, bool wanted);

    /// <summary>
    /// Applies the switch, with the rest of the settings to hand.
    /// </summary>
    /// <remarks>
    /// Almost every toggle is one switch and one address, and does not need this. The one
    /// that does is the damage display, whose second switch decides where the numbers go
    /// rather than whether they appear — which is not a second toggle, because on its own
    /// it would have nothing to move.
    /// </remarks>
    bool Apply(RemoteProcess process, AuxSettings settings) => Apply(process, WantedBy(settings));
}

/// <summary>
/// A toggle that is a fixed few bytes at a fixed address, in one of two states.
/// </summary>
/// <remarks>
/// <para>
/// Both of the toggles built this way are one address that reads as either what the client
/// shipped or what the launcher writes. Anything else there is somebody else's patch, or a
/// client this port has not seen — either way it is not this toggle's to overwrite, and it
/// is certainly not this toggle's to "restore".
/// </para>
/// <para>
/// The applied state is not remembered. It is read from the game every pass, which is what
/// makes this work for a flag the client also writes — and what stops one game's toggle
/// from deciding another game has already been dealt with. The reference kept it in a
/// process-global, so with two clients running the second one silently got nothing.
/// </para>
/// </remarks>
public abstract class ByteToggle : IGameToggle
{
    private readonly ILogger _logger;
    private bool _reported;

    protected ByteToggle(ILogger logger) => _logger = logger;

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract bool WantedBy(AuxSettings settings);

    /// <summary>
    /// Where the switch lives.
    /// </summary>
    /// <remarks>
    /// Public, along with the two states and whether they are instructions. These are facts
    /// about a particular client build rather than anything private — they belong in the log
    /// when a toggle refuses to act, and they are the part a client update would break.
    /// </remarks>
    public abstract GameAddress Address { get; }

    /// <summary>What is there when the toggle is off — the bytes the client shipped.</summary>
    public abstract ReadOnlySpan<byte> SwitchedOff { get; }

    /// <summary>What the launcher writes when it is on.</summary>
    public abstract ReadOnlySpan<byte> SwitchedOn { get; }

    /// <summary>
    /// Whether the address holds instructions rather than data.
    /// </summary>
    /// <remarks>
    /// Instructions need the page made writable and the instruction cache flushed
    /// afterwards; without the flush the change sometimes does not take, which is the worst
    /// kind of fault to chase in a running game.
    /// </remarks>
    public virtual bool IsCode => true;

    /// <summary>
    /// Whether the client keeps this value up to date itself, for its own reasons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A patched instruction has an original, and switching off means putting it back. A
    /// flag the client rewrites whenever the player enters or leaves water has no
    /// original: whatever it holds is what the map calls for, and writing the "off" value
    /// over it is not a restore but a change.
    /// </para>
    /// <para>
    /// That is not hypothetical. The sea-water flag reads 0 on a map with no sea in it,
    /// and a switched-off pump wrote 1 over that twice a second — so dry ground grew
    /// water, and ticking the box was what made the game look right. Where this is true,
    /// off means writing nothing at all.
    /// </para>
    /// <para>
    /// Nothing is remembered about what was written, deliberately. Undoing "our own" write
    /// needs a rule for what to put back, and there is none: the launcher cannot tell
    /// whether this map's own value is the one it overwrote or the other one. Leaving it
    /// costs the player nothing they will notice — the client sets the flag again the next
    /// time they touch water, which is the only place the flag is visible.
    /// </para>
    /// </remarks>
    public virtual bool ClientOwned => false;

    /// <inheritdoc/>
    public bool Apply(RemoteProcess process, bool wanted)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!wanted && ClientOwned)
        {
            return true;
        }

        var target = wanted ? SwitchedOn : SwitchedOff;
        Span<byte> current = stackalloc byte[target.Length];

        if (!process.TryReadBytes(Address, current))
        {
            return Report($"{Name}: {Address} could not be read.");
        }

        if (current.SequenceEqual(target))
        {
            _reported = false;

            return true;
        }

        // Neither state means somebody else has been here, or this is a client build the
        // port has not seen. Writing over it would be a guess, and writing the "original"
        // over it would be worse.
        if (!current.SequenceEqual(wanted ? SwitchedOff : SwitchedOn))
        {
            return Report(
                $"{Name}: {Address} reads {BytePattern.Format(current)}, which is neither of its two states.");
        }

        if (IsCode)
        {
            process.WriteCode(Address, target);
        }
        else
        {
            process.WriteBytes(Address, target);
        }

        _logger.LogInformation("{Toggle} switched {State}", Name, wanted ? "on" : "off");
        _reported = false;

        return true;
    }

    /// <summary>Logs once per spell of trouble rather than once per pass.</summary>
    /// <remarks>
    /// These run several times a second for the length of a session. A toggle whose address
    /// has moved would otherwise fill the log with the same line and bury everything else.
    /// </remarks>
    private bool Report(string message)
    {
        if (!_reported)
        {
            _logger.LogWarning("{Message}", message);
            _reported = true;
        }

        return false;
    }
}
