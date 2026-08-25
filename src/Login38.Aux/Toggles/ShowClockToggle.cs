using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Toggles;

/// <summary>
/// Keeps the in-game clock on screen.
/// </summary>
/// <remarks>
/// <para>
/// The client already draws the time at the bottom of the screen — it just checks first
/// whether the mouse is over that part of the interface, and skips the formatting and the
/// draw when it is not. So the clock exists and is only ever visible while the player is
/// pointing at it.
/// </para>
/// <para>
/// Removing the check leaves the draw running every frame, which is what the player wanted
/// in the first place. Nothing else about the drawing changes.
/// </para>
/// </remarks>
public sealed class ShowClockToggle : ByteToggle
{
    public ShowClockToggle(ILogger<ShowClockToggle> logger) : base(logger)
    {
    }

    /// <inheritdoc/>
    public override string Name => "show-clock";

    /// <inheritdoc/>
    public override bool WantedBy(AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Misc.ShowClock;
    }

    /// <summary>
    /// The branch that skips the clock.
    /// </summary>
    /// <remarks>
    /// <c>movzx ecx, byte [eax+0x48]; test ecx, ecx; je</c> — the byte is the interface
    /// object's own "is the pointer over me" flag, and the jump lands past the formatting
    /// and the draw.
    /// </remarks>
    public override GameAddress Address => new(0x0078_AD50);

    /// <inheritdoc/>
    public override ReadOnlySpan<byte> SwitchedOff => [0x0F, 0x84, 0xA1, 0x00, 0x00, 0x00];

    /// <summary>Six bytes of nothing, so the draw is reached whatever the flag says.</summary>
    /// <remarks>
    /// All six, not five. The remaining byte of a six-byte branch would be the tail of an
    /// instruction and would decode as whatever those bytes happen to be.
    /// </remarks>
    public override ReadOnlySpan<byte> SwitchedOn => [0x90, 0x90, 0x90, 0x90, 0x90, 0x90];
}
