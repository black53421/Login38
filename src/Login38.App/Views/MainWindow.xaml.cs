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
/// what the reference did by exiting — which is not available here, because the patching
/// and every helper feature run in this process and poll the client from outside. So it
/// withdraws to the notification area instead: gone from in front of the game, still there
/// to come back to.
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
    private readonly LauncherTray? _tray;

    /// <param name="model">What the window shows.</param>
    /// <param name="loggers">
    /// Where the notification area reports to. Null leaves the launcher on screen — which
    /// is what the layout tests want, having no notification area to withdraw into.
    /// </param>
    public MainWindow(MainViewModel model, ILoggerFactory? loggers = null)
    {
        _model = model;
        DataContext = model;

        InitializeComponent();

        if (loggers is not null)
        {
            _tray = new LauncherTray(this, loggers.CreateLogger<LauncherTray>());
        }

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
        }
        else if (_tray.IsWithdrawn)
        {
            // The game ended. Coming back is what the player expects — the alternative is
            // an icon beside the clock and no sign that anything happened.
            _tray.Restore();
        }
    }
}
