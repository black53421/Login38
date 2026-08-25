using Login38.Core.Text;
using Login38.Encoder.Services;
using Login38.Encoder.ViewModels;
using Login38.Encoder.Views;
using Login38.TestSupport;
using Shouldly;

namespace Login38.Encoder.Tests.Views;

/// <summary>
/// What the encoder's window comes out as when it is laid out.
/// </summary>
/// <remarks>
/// The fields that carry the largest numbers — a six-digit image ceiling, a port, a first
/// image id — were sized by eye against columns fixed at a hundred pixels, and the spin
/// buttons inside the control took most of that. An operator saw two digits of what they
/// had typed.
/// </remarks>
public sealed class EncoderWindowLayoutTests : IDisposable
{
    /// <summary>The narrowest a number field may be.</summary>
    private const double NarrowestNumberField = 150;

    /// <summary>
    /// How many number fields the window is known to hold, at the least.
    /// </summary>
    /// <remarks>
    /// The port, the bag ceiling, the image ceiling, the copy limit, the first image id,
    /// the target icon, and the two timings.
    /// </remarks>
    private const int ShouldFindAtLeast = 8;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"l38-encoder-layout-{Guid.NewGuid():N}");

    public EncoderWindowLayoutTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary directory that outlives the run is not a failed test.
        }
    }

    private EncoderViewModel Tool() => new(
        new EncoderFiles(new LegacyTextCodec(TextEncodingMode.Big5), _directory),
        new LegacyTextCodec(TextEncodingMode.Big5),
        new StubPrompts());

    // Opening is not drawing. A window whose style was applied by name without deriving
    // from the library's has no template at all: it opens, answers its title, and puts
    // nothing on screen. Every width check phrased as "nothing was too narrow" passes on
    // an empty tree.
    [Fact]
    public void PutsItsContentsOnScreen()
    {
        var realised = WindowLayout.Shown(() => new EncoderWindow(Tool()), WindowLayout.Realised);

        realised.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void GivesEveryNumberFieldRoomForItsLargestValue()
    {
        var fields = WindowLayout.Shown(
            () => new EncoderWindow(Tool()),
            WindowLayout.NumberFields);

        fields.Count.ShouldBeGreaterThanOrEqualTo(ShouldFindAtLeast);

        foreach (var (name, width) in fields)
        {
            width.ShouldBeGreaterThanOrEqualTo(
                NarrowestNumberField,
                $"{name} came out {width} wide, which clips its own maximum.");
        }
    }

    /// <summary>
    /// That it comes out dark.
    /// </summary>
    /// <remarks>
    /// The check that reads the picture rather than the tree, and the only one of these
    /// that would have caught the worst of it: the window shipped with a white field
    /// behind everything the markup drew, while every brush the styles named was still
    /// the right colour and every other check here was green.
    /// </remarks>
    [Fact]
    public void ComesOutDark()
    {
        var paint = WindowLayout.Shown(() => new EncoderWindow(Tool()), WindowLayout.Painted);

        // Light type on a dark window is a fraction of a per cent of it. A light
        // background is nearer half, so nothing delicate rests on where this sits.
        paint.LightShare.ShouldBeLessThan(
            0.05,
            $"{paint.LightShare:P2} of the window is light, which is a light background rather than light text.");

        // And not mid-grey everywhere either, which no share of light pixels would notice.
        paint.CommonestBrightness.ShouldBeLessThan(
            0.2,
            $"the colour {paint.CommonestShare:P0} of the window is made of has a brightness of {paint.CommonestBrightness:F2}.");
    }

    /// <summary>
    /// That no control on any page came out white.
    /// </summary>
    /// <remarks>
    /// A style applied by name replaces the implicit one outright, template included, so
    /// one written without a <c>BasedOn</c> drops the control back to the framework's own
    /// look: a white fill with this window's light text on it, which is a label nobody can
    /// read. It is invisible to the compiler, to the styles, and to any check that only
    /// looks at the page a window opens on.
    /// </remarks>
    [Fact]
    public void HasNoWhiteControlOnAnyPage()
    {
        var white = WindowLayout.Shown(() => new EncoderWindow(Tool()), WindowLayout.WhiteControls);

        white.ShouldBeEmpty(string.Join("; ", white));
    }
}
