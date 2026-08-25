using System.Windows;
using Login38.App.ViewModels.Helper;
using Login38.App.Views;
using Login38.Aux.Settings;

namespace Login38.App.Services;

/// <summary>Shows the helper's settings for a running game.</summary>
/// <remarks>
/// An interface so the launcher's own view model can offer the window without knowing what
/// a window is, and so what it does can be exercised without one.
/// </remarks>
public interface IHelperWindows
{
    /// <summary>Opens the settings for one game, or brings its window forward.</summary>
    void Show(GameSession session);

    /// <summary>Shows one game's settings, or puts them away if they are already up.</summary>
    /// <remarks>
    /// What Home does, so it may be called from the helper's own loop thread.
    /// </remarks>
    void Toggle(GameSession session);
}

/// <summary>
/// One helper window per running game.
/// </summary>
/// <remarks>
/// <para>
/// Per game, because everything in it is per game: which character is playing, what they
/// have switched on, what is in their bag. A launcher driving two clients shows two of
/// these, each editing its own settings.
/// </para>
/// <para>
/// Asking for one that is already open brings it forward rather than making a second. Two
/// windows onto one set of settings would each publish over the other.
/// </para>
/// </remarks>
public sealed class HelperWindows : IHelperWindows
{
    private readonly ItemCatalog _catalog;
    private readonly Dictionary<uint, HelperWindow> _open = [];

    public HelperWindows(ItemCatalog catalog) => _catalog = catalog;

    /// <inheritdoc/>
    public void Show(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Reached from the helper's loop thread when Home starts the helper, as well as
        // from the launcher's button.
        if (Application.Current?.Dispatcher is { } onto && !onto.CheckAccess())
        {
            onto.BeginInvoke(() => Show(session));

            return;
        }

        if (_open.TryGetValue(session.ProcessId, out var already))
        {
            // Shown as well as activated: one that Home has put away is still open and
            // still in here, and activating a hidden window leaves it hidden.
            already.Show();
            already.Activate();

            return;
        }

        // Written out here rather than at startup: this is the moment the lists matter,
        // and it is also the moment a player might go looking for the file.
        _catalog.Ensure();

        var dispatcher = Application.Current?.Dispatcher;

        var helper = new HelperViewModel(
            session.AuxSettings,
            _catalog,
            session.Helper.Inventory,
            session.Helper.Timers,
            work => dispatcher?.BeginInvoke(work));

        var window = new HelperWindow(helper) { Owner = Application.Current?.MainWindow };

        window.Closed += (_, _) => _open.Remove(session.ProcessId);

        _open[session.ProcessId] = window;
        window.Show();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Hidden rather than closed, so the window comes back where the player left it with
    /// whatever they had selected still selected. Closing it would rebuild the whole thing
    /// the next time Home is pressed, losing the selection with it.
    /// </remarks>
    public void Toggle(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // The key that gets here is seen on the helper's loop thread, and a window may only
        // be touched from the one it was made on.
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Toggle(session));

            return;
        }

        if (_open.TryGetValue(session.ProcessId, out var already) && already.IsVisible)
        {
            already.Hide();

            return;
        }

        Show(session);
    }
}
