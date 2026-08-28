namespace Login38.Core.Configuration;

/// <summary>
/// The client's windowed resolution setting.
/// </summary>
/// <remarks>
/// The enum values <em>are</em> the numbers stored in <c>lineage.cfg</c> and
/// <c>launcher.ini</c>, so converting in either direction is a cast and validity is
/// <see cref="Enum.IsDefined{T}(T)"/>. The reference implementation carried a separate
/// <c>from_raw</c>/<c>as_raw</c> pair plus a hand-written <c>4..=7</c> range check.
/// </remarks>
public enum WindowMode : byte
{
    Size400x300 = 4,
    Size800x600 = 5,
    Size1200x900 = 6,
    Size1600x1200 = 7,
}

/// <summary>A window size in pixels.</summary>
public readonly record struct Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

public static class WindowModeExtensions
{
    /// <summary>
    /// 800x600. The size least likely to run off the edge of the screen or trip the
    /// DPI-scaling ghosting seen on Windows 11.
    /// </summary>
    public const WindowMode Default = WindowMode.Size800x600;

    public static Resolution ToResolution(this WindowMode mode) => mode switch
    {
        WindowMode.Size400x300 => new Resolution(400, 300),
        WindowMode.Size800x600 => new Resolution(800, 600),
        WindowMode.Size1200x900 => new Resolution(1200, 900),
        WindowMode.Size1600x1200 => new Resolution(1600, 1200),
        _ => Default.ToResolution(),
    };

    /// <summary>Interprets a raw config value, or null if it is not a supported size.</summary>
    public static WindowMode? ToWindowMode(this byte raw) =>
        Enum.IsDefined((WindowMode)raw) ? (WindowMode)raw : null;
}
