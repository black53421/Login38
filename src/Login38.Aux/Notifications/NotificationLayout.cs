namespace Login38.Aux.Notifications;

/// <summary>Where one thing goes and how solid it is.</summary>
/// <param name="X">From the left of the game's picture.</param>
/// <param name="Y">From the top.</param>
/// <param name="Alpha">255 solid, 0 gone.</param>
public readonly record struct Placement(int X, int Y, byte Alpha);

/// <summary>
/// Where the toasts and the numbers sit, and how they fade.
/// </summary>
/// <remarks>
/// <para>
/// Arithmetic only, with the clock passed in. Everything about where this feature appears
/// is decided here, which is what makes it something a test can hold still and look at
/// rather than something that has to be watched on a running game.
/// </para>
/// <para>
/// The anchors are proportions of the picture rather than pixel offsets from an edge, so
/// the same numbers land in the same place whatever the player runs the client at. The
/// reference has two layouts — a <c>layout.rs</c> of fixed offsets from the bottom-left,
/// which nothing outside its own tests calls, and these, which are what it actually draws.
/// </para>
/// </remarks>
public static class NotificationLayout
{
    /// <summary>How wide a toast row is: the width of the client's own bar art.</summary>
    public const int ToastWidth = 329;

    /// <summary>And how tall, which is also the size of the item frame.</summary>
    public const int ToastHeight = 37;

    /// <summary>The gap between two rows.</summary>
    public const int ToastGap = 2;

    /// <summary>How far the name sits from the left of the row.</summary>
    public const int NameLeft = ToastHeight + 6;

    /// <summary>How tall the name is drawn.</summary>
    public const int NameHeight = 16;

    /// <summary>How far a number rises over its life.</summary>
    public const int DriftRise = 60;

    /// <summary>How far above the anchor the experience number sits.</summary>
    public const int ExperienceOffset = -9;

    /// <summary>And how far below it the coin number does.</summary>
    public const int GoldOffset = 9;

    /// <summary>How long a toast is solid before it starts to go.</summary>
    public static readonly TimeSpan ToastSolid = NotificationBoard.ToastLife - TimeSpan.FromMilliseconds(500);

    /// <summary>And a number.</summary>
    public static readonly TimeSpan DriftSolid = NotificationBoard.DriftLife - TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Where the bottom of the stack of toasts is.
    /// </summary>
    /// <remarks>
    /// Just past two thirds down, which is above the client's own chat panel at every size
    /// the game is played at.
    /// </remarks>
    internal static int ToastAnchorY(int height) => height * 71 / 100;

    /// <summary>And how far in from the left edge.</summary>
    internal static int ToastAnchorX(int width) => width * 12 / 1000;

    /// <summary>Where the numbers drift from.</summary>
    internal static int DriftAnchorX(int width) => width * 62 / 100;

    /// <inheritdoc cref="DriftAnchorX"/>
    internal static int DriftAnchorY(int height) => height * 41 / 100;

    /// <summary>
    /// Where a toast row goes.
    /// </summary>
    /// <param name="slot">Its place in the stack, oldest first.</param>
    public static Placement Toast(TimeSpan spawnedAt, int slot, int width, int height, TimeSpan now) =>
        new(
            ToastAnchorX(width),
            ToastAnchorY(height) - ((slot + 1) * (ToastHeight + ToastGap)),
            Fade(now - spawnedAt, ToastSolid, NotificationBoard.ToastLife));

    /// <summary>Where a number is, this instant.</summary>
    public static Placement Drift(
        TimeSpan spawnedAt, DriftKind kind, int width, int height, TimeSpan now)
    {
        var elapsed = now - spawnedAt;
        var life = NotificationBoard.DriftLife;
        var risen = elapsed >= life ? DriftRise : (int)(elapsed.Ticks * DriftRise / life.Ticks);
        var offset = kind == DriftKind.Experience ? ExperienceOffset : GoldOffset;

        return new Placement(
            DriftAnchorX(width),
            DriftAnchorY(height) + offset - risen,
            Fade(elapsed, DriftSolid, life));
    }

    /// <summary>
    /// Solid, then straight down to nothing over what is left.
    /// </summary>
    /// <remarks>
    /// Clamped at both ends rather than trusted to land there: a caller drawing a frame
    /// late would otherwise be asked for an alpha below zero.
    /// </remarks>
    internal static byte Fade(TimeSpan elapsed, TimeSpan solid, TimeSpan life)
    {
        if (elapsed >= life)
        {
            return 0;
        }

        if (elapsed < solid)
        {
            return byte.MaxValue;
        }

        var gone = (elapsed - solid).Ticks * byte.MaxValue / (life - solid).Ticks;

        return (byte)Math.Max(0, byte.MaxValue - gone);
    }
}
