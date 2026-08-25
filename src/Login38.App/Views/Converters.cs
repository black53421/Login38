using System.Globalization;
using System.Windows.Data;
using Login38.App.ViewModels;
using Login38.Core.Configuration;

namespace Login38.App.Views;

/// <summary>Whether a string has anything in it, for showing a message only when there is one.</summary>
public sealed class StringPresentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// What to show when there is nothing to show.
/// </summary>
/// <remarks>
/// The launcher's status line is empty for most of a session, and an empty line leaves a
/// panel that looks broken rather than idle. The stand-in text is markup rather than view
/// model state, because "nothing is happening" is a thing to draw, not a thing to record.
/// </remarks>
public sealed class FallbackTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? text : parameter as string ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A window size as the player reads it.
/// </summary>
/// <remarks>
/// The enum's names are the numbers the client's own configuration file stores, so they
/// are spelled for the file rather than for a person — <c>Size800x600</c> in a dropdown is
/// the format leaking into the window.
/// </remarks>
public sealed class WindowModeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not WindowMode mode)
        {
            return string.Empty;
        }

        var size = mode.ToResolution();

        return $"{size.Width} × {size.Height}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
