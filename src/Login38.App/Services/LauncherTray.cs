using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.App.Services;

/// <summary>Puts the launcher in the notification area while a game is running.</summary>
/// <remarks>
/// An interface so the window can be exercised without one being added to a real
/// notification area, which a test host does not have.
/// </remarks>
public interface ILauncherTray : IDisposable
{
    /// <summary>Hides the window and puts an icon beside the clock.</summary>
    /// <param name="tooltip">What hovering over the icon says.</param>
    void Withdraw(string tooltip);

    /// <summary>Brings the window back and takes the icon away.</summary>
    void Restore();

    /// <summary>Whether the launcher is currently in the notification area.</summary>
    bool IsWithdrawn { get; }
}

/// <summary>
/// The launcher's own notification-area icon.
/// </summary>
/// <remarks>
/// <para>
/// Once the game is up, the launcher has nothing more to show and no business sitting in
/// front of it. It goes here instead. Exiting outright is not available: the patch pipeline
/// and every helper feature poll the client from this process, so the process that ended
/// would take the helper with it.
/// </para>
/// <para>
/// The icon rather than nothing at all, because Home is the other way back to the settings
/// and a client that will not take the key would otherwise leave the player with no way to
/// reach the launcher again.
/// </para>
/// </remarks>
public sealed class LauncherTray : ILauncherTray, IDisposable
{
    private readonly Window _window;
    private readonly ILogger<LauncherTray> _logger;
    private readonly ContextMenu _menu;

    private TrayIcon? _icon;
    private HwndSource? _messages;
    private nint _artwork;

    public LauncherTray(Window window, ILogger<LauncherTray> logger)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        _logger = logger;
        _menu = BuildMenu();
    }

    /// <inheritdoc/>
    public bool IsWithdrawn => _icon is not null;

    /// <inheritdoc/>
    public void Withdraw(string tooltip)
    {
        if (_icon is not null)
        {
            _icon.Update(tooltip);

            return;
        }

        // The handle only exists once the window has been shown, which is why this is not
        // done when the launcher starts.
        var handle = new WindowInteropHelper(_window).Handle;

        if (handle == 0)
        {
            _logger.LogWarning("The launcher has no window handle yet; it stays on screen");

            return;
        }

        try
        {
            _artwork = TrayIcon.IconOf(Environment.ProcessPath ?? string.Empty);
            _icon = new TrayIcon(handle, _artwork, tooltip);
        }
        catch (GameProcessException e)
        {
            // Not fatal, and not worth hiding the window over: a launcher with no way back
            // is worse than a launcher that stayed visible.
            _logger.LogWarning(e, "The notification area refused the icon; the launcher stays on screen");

            return;
        }

        _messages = HwndSource.FromHwnd(handle);
        _messages?.AddHook(OnMessage);

        _window.Hide();
        _logger.LogInformation("The launcher withdrew to the notification area");
    }

    /// <inheritdoc/>
    public void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();

        Remove();
    }

    public void Dispose() => Remove();

    /// <summary>The menu the icon offers on a right click.</summary>
    /// <remarks>
    /// Two items. Anything a player might want here they want in the launcher window, and
    /// the second one is the only thing that cannot be done from there once it is hidden.
    /// </remarks>
    private ContextMenu BuildMenu()
    {
        var open = new MenuItem { Header = "開啟登入器" };
        var quit = new MenuItem { Header = "結束登入器(輔助會一起關閉)" };

        open.Click += (_, _) => Restore();
        quit.Click += (_, _) => Application.Current?.Shutdown();

        return new ContextMenu { Items = { open, quit } };
    }

    private nint OnMessage(nint window, int message, nint wparam, nint lparam, ref bool handled)
    {
        switch (TrayIcon.Decode(message, lparam))
        {
            case TrayAction.Open:
                Restore();
                handled = true;
                break;

            case TrayAction.Menu:
                // Placed by the mouse rather than by the window, which is off screen
                // while this is showing.
                _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                _menu.IsOpen = true;
                handled = true;
                break;

            default:
                break;
        }

        return 0;
    }

    private void Remove()
    {
        _messages?.RemoveHook(OnMessage);
        _messages = null;

        _icon?.Dispose();
        _icon = null;

        if (_artwork != 0)
        {
            TrayIcon.Destroy(_artwork);
            _artwork = 0;
        }
    }
}
