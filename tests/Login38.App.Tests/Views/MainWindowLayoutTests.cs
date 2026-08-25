using System.IO;
using Login38.App.Services;
using Login38.App.ViewModels;
using Login38.App.Views;
using Login38.Core.Configuration;
using Login38.Core.Servers;
using Login38.Core.Text;
using Login38.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.App.Tests.Views;

/// <summary>
/// That the launcher's own window can be built at all.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else in the suite touches it. Everything it shows is covered through
/// <see cref="MainViewModel"/>, which is the right place for behaviour — but a window that
/// throws while its markup is being read never gets as far as a view model, and the first
/// thing anybody sees is a launcher that does not start.
/// </para>
/// <para>
/// That is exactly what happened: <c>BasedOn="{StaticResource {x:Type ListViewItem}}"</c>
/// asked for a resource that only exists in the framework's own theme dictionary, which
/// <c>StaticResource</c> does not reach, and the launcher threw a
/// <see cref="System.Windows.Markup.XamlParseException"/> on startup for as long as nobody
/// ran it.
/// </para>
/// </remarks>
public sealed class MainWindowLayoutTests : IDisposable
{
    /// <inheritdoc cref="HelperWindowLayoutTests"/>
    private const double NarrowestNumberField = 150;

    private readonly string _preferences =
        Path.Combine(Path.GetTempPath(), $"l38-main-layout-{Guid.NewGuid():N}.ini");

    public void Dispose()
    {
        try
        {
            File.Delete(_preferences);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary file that outlives the run is not a failed test.
        }
    }

    private sealed class NeverReachable : IServerProbe
    {
        public Task<bool> IsReachableAsync(ServerInfo server, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private MainViewModel Model() => new(
        new ServerCatalog(
            new LegacyTextCodec(TextEncodingMode.Big5),
            NullLogger<ServerCatalog>.Instance,
            Path.Combine(AppContext.BaseDirectory, "TestData")),
        new GameLaunchService(
            new Login38.Patching.PatchPipeline([], NullLogger<Login38.Patching.PatchPipeline>.Instance),
            new LineageConfigFile(NullLogger<LineageConfigFile>.Instance),
            new ServiceCollection().AddLogging().AddAuxFeatures().BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            NullLoggerFactory.Instance),
        new NeverReachable(),
        NullLogger<MainViewModel>.Instance,
        _preferences);

    /// <summary>A notification area that records rather than one beside somebody's clock.</summary>
    private sealed class Recorded : ILauncherTray
    {
        public string? Tooltip { get; private set; }

        public bool IsWithdrawn { get; private set; }

        public int Restores { get; private set; }

        public void Withdraw(string tooltip)
        {
            Tooltip = tooltip;
            IsWithdrawn = true;
        }

        public void Restore()
        {
            Restores++;
            IsWithdrawn = false;
        }

        public void Dispose()
        {
        }
    }

    // What a launcher is expected to do once the game it started is on screen.
    [Fact]
    public void WithdrawsToTheNotificationAreaWhenTheGameStarts()
    {
        var model = Model();
        var tray = new Recorded();

        var withdrawn = WindowLayout.Shown(
            () => new MainWindow(model, tray: tray),
            _ =>
            {
                model.IsGameRunning = true;

                return tray.IsWithdrawn;
            });

        withdrawn.ShouldBeTrue();
    }

    // And what it does when that game ends: goes, rather than putting itself back over
    // whatever the player turned to. A window appearing and taking the foreground at the
    // moment the client is giving up its display mode is a stutter on the way out.
    [Fact]
    public void EndsWithTheGameRatherThanComingBack()
    {
        var model = Model();
        var tray = new Recorded();

        var (closed, restores) = WindowLayout.Shown(
            () => new MainWindow(model, tray: tray),
            window =>
            {
                var ended = false;
                window.Closed += (_, _) => ended = true;

                model.IsGameRunning = true;
                model.IsGameRunning = false;

                return (ended, tray.Restores);
            });

        closed.ShouldBeTrue("the launcher stayed open after the game it was started for ended");
        restores.ShouldBe(0, "it put its window back instead of ending");
    }

    // The markup is read in the constructor, so building it is the whole test.
    [Fact]
    public void CanBeBuiltAndShown()
    {
        var title = WindowLayout.Shown(() => new MainWindow(Model()), window => window.Title);

        title.ShouldNotBeNullOrWhiteSpace();
    }

    // Opening is not drawing. A window whose style was applied by name without deriving
    // from the library's has no template at all: it opens, reports its size, answers its
    // title, and puts nothing on screen. That shipped once, and every check phrased as
    // "nothing was too narrow" passed the whole time.
    [Fact]
    public void PutsItsContentsOnScreen()
    {
        var realised = WindowLayout.Shown(() => new MainWindow(Model()), WindowLayout.Realised);

        realised.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void GivesEveryNumberFieldRoomForItsLargestValue()
    {
        var fields = WindowLayout.Shown(() => new MainWindow(Model()), WindowLayout.NumberFields);

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
        var paint = WindowLayout.Shown(() => new MainWindow(Model()), WindowLayout.Painted);

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
        var white = WindowLayout.Shown(() => new MainWindow(Model()), WindowLayout.WhiteControls);

        white.ShouldBeEmpty(string.Join("; ", white));
    }
}
