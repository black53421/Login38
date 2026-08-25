using System.Windows;

namespace Login38.Ui;

/// <summary>
/// The extras a page carries in the navigation rail.
/// </summary>
/// <remarks>
/// <para>
/// The helper has seven pages and the encoder six, which is more than a row of tabs across
/// the top can label without either truncating or wrapping. They are drawn down the side
/// instead, where there is room for a symbol and a word — and where the list of pages stays
/// visible while a page is being read rather than becoming a strip the eye has to return to.
/// </para>
/// <para>
/// Attached properties rather than a page type of its own, so the pages stay ordinary
/// <see cref="System.Windows.Controls.TabItem"/>s: the selection, the keyboard handling and
/// the content hosting are the framework's, and only the drawing is here.
/// </para>
/// </remarks>
public static class Rail
{
    /// <summary>The symbol drawn beside the page's name.</summary>
    /// <remarks>
    /// Typed as object so the markup can hand over any icon element the library offers
    /// without this assembly deciding which one. A page with none is drawn with its name
    /// alone rather than with a gap where a symbol would be.
    /// </remarks>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(object), typeof(Rail), new PropertyMetadata(null));

    /// <summary>A short note under the page's name, or nothing.</summary>
    /// <remarks>
    /// For the two or three pages whose name does not say what is on them. Not a tooltip:
    /// a rail is read once when the window opens, and something that only appears on hover
    /// is something the reader has to already suspect is there.
    /// </remarks>
    public static readonly DependencyProperty NoteProperty = DependencyProperty.RegisterAttached(
        "Note", typeof(string), typeof(Rail), new PropertyMetadata(string.Empty));

    public static void SetIcon(DependencyObject element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(IconProperty, value);
    }

    public static object? GetIcon(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.GetValue(IconProperty);
    }

    public static void SetNote(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(NoteProperty, value);
    }

    public static string GetNote(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (string)element.GetValue(NoteProperty);
    }
}
