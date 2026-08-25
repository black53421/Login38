namespace Login38.Core.Text;

/// <summary>
/// Which encoding a byte sequence turned out to be.
/// </summary>
/// <remarks>
/// The game is a pre-Unicode Windows application: everything it holds in memory or
/// writes to disk is a legacy double-byte code page, and which one depends on the
/// region the client was built for. Config files written by newer tooling are UTF-8.
/// </remarks>
public enum LegacyEncoding
{
    /// <summary>UTF-8. Config files and anything this launcher wrote itself.</summary>
    Utf8,

    /// <summary>Code page 950, traditional Chinese. The Taiwanese client.</summary>
    Big5,

    /// <summary>Code page 936, simplified Chinese.</summary>
    Gbk,
}
