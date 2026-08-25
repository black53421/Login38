using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Login38.Aux.Notifications;

namespace Login38.App.Notifications;

/// <summary>
/// Draws one frame of the pickups and the numbers.
/// </summary>
/// <remarks>
/// <para>
/// A single element that renders the whole board, rather than one control per notification.
/// Nothing here is interactive, the contents change several times a second, and the
/// arithmetic that decides where everything goes already exists — so a retained tree of
/// elements would be work done to arrive back at a list of draw calls.
/// </para>
/// <para>
/// Everything it needs is a snapshot and a clock. Both come from the board, so a frame
/// drawn between two passes of the helper loop is still correct: the entries carry when
/// they arrived, not how old they were when somebody last looked.
/// </para>
/// </remarks>
internal sealed class OverlaySurface : FrameworkElement
{
    /// <summary>What the name is written in.</summary>
    private static readonly Typeface Face =
        new(new FontFamily("Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>How tall the name is drawn.</summary>
    private const double NameSize = 14;

    /// <summary>And a drifting number, which is the thing the player looks for.</summary>
    private const double NumberSize = 16;

    /// <summary>What the bar looks like where the client's own artwork is not available.</summary>
    private static readonly Color PlainBar = Color.FromRgb(0x20, 0x20, 0x20);

    /// <summary>The colour of an experience number, taken from the client's own mark.</summary>
    private static readonly Color ExperienceInk = Color.FromRgb(0x60, 0xD7, 0x2E);

    /// <summary>And of a coin number.</summary>
    private static readonly Color GoldInk = Color.FromRgb(0xDF, 0xCD, 0x65);

    private readonly SpriteArtwork _artwork;

    private BoardSnapshot _board;
    private TimeSpan _now;

    internal OverlaySurface(SpriteArtwork artwork)
    {
        _artwork = artwork;
        IsHitTestVisible = false;
    }

    /// <summary>Hands over what to draw. Called on the interface thread.</summary>
    internal void Show(BoardSnapshot board, TimeSpan now)
    {
        _board = board;
        _now = now;

        InvalidateVisual();
    }

    /// <summary>Whether the last snapshot handed over had anything in it.</summary>
    internal bool HasAnythingToDraw => !_board.IsEmpty;

    protected override void OnRender(DrawingContext drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var width = (int)ActualWidth;
        var height = (int)ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        for (var slot = 0; slot < _board.Toasts.Count; slot++)
        {
            var toast = _board.Toasts[slot];

            // Newest nearest the anchor: the board keeps them oldest first, and the stack
            // grows upwards, so the last one in is the one at the bottom.
            Toast(drawing, toast, _board.Toasts.Count - 1 - slot, width, height);
        }

        foreach (var drift in _board.Drifts)
        {
            Drift(drawing, drift, width, height);
        }
    }

    private void Toast(DrawingContext drawing, LiveToast toast, int slot, int width, int height)
    {
        var place = NotificationLayout.Toast(toast.SpawnedAt, slot, width, height, _now);

        if (place.Alpha == 0)
        {
            return;
        }

        var fade = place.Alpha / 255.0;

        drawing.PushOpacity(fade);

        var row = new Rect(place.X, place.Y, NotificationLayout.ToastWidth, NotificationLayout.ToastHeight);

        if (_artwork.Bar is { } bar)
        {
            drawing.DrawImage(bar, row);
        }
        else
        {
            // Something to read the name against, where the client's artwork is not to hand.
            drawing.DrawRectangle(new SolidColorBrush(PlainBar) { Opacity = 0.75 }, null, row);
        }

        var frame = new Rect(place.X, place.Y, NotificationLayout.ToastHeight, NotificationLayout.ToastHeight);

        if (_artwork.Frame is { } around)
        {
            drawing.DrawImage(around, frame);
        }

        // The item's own picture, centred in its frame. Missing means it has not been read
        // yet, and the next frame will have it — an empty frame is better than no row.
        if (_artwork.Icon(toast.Sprite) is { } icon)
        {
            drawing.DrawImage(
                icon,
                new Rect(
                    frame.X + Math.Max(0, (frame.Width - icon.Width) / 2),
                    frame.Y + Math.Max(0, (frame.Height - icon.Height) / 2),
                    Math.Min(icon.Width, frame.Width),
                    Math.Min(icon.Height, frame.Height)));
        }

        var name = Text(toast.Name, NameSize, Brushes.White);

        drawing.DrawText(
            name,
            new Point(
                place.X + NotificationLayout.NameLeft,
                place.Y + ((NotificationLayout.ToastHeight - name.Height) / 2)));

        drawing.Pop();
    }

    private void Drift(DrawingContext drawing, LiveDrift drift, int width, int height)
    {
        var place = NotificationLayout.Drift(drift.SpawnedAt, drift.Kind, width, height, _now);

        if (place.Alpha == 0)
        {
            return;
        }

        var experience = drift.Kind == DriftKind.Experience;
        var mark = experience ? _artwork.Experience : _artwork.Gold;
        var ink = new SolidColorBrush(experience ? ExperienceInk : GoldInk);
        var number = Text($"+{Amounts.WithCommas(drift.Amount)}", NumberSize, ink);

        drawing.PushOpacity(place.Alpha / 255.0);

        var left = (double)place.X;

        if (mark is not null)
        {
            drawing.DrawImage(
                mark, new Rect(left, place.Y - (mark.Height / 2), mark.Width, mark.Height));

            left += mark.Width + 2;
        }

        drawing.DrawText(number, new Point(left, place.Y - (number.Height / 2)));
        drawing.Pop();
    }

    /// <summary>
    /// Lays out one piece of text.
    /// </summary>
    /// <remarks>
    /// At the surface's own scale rather than the system's. This is drawn over a game that
    /// is rendering at its own resolution, and text that grew with the desktop's scaling
    /// would not line up with the artwork beside it.
    /// </remarks>
    private static FormattedText Text(string text, double size, Brush ink) =>
        new(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, ink,
            pixelsPerDip: 1.0);
}
