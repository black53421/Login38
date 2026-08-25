using System.IO;
using Login38.App.ViewModels.Helper;
using Login38.App.Views;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.App.Tests.Views;

/// <summary>
/// What the helper's settings window comes out as when it is laid out.
/// </summary>
/// <remarks>
/// A tab a player never opens is a tab nobody looks at, so the sizes in it have to be
/// checked by something other than a person opening it.
/// </remarks>
public sealed class HelperWindowLayoutTests : IDisposable
{
    /// <summary>
    /// The narrowest a number field may be.
    /// </summary>
    /// <remarks>
    /// The spin buttons take about fifty pixels of whatever the box is given and the text
    /// gets the rest. This leaves room for six digits, which covers the largest value any
    /// of these fields accepts — 86,400 seconds, and 999,999 points.
    /// </remarks>
    private const double NarrowestNumberField = 150;

    /// <summary>
    /// How many number fields the window is known to hold, at the least.
    /// </summary>
    /// <remarks>
    /// Four healing rows, two for mana when safe, six timers and the shout interval. A
    /// walk that reports fewer than this realised less of the window than it thinks.
    /// </remarks>
    private const int ShouldFindAtLeast = 13;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"l38-helper-layout-{Guid.NewGuid():N}");

    public HelperWindowLayoutTests() => Directory.CreateDirectory(_directory);

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

    private HelperViewModel Helper() => new(
        new AuxSettingsSource(),
        new ItemCatalog(
            new LegacyTextCodec(TextEncodingMode.Big5),
            NullLogger<ItemCatalog>.Instance,
            _directory));

    // Opening is not drawing. A window whose style was applied by name without deriving
    // from the library's has no template at all: it opens, answers its title, and puts
    // nothing on screen. Every width check phrased as "nothing was too narrow" passes on
    // an empty tree.
    [Fact]
    public void PutsItsContentsOnScreen()
    {
        var realised = WindowLayout.Shown(() => new HelperWindow(Helper()), WindowLayout.Realised);

        realised.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void GivesEveryNumberFieldRoomForItsLargestValue()
    {
        var fields = WindowLayout.Shown(
            () => new HelperWindow(Helper()),
            WindowLayout.NumberFields);

        // A floor as well as a width: a walk that realised nothing would report no
        // fields at all, and an empty list passes every width check there is.
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
        var paint = WindowLayout.Shown(() => new HelperWindow(Helper()), WindowLayout.Painted);

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
        var white = WindowLayout.Shown(() => new HelperWindow(Helper()), WindowLayout.WhiteControls);

        white.ShouldBeEmpty(string.Join("; ", white));
    }
}
