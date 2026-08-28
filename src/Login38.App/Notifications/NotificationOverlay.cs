using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Login38.Aux.Notifications;
using Login38.Interop;

namespace Login38.App.Notifications;

/// <summary>
/// A transparent window over the game, showing what was picked up and what a kill was worth.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here touches the game. The reference's own notes record that it tried drawing
/// inside the client first — a detour on one of its rendering methods — and found the method
/// stops being called once a character is in the world, which is the only time any of this
/// matters. A window of the launcher's own, kept over the client's picture, has none of that
/// problem and cannot take the game down.
/// </para>
/// <para>
/// It is click-through, never activates, and stays out of the task bar and the alt-tab list.
/// A player must not be able to end up with the focus on it.
/// </para>
/// </remarks>
internal sealed class NotificationOverlay : Window
{
    private readonly OverlaySurface _surface;

    internal NotificationOverlay(SpriteArtwork artwork)
    {
        _surface = new OverlaySurface(artwork);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Title = "天堂 3.8 提示";
        Content = _surface;
    }

    /// <summary>Puts it over the game's picture and draws a frame.</summary>
    /// <param name="area">Where the game's picture is, in real pixels.</param>
    internal void Draw(ScreenArea area, BoardSnapshot board, TimeSpan now, bool hunting)
    {
        var scale = FromDevice();

        // The window is placed in the units WPF measures in, which are the desktop's
        // pixels divided by its scaling. The game reports real ones.
        var left = area.X * scale.M11;
        var top = area.Y * scale.M22;
        var width = area.Width * scale.M11;
        var height = area.Height * scale.M22;

        if (Math.Abs(Left - left) > 0.5 || Math.Abs(Top - top) > 0.5)
        {
            Left = left;
            Top = top;
        }

        if (Math.Abs(Width - width) > 0.5 || Math.Abs(Height - height) > 0.5)
        {
            Width = width;
            Height = height;
        }

        _surface.Show(board, now, hunting);

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>Takes it off the screen without throwing away what it has read.</summary>
    internal void Conceal()
    {
        // Before the window goes, because a hidden window still has a surface and the
        // surface is what asked the compositor for a frame every frame.
        _surface.Rest();

        if (IsVisible)
        {
            Hide();
        }
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Only now is there a handle to make click-through. Doing it before the window is
        // shown means the player never sees a frame of it swallowing their clicks.
        OverlayWindow.MakeUntouchable(new WindowInteropHelper(this).Handle);
    }

    /// <summary>How many of WPF's units one desktop pixel is.</summary>
    private Matrix FromDevice() =>
        PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
}
