using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Login38.TestSupport;

/// <summary>
/// Builds a window on a thread WPF will talk to, lays it out, and reports what came of it.
/// </summary>
/// <remarks>
/// <para>
/// WPF needs a single-threaded apartment and an <see cref="Application"/> whose resources
/// hold the theme, or every templated control measures to nothing and every width read off
/// it would be zero whatever the markup said. The test runner gives neither.
/// </para>
/// <para>
/// One thread for the whole process, not one per call. A process may hold exactly one
/// <see cref="Application"/>, and everything WPF makes belongs to the thread that made it —
/// a second thread that tried to build its own found <see cref="Application.Current"/>
/// already there and owned by somebody else, and took the test host down with it rather
/// than failing a test.
/// </para>
/// <para>
/// The window is shown, because a <see cref="TabControl"/> builds only the tab that is
/// selected and an <see cref="ItemsControl"/> builds nothing at all until there is a
/// visual tree to build into. It is shown far off the left of any screen so that a test
/// run does not put a window in front of whoever started it.
/// </para>
/// </remarks>
/// <summary>What a window came out looking like.</summary>
/// <param name="LightShare">Share of pixels bright enough to read as a light background.</param>
/// <param name="CommonestBrightness">How bright the colour the window is mostly made of is.</param>
/// <param name="CommonestShare">How much of the window that colour covers.</param>
public readonly record struct Paint(double LightShare, double CommonestBrightness, double CommonestShare);

public static class WindowLayout
{
    /// <summary>Far enough left that no arrangement of monitors reaches it.</summary>
    private const double OffScreen = -32000;

    /// <summary>How long a window gets to build itself before the run is called stuck.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static readonly Lazy<Dispatcher> Ui =
        new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Shows a window out of sight, reads something off it, and closes it again.
    /// </summary>
    /// <param name="build">Makes the window. Runs on the shared apartment thread.</param>
    /// <param name="read">Reads the answer off the shown window.</param>
    public static T Shown<T>(Func<Window> build, Func<Window, T> read)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(read);

        return Ui.Value.Invoke(
            () =>
            {
                Window? window = null;

                try
                {
                    window = build();
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.ShowInTaskbar = false;
                    window.Left = OffScreen;
                    window.Top = OffScreen;
                    window.Show();
                    window.UpdateLayout();

                    return read(window);
                }
                finally
                {
                    window?.Close();
                }
            },
            DispatcherPriority.Normal,
            CancellationToken.None,
            Patience);
    }

    /// <summary>
    /// How many controls a shown window actually put on screen.
    /// </summary>
    /// <remarks>
    /// The check that catches a window with no template. A style applied by name replaces
    /// the implicit one outright, so one written without a BasedOn leaves the window with
    /// no <see cref="System.Windows.Controls.ControlTemplate"/> at all — it opens, reports
    /// its size, answers its title, and draws nothing. Everything else measured off such a
    /// window is measured off an empty tree, which passes any check phrased as "nothing
    /// was too narrow".
    /// </remarks>
    public static int Realised(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Descendants<DependencyObject>(window).Count();
    }

    /// <summary>
    /// How wide every number field on every tab came out, and what each is bound to.
    /// </summary>
    /// <remarks>
    /// Every tab is selected in turn, because one that was never selected was never built
    /// and would report nothing rather than report a problem.
    /// </remarks>
    public static IReadOnlyList<(string Field, double Width)> NumberFields(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var found = new List<(string, double)>();

        foreach (var tabs in Descendants<TabControl>(window))
        {
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                window.UpdateLayout();

                Collect(window, found);
            }
        }

        Collect(window, found);

        return found;
    }

    /// <summary>
    /// What a window actually came out looking like, in pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only check that answers the question a person asks first, which is whether the
    /// thing is the colour it was meant to be. Everything else in this file reads the
    /// tree: what style an element ended up with, how wide a field came out, how many
    /// controls were realised. All of that was green while both windows opened with a
    /// white field behind them, because the brush the style named was the right colour —
    /// it simply was not what got painted.
    /// </para>
    /// <para>
    /// Reported as a share rather than as "no light pixels", because the text is light on
    /// purpose. A dark window with light type on it comes out a few per cent light; one
    /// with a light background comes out most of the way light, and no threshold in
    /// between is delicate.
    /// </para>
    /// </remarks>
    public static Paint Painted(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;
        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, PixelFormats.Pbgra32);

        target.Render(window);

        var stride = width * 4;
        var pixels = new byte[stride * height];

        target.CopyPixels(pixels, stride, 0);

        var counts = new Dictionary<int, int>();
        var light = 0;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            int blue = pixels[i];
            int green = pixels[i + 1];
            int red = pixels[i + 2];

            // Rounded to the nearest few shades before counting, so that a gradient does
            // not split the background it is part of into a thousand near-misses.
            var bucket = ((red >> 3) << 10) | ((green >> 3) << 5) | (blue >> 3);

            counts[bucket] = counts.GetValueOrDefault(bucket) + 1;

            if (Brightness(red, green, blue) > 0.5)
            {
                light++;
            }
        }

        var commonest = counts.MaxBy(pair => pair.Value).Key;
        var pixelCount = pixels.Length / 4;

        return new Paint(
            (double)light / pixelCount,
            Brightness(
                ((commonest >> 10) & 0x1F) << 3,
                ((commonest >> 5) & 0x1F) << 3,
                (commonest & 0x1F) << 3),
            counts[commonest] / (double)pixelCount);
    }

    private static double Brightness(int red, int green, int blue) =>
        ((0.2126 * red) + (0.7152 * green) + (0.0722 * blue)) / 255;

    /// <summary>
    /// Every control that came out mostly white, on every page of the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check for a control that lost its template. A style applied by name replaces
    /// the implicit one outright, so one written without a <c>BasedOn</c> leaves the
    /// control with no template of its own and it falls back to the framework's: a white
    /// fill, and the window's own light text drawn on top of it, so the label is
    /// invisible. Seven buttons across two applications shipped that way.
    /// </para>
    /// <para>
    /// Near-white rather than merely bright, because the accent is brighter than white by
    /// this measure and is meant to be. A colour counts as white only if its channels are
    /// close to each other, which the accent's are not.
    /// </para>
    /// <para>
    /// Every tab in turn, because a <see cref="TabControl"/> builds only the page that is
    /// selected. The pages this found were never built by any test, which is why nothing
    /// caught them — a window-wide count of light pixels on page one says nothing about
    /// page five.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> WhiteControls(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var found = new List<string>();
        var tabs = Descendants<TabControl>(window).FirstOrDefault();
        var pages = tabs?.Items.Count ?? 0;

        for (var page = 0; page < Math.Max(pages, 1); page++)
        {
            if (tabs is not null)
            {
                tabs.SelectedIndex = page;
            }

            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            window.UpdateLayout();

            Collect(window, page, found);
        }

        return found;
    }

    private static void Collect(Window window, int page, List<string> into)
    {
        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, PixelFormats.Pbgra32);

        target.Render(window);

        var stride = width * 4;
        var pixels = new byte[stride * height];

        target.CopyPixels(pixels, stride, 0);

        foreach (var control in Descendants<Control>(window))
        {
            // A control too small to read is a control nobody is looking at, and one of
            // the caption buttons is legitimately a light glyph on nothing.
            if (!control.IsVisible || control.ActualWidth < 24 || control.ActualHeight < 16)
            {
                continue;
            }

            Point corner;

            try
            {
                corner = control.TransformToAncestor(window).Transform(default);
            }
            catch (InvalidOperationException)
            {
                // Not connected to this window's tree after all.
                continue;
            }

            var left = Math.Max((int)corner.X, 0);
            var top = Math.Max((int)corner.Y, 0);
            var right = Math.Min((int)(corner.X + control.ActualWidth), width);
            var bottom = Math.Min((int)(corner.Y + control.ActualHeight), height);

            if (right - left < 24 || bottom - top < 16)
            {
                continue;
            }

            var white = 0;
            var seen = 0;

            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var at = (y * stride) + (x * 4);
                    int blue = pixels[at];
                    int green = pixels[at + 1];
                    int red = pixels[at + 2];

                    seen++;

                    // Bright, and grey rather than coloured. The accent is brighter than
                    // this and is not white; a framework button is exactly this.
                    if (Brightness(red, green, blue) > 0.5 &&
                        Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) < 40)
                    {
                        white++;
                    }
                }
            }

            if (seen > 0 && (double)white / seen > 0.5)
            {
                into.Add(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"page {page}: {control.GetType().Name} \"{Label(control)}\" is {(double)white / seen:P0} white"));
            }
        }
    }

    private static string Label(Control control) => control switch
    {
        ContentControl { Content: string words } => words,
        _ => control.Name.Length > 0 ? control.Name : "(unnamed)",
    };

    /// <summary>Runs something on the shared apartment thread and hands back its answer.</summary>
    /// <remarks>
    /// For the things that need a live <see cref="Application"/> but no window: reading the
    /// theme's own resources, building a control on its own.
    /// </remarks>
    public static T OnUi<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return Ui.Value.Invoke(work, DispatcherPriority.Normal, CancellationToken.None, Patience);
    }

    /// <summary>The applications' theme, loaded the same way they load it.</summary>
    public static ResourceDictionary Theme() => new()
    {
        Source = new Uri("pack://application:,,,/Login38.Ui;component/Theme/Theme.xaml"),
    };

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            _ = new Application
            {
                // Nothing closes this one down: the windows it shows come and go, and a
                // run that ended with the last of them would leave the next test without
                // an application to show anything in.
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
                // Exactly what App.xaml declares, down to the shape: one merged
                // dictionary, the theme, which merges the library's itself.
                //
                // It used to build the library's two dictionaries here in code first, and
                // that one difference from the shipping applications hid the worst bug
                // this theme has had. Constructed in code the library knew it was on the
                // dark scheme; loaded from inside the theme's markup — which is what both
                // applications do — it did not, and answered light to every question it
                // takes in code rather than from a brush. Both windows shipped with a
                // white field behind everything the markup drew on top, and every test
                // was green, because every test was looking at a differently-configured
                // application.
                Resources = new ResourceDictionary
                {
                    MergedDictionaries = { Theme() },
                },
            };

            // The library posts a designer-support callback when its dictionaries load. It
            // resolves an address from whatever assembly owns Application.Current, which in
            // an application is the application and here is PresentationFramework — so it
            // builds an address that does not exist and throws on the dispatcher, taking
            // the whole test host down instead of failing one test. Nothing in it affects
            // what a window looks like.
            Application.Current.DispatcherUnhandledException += (_, e) =>
                e.Handled = e.Exception is NotSupportedException
                    && e.Exception.StackTrace?.Contains("ContextMenuLoader", StringComparison.Ordinal) == true;

            ready.SetResult(Dispatcher.CurrentDispatcher);

            Dispatcher.Run();
        })
        {
            // The process is free to end while this is still pumping. Nothing here is
            // worth keeping a test run alive for.
            IsBackground = true,
            Name = "WPF layout",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return ready.Task.GetAwaiter().GetResult();
    }

    private static void Collect(DependencyObject node, List<(string, double)> into)
    {
        if (node is NumberBox box)
        {
            var bound = System.Windows.Data.BindingOperations
                .GetBinding(box, NumberBox.ValueProperty)?.Path.Path;

            into.Add((bound ?? "(unbound)", box.ActualWidth));
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            Collect(VisualTreeHelper.GetChild(node, i), into);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node)
        where T : DependencyObject
    {
        if (node is T hit)
        {
            yield return hit;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            foreach (var found in Descendants<T>(VisualTreeHelper.GetChild(node, i)))
            {
                yield return found;
            }
        }
    }
}
