namespace Login38.App.ViewModels.Helper;

/// <summary>
/// The bounds the helper window's number boxes work within.
/// </summary>
/// <remarks>
/// <para>
/// Every number in this window came out of a text box in the reference and was read back
/// with a parse that fell to a default when it failed — so a half-typed threshold became
/// nought, which reads as "never", and a half-typed interval became five seconds. Both are
/// silent, and both look exactly like a setting that was never applied.
/// </para>
/// <para>
/// Here the boxes take numbers, and what they take is bounded on the way in and again on
/// the way out.
/// </para>
/// </remarks>
internal static class HelperNumbers
{
    /// <summary>The ceiling for anything read as a percentage.</summary>
    internal const double Percent = 100;

    /// <summary>
    /// The ceiling for a threshold read as an amount.
    /// </summary>
    /// <remarks>
    /// Above what the expanded hit-point patch allows a character to reach, so no real
    /// character can be excluded, and far below where the number stops being a number.
    /// </remarks>
    internal const double Points = 999_999;

    /// <summary>The shortest an interval may be, in seconds.</summary>
    internal const double ShortestInterval = 1;

    /// <summary>And the longest: a day, past which nothing would ever fire twice.</summary>
    internal const double LongestInterval = 86_400;

    /// <summary>Rounds and bounds what a box holds, for writing back to the settings.</summary>
    internal static uint Whole(double value, double ceiling) => Whole(value, 0, ceiling);

    /// <inheritdoc cref="Whole(double, double)"/>
    internal static uint Whole(double value, double floor, double ceiling) =>
        double.IsNaN(value) ? (uint)floor : (uint)Math.Clamp(Math.Round(value), floor, ceiling);
}
