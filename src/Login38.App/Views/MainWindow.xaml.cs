using System.ComponentModel;
using System.Windows;
using Login38.App.Services;
using Login38.App.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui.Controls;

namespace Login38.App.Views;

/// <summary>
/// The launcher window.
/// </summary>
/// <remarks>
/// <para>
/// It leaves the screen once a game is up. That is what a launcher is expected to do, and
/// what the reference did by exiting — which is not available at that moment here, because
/// the patching and every helper feature run in this process and poll the client from
/// outside. So it withdraws to the notification area instead: gone from in front of the
/// game, still there to come back to.
/// </para>
/// <para>
/// When the game ends it does exit, which is the same thing one step later. Nothing here
/// outlives the client it was started for.
/// </para>
/// <para>
/// The close button does the same thing while a game is running rather than ending the
/// process. A player who closed the launcher and lost their potions would have no way of
/// knowing the two were connected.
/// </para>
/// </remarks>
public partial class MainWindow : FluentWindow, IDisposable
{
    private readonly MainViewModel _model;
    private readonly ILauncherTray? _tray;

    /// <param name="model">What the window shows.</param>
    /// <param name="loggers">
    /// Where the notification area reports to. Null leaves the launcher on screen — which
    /// is what the layout tests want, having no notification area to withdraw into.
    /// </param>
    /// <param name="tray">
    /// A notification area to use instead of the real one. For the tests that drive what
    /// this window does when a game starts and ends, which is the one part of it that puts
    /// an icon beside somebody's clock and takes the launcher off their screen.
    /// </param>
    public MainWindow(MainViewModel model, ILoggerFactory? loggers = null, ILauncherTray? tray = null)
    {
        _model = model;
        DataContext = model;

        InitializeComponent();

        _tray = tray ?? (loggers is null
            ? null
            : new LauncherTray(this, loggers.CreateLogger<LauncherTray>()));

        model.PropertyChanged += OnModelChanged;
    }

    /// <summary>
    /// Reads the server list once the window is up.
    /// </summary>
    /// <remarks>
    /// Not in the constructor: reading the list touches the disk and then probes every
    /// server over the network, and doing that before the window is shown would leave the
    /// player looking at nothing for a second or two.
    /// </remarks>
    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        await _model.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Withdraws instead of closing while a game is running.
    /// </summary>
    /// <remarks>
    /// The only way out from here is the notification area's own menu, which says what it
    /// costs. Closing the launcher stops the helper, and nothing about a window's close
    /// button says that.
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnClosing(e);

        if (_model.IsGameRunning && _tray is not null)
        {
            e.Cancel = true;
            _tray.Withdraw(_model.TrayCaption);

            return;
        }

        // Nothing is running, so this really is the end of the session. Ending it is the
        // application's decision, not this window's: whoever showed the window is the only
        // one who knows whether it was the launcher or something that merely wanted to
        // look at one.
        _model.PropertyChanged -= OnModelChanged;
        Dispose();
    }

    /// <summary>Takes the icon out of the notification area.</summary>
    /// <remarks>
    /// A window that owns something the operating system is holding on its behalf, which
    /// an icon beside the clock is: one left behind stays there until the shell notices
    /// the process has gone, which can be until the pointer next passes over it.
    /// </remarks>
    public void Dispose()
    {
        _tray?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsGameRunning) || _tray is null)
        {
            return;
        }

        if (_model.IsGameRunning)
        {
            _tray.Withdraw(_model.TrayCaption);

            return;
        }

        // The game ended, and with it the only reason this process was still here. The
        // patches are applied or missed, the helper has stopped, and there is nothing left
        // to poll — so the launcher goes too, rather than putting a window back over
        // whatever the player turned to.
        //
        // It came back once, and that was worse than it sounds: a window appearing and
        // taking the foreground at the exact moment the client is giving up its display
        // mode is a visible stutter on the way out of the game.
        //
        // Closing is how this is said. Ending the session is the application's decision,
        // and the application is listening for this window to close.
        Close();
    }
}
