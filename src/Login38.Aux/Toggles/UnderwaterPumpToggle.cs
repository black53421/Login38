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
/// The client writes this flag when the player enters water, and leaves it alone
/// otherwise. So the pump writes once on the way in and puts back what it found on the way
/// out, rather than asserting a value every pass.
/// </para>
/// <para>
/// Both halves of that were wrong at some point and both were visible from inside the
/// game. Asserting the off value every pass wrote 1 onto maps with no sea in them, where
/// the client had left 0 — dry ground grew water, and ticking the box was what made the
/// game look right. Writing nothing at all on the way out fixed that and broke the other
/// half: the water never came back until the player next touched some.
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
