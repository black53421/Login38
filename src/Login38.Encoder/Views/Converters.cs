using System.Globalization;
using System.Windows.Data;
using Login38.Core.Text;

namespace Login38.Encoder.Views;

/// <summary>True when there is text to show. For a bar that only appears once there is.</summary>
public sealed class TextPresentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A way of reading the client's text, as an operator would say it.
/// </summary>
/// <remarks>
/// The enum's names are the code pages' own — <c>Big5</c>, <c>Gbk</c> — which say nothing
/// about which server they belong to.
/// </remarks>
public sealed class TextEncodingNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            TextEncodingMode.Big5 => "繁體中文(Big5)",
            TextEncodingMode.Gbk => "簡體中文(GBK)",
            TextEncodingMode.Auto => "自動判斷",
            _ => string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
