namespace Login38.Core.Text;

/// <summary>
/// How to resolve the Big5/GBK ambiguity when decoding legacy bytes.
/// </summary>
/// <remarks>
/// Short strings — a two-character item name, say — often decode cleanly as both
/// Big5 and GBK while only one is right. <see cref="Auto"/> guesses; the explicit
/// modes let an operator who knows their client's region skip the guessing.
/// </remarks>
public enum TextEncodingMode
{
    /// <summary>Score both candidates and pick the more plausible one.</summary>
    Auto = 0,

    /// <summary>Always decode as code page 950.</summary>
    Big5 = 1,

    /// <summary>Always decode as code page 936.</summary>
    Gbk = 2,
}

/// <summary>
/// Conversions between <see cref="TextEncodingMode"/> and its config-file spelling.
/// </summary>
public static class TextEncodingModeExtensions
{
    /// <summary>
    /// Parses a config value. Anything unrecognised — including null — is
    /// <see cref="TextEncodingMode.Auto"/>, so a typo degrades to guessing rather
    /// than to a hard failure at startup.
    /// </summary>
    public static TextEncodingMode ToTextEncodingMode(this string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "big5" or "cp950" or "traditional" or "trad" or "tw" => TextEncodingMode.Big5,
            "gbk" or "gb2312" or "simplified" or "simp" or "cn" => TextEncodingMode.Gbk,
            _ => TextEncodingMode.Auto,
        };

    /// <summary>Renders the canonical config-file spelling.</summary>
    public static string ToConfigValue(this TextEncodingMode mode) => mode switch
    {
        TextEncodingMode.Big5 => "big5",
        TextEncodingMode.Gbk => "gbk",
        _ => "auto",
    };
}
