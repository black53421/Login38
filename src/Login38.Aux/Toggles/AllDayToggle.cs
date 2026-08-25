using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Toggles;

/// <summary>
/// Keeps the world lit as if it were noon.
/// </summary>
/// <remarks>
/// <para>
/// Eleven places, not one. The client works out how bright the world should be in several
/// independent steps — a light-level calculator, a daylight test, a palette darkener, a
/// separate indoor path for caves, a dark-scene overlay — and forcing only the first of
/// them leaves the rest still darkening what it produced. Weather goes with it: rain, snow
/// and fog cut visibility whatever the light level says.
/// </para>
/// <para>
/// Every site is read before any of them is written. The reference applied them one at a
/// time and hand-wrote an unwind cascade after each, eight levels deep and quadratic in
/// length; reading first means there is usually nothing to unwind, because the common
/// reason to stop — a client build these addresses do not fit — is known before the first
/// write.
/// </para>
/// <para>
/// Switching off needs more than putting the bytes back. Turning on also writes state the
/// client caches — the cave flag and the active palette — and the client has no reason to
/// recompute either until the player changes map. So switching off asks the client to
/// recompute them, using its own routines, on a thread of its own.
/// </para>
/// </remarks>
public sealed class AllDayToggle : IGameToggle
{
    /// <summary>The light-level calculator, replaced by <c>return 15</c>.</summary>
    /// <remarks>
    /// It normally returns 5 to 15 from the current game-time object. Fifteen is the
    /// brightest the rest of the client knows how to render.
    /// </remarks>
    internal static readonly GameAddress BrightnessCalculator = new(0x00786D70);

    /// <summary>The weather renderer, replaced by an immediate <c>ret</c>.</summary>
    internal static readonly GameAddress WeatherRenderer = new(0x004ED890);

    /// <summary>"Is it daytime", replaced by <c>return true</c>.</summary>
    internal static readonly GameAddress DaylightCheck = new(0x00787040);

    /// <summary>Where the client caches whether the player is somewhere unlit.</summary>
    internal static readonly GameAddress CaveDarkFlag = new(0x009ABCEF);

    /// <summary>Rain, snow and fog intensity.</summary>
    /// <remarks>
    /// Cleared when the toggle goes on. The renderer is already short-circuited by then,
    /// so this is only so that anything else reading them agrees with what is on screen.
    /// </remarks>
    internal static readonly GameAddress[] WeatherState =
    [
        new(0x00ABF324),
        new(0x00ABF328),
        new(0x00ABF8A4),
    ];

    /// <summary>How far the active palette is currently darkened.</summary>
    internal static readonly GameAddress PaletteDarkLevel = new(0x00BDC9D0);

    /// <summary>The palette table, which holds one palette per light level.</summary>
    internal static readonly GameAddress PaletteTablePointer = new(0x00BDC9D4);

    /// <summary>The brightest palette in the table.</summary>
    internal const uint PaletteMaxLightSource = 0x150;

    /// <summary>The one the renderer actually reads.</summary>
    internal const uint PaletteActiveDestination = 0x50;

    /// <summary>256 entries of one byte.</summary>
    internal const int PaletteCopyLength = 0x100;

    /// <summary>How long to give the client to recompute its palette.</summary>
    internal static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Every byte this toggle changes, in the order it writes them.
    /// </summary>
    /// <remarks>
    /// The order matters only for putting them back after a write fails part way, which is
    /// why it is one list rather than eleven fields.
    /// </remarks>
    internal static readonly Site[] Sites =
    [
        new("brightness calculator", BrightnessCalculator,
            [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x24, 0x8B, 0x45, 0x08, 0x50, 0xE8, 0x81, 0xA1, 0xE0, 0xFF, 0x83],
            [0xB8, 0x0F, 0x00, 0x00, 0x00, 0xC3, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90]),

        new("weather renderer", WeatherRenderer, [0x55], [0xC3]),

        new("daylight check", DaylightCheck, [0x55, 0x8B, 0xEC], [0xB0, 0x01, 0xC3]),

        // The darkener takes the level to darken by as an argument. Zeroing the argument
        // where it is loaded leaves the routine itself alone, which matters because the
        // client calls it from more than one place.
        new("palette darken argument", new GameAddress(0x0057E6F7), [0x8B, 0x45, 0x08], [0x31, 0xC0, 0x90]),
        new("palette darken cache argument", new GameAddress(0x0057E707), [0x8B, 0x4D, 0x08], [0x31, 0xC9, 0x90]),

        // Two places decide the player is somewhere unlit — a map id above 0x4000, and a
        // tileset in a table of them. Both write the same flag.
        new("cave flag from map id", new GameAddress(0x004EA514), [0x01], [0x00]),
        new("cave flag from tileset", new GameAddress(0x004EA551), [0x01], [0x00]),

        // Indoors the client forces light level 1 rather than computing one.
        new("indoor light level", new GameAddress(0x004EA6D4), [0x01], [0x0F]),

        // A branch that skips the palette refresh when the light level has not changed.
        // With the level forced it never changes, so without this the refresh never runs.
        new("light recompute skip", new GameAddress(0x004EAD19),
            [0x0F, 0x8D, 0x2B, 0x04, 0x00, 0x00], [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]),

        // The dark-scene overlay, jumped over.
        new("environment overlay", new GameAddress(0x004F0E92),
            [0x83, 0x3D, 0xF0, 0xC9, 0xBD, 0x00, 0x00], [0xE9, 0xA6, 0x00, 0x00, 0x00, 0x90, 0x90]),

        // The last place a light level reaches the renderer: load a local, replaced by
        // pushing 15 and popping it into the same register.
        new("final light argument", new GameAddress(0x004F037C), [0x8B, 0x55, 0xAC], [0x6A, 0x0F, 0x5A]),
    ];

    private readonly ILogger<AllDayToggle> _logger;
    private readonly Site[] _sites;
    private readonly CachedLighting _cached;
    private bool _reported;

    public AllDayToggle(ILogger<AllDayToggle> logger) : this(logger, Sites, new CachedLighting(logger))
    {
    }

    /// <summary>
    /// For the tests, which have no client to write to.
    /// </summary>
    /// <remarks>
    /// The cached-lighting half is separate because half of it runs code inside the game.
    /// Against anything that is not the client those addresses are somebody else's, and a
    /// test that reached them would not fail — it would crash whatever it was pointed at.
    /// </remarks>
    internal AllDayToggle(ILogger<AllDayToggle> logger, Site[] sites, CachedLighting cached)
    {
        _logger = logger;
        _sites = sites;
        _cached = cached;
    }

    /// <inheritdoc/>
    public string Name => "all-day";

    /// <inheritdoc/>
    public bool WantedBy(AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Misc.AllDay;
    }

    /// <summary>One place this toggle writes, and the two states it knows.</summary>
    internal sealed record Site(string What, GameAddress Address, byte[] SwitchedOff, byte[] SwitchedOn)
    {
        /// <summary>The bytes for a given state.</summary>
        public byte[] For(bool on) => on ? SwitchedOn : SwitchedOff;

        /// <summary>How many bytes this site is. Both states are the same length.</summary>
        public int Length => SwitchedOff.Length;
    }

    /// <inheritdoc/>
    public bool Apply(RemoteProcess process, bool wanted)
    {
        ArgumentNullException.ThrowIfNull(process);

        var current = new byte[_sites.Length][];

        for (var i = 0; i < _sites.Length; i++)
        {
            var site = _sites[i];
            var bytes = new byte[site.Length];

            if (!process.TryReadBytes(site.Address, bytes))
            {
                return Report($"{Name}: the {site.What} at {site.Address} could not be read.");
            }

            // Neither state means somebody else has been here, or this is a client build
            // the port has not seen. Nothing has been written yet, so there is nothing to
            // put back — which is the reason every site is read before any is written.
            if (!bytes.AsSpan().SequenceEqual(site.SwitchedOff) && !bytes.AsSpan().SequenceEqual(site.SwitchedOn))
            {
                return Report(
                    $"{Name}: the {site.What} at {site.Address} reads {BytePattern.Format(bytes)}, " +
                    "which is neither of its two states.");
            }

            current[i] = bytes;
        }

        var changed = WriteSites(process, current, wanted);

        if (changed < 0)
        {
            return false;
        }

        _reported = false;

        if (changed == 0)
        {
            return true;
        }

        // The state the client caches, which the patched code has no reason to revisit.
        if (wanted)
        {
            _cached.ForceBrightest(process);
        }
        else
        {
            _cached.Recompute(process);
        }

        _logger.LogInformation("{Toggle} switched {State}", Name, wanted ? "on" : "off");

        return true;
    }

    /// <summary>
    /// Brings every site to the wanted state.
    /// </summary>
    /// <returns>How many sites were written, or -1 if one of them failed.</returns>
    private int WriteSites(RemoteProcess process, byte[][] current, bool wanted)
    {
        var written = 0;

        for (var i = 0; i < _sites.Length; i++)
        {
            var site = _sites[i];
            var target = site.For(wanted);

            if (current[i].AsSpan().SequenceEqual(target))
            {
                continue;
            }

            try
            {
                process.WriteCode(site.Address, target);
                written++;
            }
            catch (GameProcessException e)
            {
                Unwind(process, _sites, current, i);
                Report($"{Name}: the {site.What} at {site.Address} could not be written: {e.Message}");

                return -1;
            }
        }

        return written;
    }

    /// <summary>
    /// Puts back what was there, for the sites written before one failed.
    /// </summary>
    /// <remarks>
    /// A half-patched client renders worse than an unpatched one — some steps forced
    /// bright, the rest still darkening — so this is worth doing even though the write
    /// that failed suggests the next one will too. Failures here are not reported: the
    /// caller is already reporting why it is unwinding, and a second line about the same
    /// dead process says nothing.
    /// </remarks>
    internal static void Unwind(RemoteProcess process, Site[] sites, byte[][] current, int failedAt)
    {
        for (var i = failedAt - 1; i >= 0; i--)
        {
            try
            {
                process.WriteCode(sites[i].Address, current[i]);
            }
            catch (GameProcessException)
            {
            }
        }
    }

    /// <summary>Logs once per spell of trouble rather than once per pass.</summary>
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
