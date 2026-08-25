namespace Login38.Aux.Notifications;

/// <summary>
/// A picture out of the client's archives, in straight RGBA.
/// </summary>
/// <param name="Width">In pixels.</param>
/// <param name="Height">In pixels.</param>
/// <param name="Rgba">Row-major, top-down, four bytes a pixel.</param>
public sealed record SpriteImage(int Width, int Height, byte[] Rgba)
{
    /// <summary>How many bytes one pixel takes.</summary>
    public const int Channels = 4;

    /// <summary>Whether the array is the size the dimensions claim.</summary>
    public bool IsWholeImage => Rgba.Length == Width * Height * Channels;

    /// <summary>
    /// Makes pure black transparent, where the picture is using it as a colour key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's own convention for artwork with no alpha channel. Only applied when all
    /// four corners are pure black, which is what tells a keyed sprite from a picture that
    /// merely has black in it — the item frame's corners are <c>(15, 9, 2)</c> and the bar
    /// is greyscale, and keying either of those puts holes in them.
    /// </para>
    /// <para>
    /// Never applied to something that already has an alpha channel: it said what it meant.
    /// </para>
    /// </remarks>
    public SpriteImage KeyedOnBlack()
    {
        if (Width < 2 || Height < 2 || !IsWholeImage || !CornersAreBlack())
        {
            return this;
        }

        var keyed = (byte[])Rgba.Clone();

        for (var at = 0; at + Channels <= keyed.Length; at += Channels)
        {
            if (keyed[at] == 0 && keyed[at + 1] == 0 && keyed[at + 2] == 0)
            {
                keyed[at + 3] = 0;
            }
        }

        return this with { Rgba = keyed };
    }

    private bool CornersAreBlack()
    {
        var stride = Width * Channels;

        return Black(0) && Black((Width - 1) * Channels)
            && Black((Height - 1) * stride) && Black(((Height - 1) * stride) + ((Width - 1) * Channels));

        bool Black(int at) => Rgba[at] == 0 && Rgba[at + 1] == 0 && Rgba[at + 2] == 0;
    }

    /// <summary>
    /// Recovers transparency from the same picture drawn on black and on white.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client ships some artwork as a pair with no alpha channel between them. Drawn
    /// over black a pixel comes out as <c>colour × alpha</c>; over white it comes out as
    /// <c>colour × alpha + (1 - alpha)</c>. The difference between the two is therefore
    /// <c>1 - alpha</c> whatever the colour is, and the black copy divided by the recovered
    /// alpha is the colour.
    /// </para>
    /// <para>
    /// Each channel gives its own answer for alpha and the least transparent is taken, so a
    /// half-transparent edge pixel is not read as solid because one channel happened to
    /// agree in both copies.
    /// </para>
    /// </remarks>
    /// <returns>Null if the two are not the same picture.</returns>
    public static SpriteImage? Paired(SpriteImage black, SpriteImage white)
    {
        ArgumentNullException.ThrowIfNull(black);
        ArgumentNullException.ThrowIfNull(white);

        if (black.Width != white.Width || black.Height != white.Height
            || !black.IsWholeImage || !white.IsWholeImage)
        {
            return null;
        }

        var rgba = new byte[black.Rgba.Length];

        for (var at = 0; at < rgba.Length; at += Channels)
        {
            var alpha = 255;

            for (var channel = 0; channel < 3; channel++)
            {
                alpha = Math.Min(alpha, 255 - Math.Max(0, white.Rgba[at + channel] - black.Rgba[at + channel]));
            }

            alpha = Math.Clamp(alpha, 0, 255);

            for (var channel = 0; channel < 3; channel++)
            {
                rgba[at + channel] = Straight(black.Rgba[at + channel], alpha);
            }

            rgba[at + 3] = (byte)alpha;
        }

        return new SpriteImage(black.Width, black.Height, rgba);
    }

    /// <summary>Undoes the multiplication by alpha, rounding to nearest.</summary>
    /// <remarks>Nothing is recoverable where the pixel is fully transparent.</remarks>
    private static byte Straight(byte over, int alpha) =>
        alpha == 0 ? (byte)0 : (byte)Math.Min(255, ((over * 255) + (alpha / 2)) / alpha);
}
