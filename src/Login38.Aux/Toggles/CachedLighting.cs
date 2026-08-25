using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Toggles;

/// <summary>
/// The lighting the client has already worked out and will not work out again.
/// </summary>
/// <remarks>
/// <para>
/// Patching the eleven places that decide how bright the world is changes what the client
/// will compute next time. It does not change what the client computed last time — the
/// cave flag and the 256 colours the renderer is reading right now — and nothing prompts
/// it to look again until the player changes map.
/// </para>
/// <para>
/// So the two directions are not symmetric. Going bright can be forced from outside, by
/// writing the brightest palette into the slot the renderer reads. Coming back needs a
/// real answer, which depends on the map, a table of tilesets and the time of day — so it
/// has to be the client's own routine, run inside the client.
/// </para>
/// <para>
/// Its own class, and not sealed, because that second half runs code at fixed addresses
/// inside whatever process it is handed. Against anything that is not the client, those
/// addresses belong to somebody else.
/// </para>
/// </remarks>
internal class CachedLighting
{
    private readonly ILogger _logger;

    public CachedLighting(ILogger logger) => _logger = logger;

    /// <summary>
    /// Makes the world bright now rather than at the next map change.
    /// </summary>
    /// <remarks>
    /// Failures are logged and ignored on purpose. The patches are already in, so the world
    /// lights up as soon as the client next recomputes anything; this only spares the
    /// player the wait for that.
    /// </remarks>
    public virtual void ForceBrightest(RemoteProcess process)
    {
        try
        {
            foreach (var address in AllDayToggle.WeatherState)
            {
                process.Write<uint>(address, 0);
            }

            process.Write<byte>(AllDayToggle.CaveDarkFlag, 0);

            var table = process.Read<uint>(AllDayToggle.PaletteTablePointer);

            if (table == 0)
            {
                // Before the first map has loaded. There is no palette to brighten yet, and
                // the one that gets built will be built from the patched code.
                return;
            }

            var brightest = process.ReadBytes(
                new GameAddress(table + AllDayToggle.PaletteMaxLightSource), AllDayToggle.PaletteCopyLength);

            process.WriteBytes(new GameAddress(table + AllDayToggle.PaletteActiveDestination), brightest);
            process.Write<uint>(AllDayToggle.PaletteDarkLevel, 0);
        }
        catch (GameProcessException e)
        {
            _logger.LogInformation(e, "All-day is on, but the screen will not brighten until the next map");
        }
    }

    /// <summary>
    /// Asks the client to work out its own lighting again.
    /// </summary>
    /// <remarks>
    /// Without this, switching the toggle off inside a cave leaves the cave lit until the
    /// player walks out of it and back in.
    /// </remarks>
    public virtual void Recompute(RemoteProcess process)
    {
        try
        {
            RemoteCall.Run(process, PaletteRefresh.Code, AllDayToggle.RefreshTimeout);
        }
        catch (GameProcessException e)
        {
            _logger.LogWarning(e, "All-day is off, but the screen will stay bright until the next map");
        }
    }
}
