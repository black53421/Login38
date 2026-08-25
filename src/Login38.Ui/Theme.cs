using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Login38.Ui;

/// <summary>
/// Tells the control library which of its two colour schemes this application is on.
/// </summary>
/// <remarks>
/// <para>
/// Recolouring the library's brushes is not enough on its own. A handful of the library's
/// decisions are taken in code against
/// <see cref="ApplicationThemeManager.GetAppTheme"/> rather than against a resource — the
/// window's own fill, the caption buttons, the backdrop — and that answer does not come
/// from the dictionary the application merged. Left unasked it answered
/// <see cref="ApplicationTheme.Light"/>, and both windows opened with a white field behind
/// everything the markup had drawn on top. Every brush in the theme was still the right
/// colour, which is why nothing looked wrong until somebody ran it.
/// </para>
/// <para>
/// Attached to the window style rather than called from each application's startup on
/// purpose: a window that uses this theme is a window that needs this, and a rule that
/// only holds when somebody remembers to add a line to <c>OnStartup</c> is a rule that
/// holds until the second application. It also means a window built by a test gets the
/// same treatment as one built by the launcher, so what a test measures is what ships.
/// </para>
/// </remarks>
public static class Theme
{
    private static bool _pinned;

    /// <summary>Set on the window style. Setting it true pins the library to dark.</summary>
    public static readonly DependencyProperty PinnedProperty = DependencyProperty.RegisterAttached(
        "Pinned",
        typeof(bool),
        typeof(Theme),
        new PropertyMetadata(false, OnPinned));

    public static void SetPinned(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(PinnedProperty, value);
    }

    public static bool GetPinned(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(PinnedProperty);
    }

    /// <summary>
    /// Tells the library it is on the dark scheme.
    /// </summary>
    /// <remarks>
    /// Idempotent, and cheap to call again: the work is a dictionary swap the library
    /// skips when the theme already matches.
    /// </remarks>
    public static void Pin()
    {
        if (_pinned || Application.Current is null)
        {
            return;
        }

        _pinned = true;

        // No backdrop, and no accent. The backdrop tints the window with the desktop
        // behind it, which on a scheme this dark means the panel colour is whatever
        // wallpaper the player has. The accent would be Windows' own, overwriting the one
        // colour in this theme that carries meaning.
        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            WindowBackdropType.None,
            updateAccent: false);
    }

    private static void OnPinned(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Pin();
        }
    }
}
