using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Login38.Aux.Notifications;
using Login38.Aux.Runtime;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.App.Notifications;

/// <summary>
/// Keeps the overlay over the game and fed.
/// </summary>
/// <remarks>
/// <para>
/// The only part of the helper loop that lives in the launcher's own project rather than
/// in the library, because it is the only one that draws. Everything it draws was decided
/// elsewhere: it takes a snapshot of the board, works out whether the game is in a state
/// worth drawing over, and hands both to the interface thread.
/// </para>
/// <para>
/// Nothing is drawn while the player is looking at something else. A toast hanging over
/// another application, or over the desktop after the game was minimised, is worse than no
/// toast at all.
/// </para>
/// </remarks>
public sealed class OverlayTask : IAuxTask, IAuxTaskShutdown, IDisposable
{
    private readonly NotificationBoard _board;
    private readonly SpriteArtwork _artwork;
    private readonly ILogger<OverlayTask> _logger;
    private readonly AtsMark _mark;

    private NotificationOverlay? _overlay;
    private GameWindow? _window;
    private bool _looked;

    public OverlayTask(NotificationBoard board, SpriteArtwork artwork, ILogger<OverlayTask> logger)
    {
        _board = board;
        _artwork = artwork;
        _logger = logger;
        _mark = new AtsMark(logger);
    }

    /// <inheritdoc/>
    public string Name => "overlay";

    /// <summary>Every pass of the loop.</summary>
    /// <remarks>
    /// The window's own drawing is what has to look smooth, and that happens on the
    /// interface thread from the snapshot handed over here. This only has to keep up with
    /// the game being moved, resized or tabbed away from.
    /// </remarks>
    public TimeSpan Interval => AuxHost.Cadence;

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var misc = context.Settings.Misc;

        // The hunt's own mark, which is not a notification and is not switched by either of
        // the notification settings. It is on for exactly as long as the hunt is, and only
        // in the world — a mark over the character select screen would be a lie.
        var running = context.Settings.Hunt.Enabled && context.IsInWorld;

        // Said before anything else and whatever else happens on this pass, because the game
        // draws it from here on and the overlay may have nothing to do at all.
        _mark.Set(context.Process.Id, running);

        // Only the launcher's copy of it. Once the game is drawing the mark itself, drawing a
        // second one over the top is two marks.
        var hunting = running && !_mark.IsLive;

        if (!hunting && !misc.PickupToast && !misc.GainDrift)
        {
            Hide();

            return;
        }

        var board = _board.Snapshot();

        // Read before the artwork is: a player who never picks anything up never has the
        // client's archives opened, and never has a window made for them.
        if (board.IsEmpty && !hunting)
        {
            Hide();

            return;
        }

        Ready(context.Process.Id);

        _window ??= GameWindow.Find(context.Process.Id);

        var where = _window is null
            ? null
            : OverlayPlacement.Where(
                new OverlayPlacement.WindowState(
                    _window.IsVisible, _window.IsMinimised, _window.IsForeground, _window.ClientArea()),
                anythingToShow: !board.IsEmpty || hunting);

        if (where is not { } area)
        {
            Hide();

            return;
        }

        Post(overlay => overlay.Draw(area, board, _board.Elapsed, hunting));
    }

    /// <inheritdoc/>
    /// <remarks>Nothing is made here: a window that was never needed is not created to close.</remarks>
    public void Stopping()
    {
        _mark.Stop();

        if (_overlay is null)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            _overlay?.Close();
            _overlay = null;
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The block the game reads is the only thing here that is not the launcher's own memory.
    /// <see cref="Stopping"/> is what normally lets go of it; this is for a host that takes
    /// its tasks apart without stopping them.
    /// </remarks>
    public void Dispose() => _mark.Dispose();

    /// <summary>Takes it off the screen, if there is one.</summary>
    private void Hide()
    {
        if (_overlay is not null)
        {
            Post(overlay => overlay.Conceal());
        }
    }

    /// <summary>
    /// Reads the client's own artwork, once, from wherever it is installed.
    /// </summary>
    /// <remarks>
    /// Off the game's own module path rather than out of the launcher's settings: the game
    /// this is drawing over is the one that says where its archives are, and a launcher
    /// pointed at one install while driving a client from another would otherwise show the
    /// wrong pictures.
    /// </remarks>
    private void Ready(uint processId)
    {
        if (_looked)
        {
            return;
        }

        _looked = true;

        try
        {
            using var game = Process.GetProcessById((int)processId);

            if (Path.GetDirectoryName(game.MainModule?.FileName) is { Length: > 0 } directory)
            {
                _artwork.Load(directory);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException
                                  or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(e, "The client's own artwork could not be found; drawing plainly");
        }
    }

    /// <summary>
    /// Hands work to the interface thread.
    /// </summary>
    /// <remarks>
    /// The window is created there too, on the first pass that has something to do — so a
    /// player who never turns this on never has one made.
    /// </remarks>
    private void Post(Action<NotificationOverlay> work)
    {
        var application = Application.Current;

        if (application is null)
        {
            return;
        }

        application.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _overlay ??= new NotificationOverlay(_artwork);

            work(_overlay);
        });
    }
}
