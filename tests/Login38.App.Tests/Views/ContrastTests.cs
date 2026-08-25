using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Login38.TestSupport;
using Shouldly;

namespace Login38.App.Tests.Views;

/// <summary>
/// That everything meant to be read can be.
/// </summary>
/// <remarks>
/// <para>
/// A dark scheme makes this easy to get wrong in one direction only. Every shade looks
/// fine on the monitor it was chosen on, and the ones that are too close together are
/// exactly the ones a designer stops noticing after an hour — the sentence under a
/// heading, the note beside a tab, the path along the bottom. Somebody who did not choose
/// them reads them once and asks how anybody is supposed to see that.
/// </para>
/// <para>
/// So it is arithmetic rather than judgement. The ratios are the ones the accessibility
/// guidelines use, because they are the only published numbers for this and they are not
/// wrong just because this is a game launcher rather than a form.
/// </para>
/// </remarks>
public sealed class ContrastTests
{
    /// <summary>Every surface a piece of text is ever drawn on.</summary>
    private static readonly string[] Surfaces =
    [
        "Surface.Void",
        "Surface.Base",
        "Surface.Raised",
        "Surface.Sunken",
        "Surface.Hover",
        "Surface.Press",
        "Surface.Selected",
    ];

    /// <summary>Everything drawn as text or as a mark that carries a word's worth of meaning.</summary>
    private static readonly string[] Inks =
    [
        "Ink.Primary",
        "Ink.Secondary",
        "Ink.Tertiary",
        "Accent",
        "Accent.Bright",
        "Accent.Deep",
        "Signal.Good",
        "Signal.Warn",
        "Signal.Bad",
    ];

    /// <summary>What the guidelines ask of text.</summary>
    private const double Readable = 4.5;

    /// <summary>What they ask of the edge of a control, which carries no words.</summary>
    private const double Findable = 3.0;

    /// <summary>
    /// Not a floor anybody publishes: greyed out is meant to read as greyed out. Enough
    /// that a disabled control still looks like a control rather than like a gap.
    /// </summary>
    private const double Present = 2.5;

    [Fact]
    public void KeepsEveryShadeOfTextReadableOnEverySurfaceItIsDrawnOn()
    {
        var failures = Pairs(Inks, Surfaces)
            .Where(pair => Contrast(pair.Ink, pair.On) < Readable)
            .Select(pair => Describe(pair.Ink, pair.On))
            .ToArray();

        failures.ShouldBeEmpty(
            $"under {Readable}:1 — {string.Join("; ", failures)}");
    }

    // The one thing that says where a field ends. A text box on this scheme is a hair
    // darker than the panel behind it, so the fill cannot be what finds it.
    [Fact]
    public void KeepsTheEdgeOfAControlFindable()
    {
        var edges = new[] { "Line.Strong", "Line.Focus" };
        var against = new[] { "Surface.Base", "Surface.Raised", "Surface.Sunken", "Surface.Hover" };

        var failures = Pairs(edges, against)
            .Where(pair => Contrast(pair.Ink, pair.On) < Findable)
            .Select(pair => Describe(pair.Ink, pair.On))
            .ToArray();

        failures.ShouldBeEmpty($"under {Findable}:1 — {string.Join("; ", failures)}");
    }

    [Fact]
    public void LeavesSomethingOfWhatIsGreyedOut()
    {
        foreach (var on in new[] { "Surface.Base", "Surface.Raised" })
        {
            Contrast("Ink.Disabled", on)
                .ShouldBeGreaterThanOrEqualTo(Present, Describe("Ink.Disabled", on));

            Contrast("Signal.Idle", on)
                .ShouldBeGreaterThanOrEqualTo(Present, Describe("Signal.Idle", on));
        }
    }

    // The accent is bright enough that the readable choice on top of it is very dark
    // rather than white, which is worth pinning because it is the opposite of the habit.
    [Fact]
    public void KeepsTheLabelOnTheAccentReadable()
    {
        Contrast("Accent.Ink", "Accent")
            .ShouldBeGreaterThanOrEqualTo(Readable, Describe("Accent.Ink", "Accent"));

        Contrast("Accent.Ink", "Accent.Bright")
            .ShouldBeGreaterThanOrEqualTo(Readable, Describe("Accent.Ink", "Accent.Bright"));
    }

    // A wash is mostly the surface behind it, so what a message inside one is read
    // against is the two of them mixed rather than either.
    [Fact]
    public void KeepsAMessageInsideItsOwnWashReadable()
    {
        var washes = new[]
        {
            ("Signal.Good", "Signal.Good.Wash"),
            ("Signal.Warn", "Signal.Warn.Wash"),
            ("Signal.Bad", "Signal.Bad.Wash"),
            ("Ink.Primary", "Accent.Wash"),
            ("Ink.Secondary", "Accent.Wash"),
            ("Accent", "Accent.Wash"),
        };

        foreach (var (ink, wash) in washes)
        {
            var behind = Over(Shade(wash), Shade("Surface.Raised"));
            var ratio = Contrast(Shade(ink), behind);

            ratio.ShouldBeGreaterThanOrEqualTo(
                Readable,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ink} on {wash} over Surface.Raised is {ratio:F2}:1"));
        }
    }

    private static IEnumerable<(string Ink, string On)> Pairs(string[] inks, string[] surfaces) =>
        from ink in inks
        from surface in surfaces
        select (ink, surface);

    private static string Describe(string ink, string on) => string.Create(
        CultureInfo.InvariantCulture,
        $"{ink} on {on} is {Contrast(ink, on):F2}:1");

    private static double Contrast(string ink, string on) => Contrast(Shade(ink), Shade(on));

    private static double Contrast(Color ink, Color on)
    {
        var one = Luminance(ink);
        var other = Luminance(on);

        return (Math.Max(one, other) + 0.05) / (Math.Min(one, other) + 0.05);
    }

    /// <summary>A translucent shade laid over an opaque one.</summary>
    private static Color Over(Color front, Color back)
    {
        var alpha = front.A / 255d;

        return Color.FromRgb(
            (byte)Math.Round((alpha * front.R) + ((1 - alpha) * back.R)),
            (byte)Math.Round((alpha * front.G) + ((1 - alpha) * back.G)),
            (byte)Math.Round((alpha * front.B) + ((1 - alpha) * back.B)));
    }

    /// <summary>Relative luminance, as the guidelines define it.</summary>
    private static double Luminance(Color colour) =>
        (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Channel(byte value)
    {
        var part = value / 255d;

        return part <= 0.03928 ? part / 12.92 : Math.Pow((part + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// A shade, read out of the theme the applications load.
    /// </summary>
    /// <remarks>
    /// Out of the theme rather than out of a list written here, so that a shade changed in
    /// the palette is a shade checked here — a table of hex in a test file is a second
    /// palette that drifts from the first.
    /// </remarks>
    private static Color Shade(string key) => WindowLayout.OnUi(() =>
    {
        var found = Application.Current.TryFindResource(key);

        found.ShouldNotBeNull($"the theme has no brush called {key}");

        return ((SolidColorBrush)found).Color;
    });
}
