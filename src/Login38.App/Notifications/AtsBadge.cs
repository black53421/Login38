using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Login38.Aux.Notifications;

namespace Login38.App.Notifications;

/// <summary>
/// The classic client's auto-hunt mark, drawn in the middle of the game's picture.
/// </summary>
/// <remarks>
/// <para>
/// Two sprites and two curves, all four taken from the classic client rather than invented:
/// <c>ATSMsgHudLayout.csb</c> holds <c>Ico_Play</c> (<c>Common_ATS_Ico_Ing_01.png</c>, the
/// lettering) and <c>Ico_Rotate</c> (<c>Common_ATS_Ico_Ing_02.png</c>, the ring), and one
/// timeline named <c>Playing_loop</c> driving an <c>Alpha</c> track on the first and a
/// <c>RotationSkew</c> track on the second.
/// </para>
/// <para>
/// The tracks read, out of the layout's own frames: rotation 0° at frame 0 and 360° at frame
/// 120, alpha 255 at frame 0, 127 at frame 60 and 255 again at frame 120. Cocos Studio
/// timelines run at sixty frames a second, so both are a two-second loop — the ring turns
/// once while the lettering breathes once.
/// </para>
/// <para>
/// Drawn at the size it would be on the screen it was authored for. The classic HUD is laid
/// out for 1280x960, so on a shorter picture the mark comes down with everything else around
/// it — at native size on a 600-tall window it is nearly twice the badge the classic client
/// would show, which is what "it looks too big" is.
/// </para>
/// </remarks>
internal sealed class AtsBadge
{
    /// <summary>How long one turn of the ring takes, from the layout's own frame numbers.</summary>
    private static readonly TimeSpan Loop = TimeSpan.FromSeconds(2);

    /// <summary>The alpha the lettering falls to half way round the loop, out of 255.</summary>
    private const double Dimmest = 127.0 / 255.0;

    /// <summary>The height the classic HUD, and this mark with it, is laid out for.</summary>
    private const double Designed = 960;

    /// <summary>
    /// How much smaller than the classic size the mark is allowed to become.
    /// </summary>
    /// <remarks>
    /// A floor rather than a free proportion, because below about half the lettering stops
    /// being lettering and turns into three grey marks.
    /// </remarks>
    private const double Smallest = 0.5;

    private readonly BitmapImage? _letters;
    private readonly BitmapImage? _ring;

    internal AtsBadge()
    {
        _letters = Load("Common_ATS_Ico_Ing_01.png");
        _ring = Load("Common_ATS_Ico_Ing_02.png");
    }

    /// <summary>Whether there is anything to draw at all.</summary>
    internal bool IsReady => _letters is not null && _ring is not null;

    /// <summary>Draws one frame of it, across the middle and above the client's own bar.</summary>
    /// <param name="now">
    /// How long the overlay has been running. Only the position within the loop is used, so
    /// the mark neither restarts nor jumps when the hunt is switched on and off.
    /// </param>
    internal void Draw(DrawingContext drawing, double width, double height, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        if (_letters is null || _ring is null)
        {
            return;
        }

        var through = now.Ticks % Loop.Ticks / (double)Loop.Ticks;
        var scale = Math.Clamp(height / Designed, Smallest, 1);

        // Across the middle, and standing on the line the bottom bar starts at rather than in
        // the middle of the picture — where the character is, and where it would be in the
        // way of the one thing the player is watching.
        var floor = NotificationLayout.MarkBottomY((int)height);
        var middle = new Point(width / 2, floor - (_ring.PixelHeight * scale / 2));

        // The ring first and under, which is the order the layout lists them in and is what
        // puts the lettering inside the circle rather than behind it.
        drawing.PushTransform(new RotateTransform(through * 360, middle.X, middle.Y));
        drawing.DrawImage(_ring, Around(middle, _ring, scale));
        drawing.Pop();

        drawing.PushOpacity(Breath(through));
        drawing.DrawImage(_letters, Around(middle, _letters, scale));
        drawing.Pop();
    }

    /// <summary>
    /// The alpha of the lettering at a point in the loop.
    /// </summary>
    /// <remarks>
    /// Straight lines between the layout's three keys, because that is what a Cocos timeline
    /// does between frames unless the frame says otherwise, and these do not.
    /// </remarks>
    private static double Breath(double through) =>
        through < 0.5
            ? 1 - ((1 - Dimmest) * (through * 2))
            : Dimmest + ((1 - Dimmest) * ((through - 0.5) * 2));

    /// <summary>A sprite centred on a point, at a fraction of its own size.</summary>
    private static Rect Around(Point middle, BitmapImage sprite, double scale)
    {
        var width = sprite.PixelWidth * scale;
        var height = sprite.PixelHeight * scale;

        return new Rect(middle.X - (width / 2), middle.Y - (height / 2), width, height);
    }

    /// <summary>
    /// One of the two sprites, out of the launcher's own resources.
    /// </summary>
    /// <returns>Null if it is not there, which leaves the mark undrawn rather than crashing.</returns>
    private static BitmapImage? Load(string name)
    {
        try
        {
            var image = new BitmapImage();

            image.BeginInit();
            // Named with the assembly it lives in rather than by the short form. The short
            // form resolves against whichever assembly started the process, which is this one
            // in the launcher and is not in a test — and a mark that silently fails to load is
            // exactly the failure worth having a test for.
            //
            // Asked of the assembly rather than written down: this project's name and the
            // name it builds under are not the same word, and the one a pack URI wants is the
            // second.
            var assembly = typeof(AtsBadge).Assembly.GetName().Name;

            image.UriSource = new Uri(
                $"pack://application:,,,/{assembly};component/Assets/Ats/{name}",
                UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
