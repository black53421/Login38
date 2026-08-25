using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Toggles;

/// <summary>
/// Takes the water off the screen while the player is under it.
/// </summary>
/// <remarks>
/// <para>
/// Underwater the client draws a blue wash over everything, which is atmospheric and makes
/// it hard to see what is happening. One flag decides whether that layer is drawn.
/// </para>
/// <para>
/// The client writes this flag itself, on entering and leaving water — which is why the
/// state is read back and re-asserted every pass rather than written once when the switch
/// moves, and why switching off does not write the other value over whatever is there.
/// </para>
/// <para>
/// That second part was missing, and it was visible from inside the game. On a map with no
/// sea in it the client leaves the flag at 0, and a switched-off pump wrote 1 over that
/// twice a second: water appeared on dry ground, and ticking the box was what made the
/// game look right. The reference guarded against this and the guard did not survive the
/// port — it was a process-wide static, which had its own fault, and dropping the static
/// dropped the guard with it.
/// </para>
/// </remarks>
public sealed class UnderwaterPumpToggle : ByteToggle
{
    public UnderwaterPumpToggle(ILogger<UnderwaterPumpToggle> logger) : base(logger)
    {
    }

    /// <inheritdoc/>
    public override string Name => "underwater-pump";

    /// <inheritdoc/>
    public override bool WantedBy(AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Misc.UnderwaterPump;
    }

    /// <summary>Whether the water layer is drawn.</summary>
    public override GameAddress Address => new(0x009A_B646);

    /// <inheritdoc/>
    public override ReadOnlySpan<byte> SwitchedOff => [1];

    /// <inheritdoc/>
    public override ReadOnlySpan<byte> SwitchedOn => [0];

    /// <summary>Data the client writes, not an instruction.</summary>
    public override bool IsCode => false;

    /// <summary>
    /// The client decides this per map, so off means leaving it to the client.
    /// </summary>
    public override bool ClientOwned => true;
}
