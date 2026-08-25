using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Login38.Aux.Notifications;
using Microsoft.Extensions.Logging;

namespace Login38.App.Notifications;

/// <summary>
/// The client's own artwork, as something WPF can draw.
/// </summary>
/// <remarks>
/// <para>
/// The formats live in <c>Login38.Aux</c>: the archive index, the item picture format, and
/// the pixel arithmetic that recovers transparency. What is here is only the bridge — PNG,
/// which the framework already understands, and the conversion to a bitmap source.
/// </para>
/// <para>
/// The reference hand-writes a PNG decoder call chain and then a GDI DIB blitter for each
/// of grayscale-as-mask, straight RGBA, and a colour-keyed fallback. None of that is the
/// game's business: only the two formats the framework does not know about are.
/// </para>
/// </remarks>
public sealed class SpriteArtwork
{
    /// <summary>The frame drawn around an item's picture, and the size of one row.</summary>
    private const string FrameFile = "1888.png";

    /// <summary>The bar a pickup's name is written on.</summary>
    private const string BarFile = "1889.png";

    /// <summary>
    /// The experience and coin marks, each shipped as a pair drawn on black and on white.
    /// </summary>
    private static readonly (string Black, string White) ExperienceFiles = ("1971.png", "1970.png");

    /// <inheritdoc cref="ExperienceFiles"/>
    private static readonly (string Black, string White) GoldFiles = ("1973.png", "1972.png");

    /// <summary>How much of an archive entry is read for a picture.</summary>
    private const int MostPerPicture = 1 << 20;

    /// <summary>And for an item icon, which is always small.</summary>
    private const int MostPerIcon = 64 * 1024;

    private readonly SpriteIndex _index;
    private readonly ILogger<SpriteArtwork> _logger;
    private readonly ConcurrentDictionary<ushort, ImageSource?> _icons = new();

    public SpriteArtwork(SpriteIndex index, ILogger<SpriteArtwork> logger)
    {
        _index = index;
        _logger = logger;
    }

    /// <summary>Whether the client's archives were found.</summary>
    public bool Ready { get; private set; }

    /// <summary>The frame drawn around an item's picture.</summary>
    public ImageSource? Frame { get; private set; }

    /// <summary>The bar a pickup's name is written on.</summary>
    public ImageSource? Bar { get; private set; }

    /// <summary>The mark beside an experience number.</summary>
    public ImageSource? Experience { get; private set; }

    /// <summary>And beside a coin number.</summary>
    public ImageSource? Gold { get; private set; }

    /// <summary>
    /// Reads the archives beside the client and the four pictures that never change.
    /// </summary>
    /// <remarks>
    /// Missing artwork is not a failure. The overlay draws its own shapes instead, which is
    /// less pretty and entirely legible — and a player whose client is installed somewhere
    /// this cannot read still wants to know what they picked up.
    /// </remarks>
    public void Load(string gameDirectory)
    {
        if (Ready)
        {
            return;
        }

        Ready = _index.Load(gameDirectory);

        if (!Ready)
        {
            return;
        }

        Frame = Freeze(Png(FrameFile)?.KeyedOnBlack());
        Bar = Freeze(Png(BarFile)?.KeyedOnBlack());
        Experience = Freeze(Paired(ExperienceFiles));
        Gold = Freeze(Paired(GoldFiles));

        _logger.LogInformation(
            "Read the client's own artwork: frame={Frame} bar={Bar} experience={Experience} gold={Gold}",
            Frame is not null, Bar is not null, Experience is not null, Gold is not null);
    }

    /// <summary>
    /// The picture for one item, or null while it has not been read.
    /// </summary>
    /// <remarks>
    /// Read once and kept, null included: an item this client has no picture for is asked
    /// about again on every frame of every toast otherwise.
    /// </remarks>
    public ImageSource? Icon(ushort sprite) =>
        sprite == 0 ? null : _icons.GetOrAdd(sprite, ReadIcon);

    private ImageSource? ReadIcon(ushort sprite)
    {
        if (_index.Read($"{sprite}.tbt", MostPerIcon) is not { } raw)
        {
            return null;
        }

        return Freeze(TbtImage.Decode(raw));
    }

    /// <summary>Reads one PNG out of the archives.</summary>
    private SpriteImage? Png(string name)
    {
        if (_index.Read(name, MostPerPicture) is not { } raw)
        {
            return null;
        }

        try
        {
            // The archive entry runs on past the picture, and the decoder stops at its own
            // end marker — which is why the length in the index is never needed.
            using var stream = new MemoryStream(raw, writable: false);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

            var rgba = new byte[converted.PixelWidth * converted.PixelHeight * SpriteImage.Channels];

            converted.CopyPixels(rgba, converted.PixelWidth * SpriteImage.Channels, 0);

            // WPF hands them over blue first; everything on the other side of this is RGBA.
            for (var at = 0; at < rgba.Length; at += SpriteImage.Channels)
            {
                (rgba[at], rgba[at + 2]) = (rgba[at + 2], rgba[at]);
            }

            return new SpriteImage(converted.PixelWidth, converted.PixelHeight, rgba);
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException or FileFormatException)
        {
            _logger.LogWarning(e, "{Name} is in the archives but is not a picture", name);

            return null;
        }
    }

    /// <summary>Reads a pair drawn on black and on white and recovers the transparency.</summary>
    private SpriteImage? Paired((string Black, string White) files)
    {
        var black = Png(files.Black);
        var white = Png(files.White);

        return black is null || white is null ? null : SpriteImage.Paired(black, white);
    }

    /// <summary>Turns one into something WPF can draw from any thread.</summary>
    private static BitmapSource? Freeze(SpriteImage? image)
    {
        if (image is not { IsWholeImage: true } whole)
        {
            return null;
        }

        var bgra = new byte[whole.Rgba.Length];

        for (var at = 0; at < bgra.Length; at += SpriteImage.Channels)
        {
            bgra[at] = whole.Rgba[at + 2];
            bgra[at + 1] = whole.Rgba[at + 1];
            bgra[at + 2] = whole.Rgba[at];
            bgra[at + 3] = whole.Rgba[at + 3];
        }

        var source = BitmapSource.Create(
            whole.Width, whole.Height, 96, 96, PixelFormats.Bgra32, null,
            bgra, whole.Width * SpriteImage.Channels);

        source.Freeze();

        return source;
    }
}
