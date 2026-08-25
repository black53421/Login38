namespace Login38.Core.Text;

/// <summary>
/// Bound from the server-distributed config; lets an operator who knows their
/// client's region turn off Big5/GBK guessing.
/// </summary>
public sealed class LegacyTextOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Text";

    /// <summary>How to resolve ambiguous legacy bytes. Defaults to guessing.</summary>
    public TextEncodingMode Mode { get; set; } = TextEncodingMode.Auto;
}
