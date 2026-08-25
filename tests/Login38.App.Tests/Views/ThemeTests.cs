using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Login38.TestSupport;
using Shouldly;

namespace Login38.App.Tests.Views;

/// <summary>
/// Covers the theme as a whole rather than any one window.
/// </summary>
/// <remarks>
/// <para>
/// A restyled interface fails in one particular way: almost everything is redrawn and two
/// or three controls are not, so a stock scroll bar sits inside a custom list and a stock
/// drop-down opens over a custom panel. That is invisible to a compiler, invisible to a
/// window test that only asks whether the window opened, and completely obvious to whoever
/// uses it.
/// </para>
/// <para>
/// So the theme is checked against the markup: every control either has a style written
/// for it or is named here as an exception with a reason. Adding a control to a window
/// without giving it a look fails the build.
/// </para>
/// </remarks>
public sealed class ThemeTests
{
    /// <summary>
    /// Controls with no style of their own, and why.
    /// </summary>
    /// <remarks>
    /// The window itself is styled by key rather than implicitly, because an implicit style
    /// on the window type would also reach the ones the test harness builds — which have no
    /// composition target for a backdrop and no owner for a title bar.
    /// </remarks>
    private static readonly HashSet<string> Exempt = ["FluentWindow"];

    /// <summary>Where the styles are written.</summary>
    private static readonly string[] Dictionaries =
    [
        "Palette", "LibraryTokens", "Typography", "Primitives",
        "Buttons", "Inputs", "Collections", "Surfaces",
    ];

    [Fact]
    public void StylesEveryControlTheWindowsUse()
    {
        var styled = ImplicitlyStyled();
        var files = Markup();

        // A floor, so a scan that found nothing cannot pass by finding nothing missing.
        files.Count.ShouldBeGreaterThan(2);
        files.Sum(f => f.Used.Count).ShouldBeGreaterThan(20);

        var missing = files
            .SelectMany(f => f.Used
                .Where(t => !styled.Contains(t) && !f.Adopted.Contains(t.Name) && !Exempt.Contains(t.Name))
                .Select(t => $"{f.Name}: {t.Name}"))
            .Order()
            .ToArray();

        missing.ShouldBeEmpty($"no style for: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// What each window uses, and what it styles for itself.
    /// </summary>
    /// <remarks>
    /// Per file rather than across all of them, because a style written in one window's
    /// resources covers that window and nothing else. The controls the theme keeps keyed
    /// rather than implicit — the ones whose styles derive from the library's — have to be
    /// adopted by every window that shows one, and a window that forgot is exactly the
    /// half-themed case this is here to catch.
    /// </remarks>
    private static List<(string Name, HashSet<Type> Used, HashSet<string> Adopted)> Markup()
    {
        var element = new Regex(@"<(?:ui:)?([A-Z][A-Za-z]*)[\s/>]", RegexOptions.None, TimeSpan.FromSeconds(5));
        var declared = new Regex(@"<Style[^>]*?TargetType=""(?:\{x:Type\s+)?(?:ui:|local:|lui:)?([A-Za-z]+)",
            RegexOptions.Singleline, TimeSpan.FromSeconds(5));

        var files = new List<(string, HashSet<Type>, HashSet<string>)>();

        foreach (var file in Directory.EnumerateFiles(System.IO.Path.Combine(Root(), "src"), "*.xaml", SearchOption.AllDirectories))
        {
            // The theme's own files describe controls rather than use them, and the obj
            // folder holds a copy of everything already counted.
            if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{System.IO.Path.DirectorySeparatorChar}Login38.Ui{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var used = element.Matches(text)
                .Select(m => Resolve(m.Groups[1].Value))
                .Where(t => t is not null && Styleable(t))
                .Select(t => t!)
                .ToHashSet();

            var adopted = declared.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            if (used.Count > 0)
            {
                files.Add((System.IO.Path.GetFileName(file), used, adopted));
            }
        }

        return files;
    }

    // Every value, not just a count of the keys.
    //
    // A ResourceDictionary keeps its contents packed until somebody asks for one, so a
    // dictionary that parses is not a dictionary that works: a Setter naming a property
    // the target type does not have — WindowStartupLocation, which is an ordinary property
    // on Window rather than a dependency property — survived a load check and threw the
    // moment a window was built.
    [Fact]
    public void BuildsEveryStyleAndBrushItDefines()
    {
        var built = WindowLayout.OnUi(() =>
        {
            var theme = WindowLayout.Theme();

            // Reading a key is what unpacks it. The value is not wanted; the fact that
            // producing one did not throw is.
            return Ours(theme).SelectMany(Keys).Count(key => theme[key] is not null);
        });

        built.ShouldBeGreaterThan(80);
    }

    [Fact]
    public void ResolvesThePaletteAndTheImplicitStylesThroughTheMergedSet()
    {
        var (parts, accent, tick) = WindowLayout.OnUi(() =>
        {
            var theme = WindowLayout.Theme();

            return (theme.MergedDictionaries.Count, theme["Accent"], theme[typeof(CheckBox)]);
        });

        parts.ShouldBeGreaterThan(5);
        accent.ShouldBeOfType<System.Windows.Media.SolidColorBrush>();
        tick.ShouldBeOfType<Style>();
    }

    // An override of a name the library does not use is a line that looks applied and
    // changes nothing, which is the same failure this whole file is about.
    [Fact]
    public void ReplacesOnlyColoursTheLibraryActuallyDefines()
    {
        var stray = WindowLayout.OnUi(() =>
        {
            var library = new ResourceDictionary
            {
                MergedDictionaries =
                {
                    new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark },
                    new Wpf.Ui.Markup.ControlsDictionary(),
                },
            };

            return Ours(WindowLayout.Theme())
                .Where(d => d.Source!.OriginalString.EndsWith("LibraryTokens.xaml", StringComparison.Ordinal))
                .SelectMany(Keys)
                .Where(key => !library.Contains(key))
                .Select(key => key.ToString() ?? "?")
                .Order()
                .ToArray();
        });

        stray.ShouldBeEmpty($"not a library colour: {string.Join(", ", stray)}");
    }

    /// <summary>
    /// That the library is told which of its two schemes this is.
    /// </summary>
    /// <remarks>
    /// Recolouring its brushes does not tell it. A few of its decisions are taken in code
    /// against the theme it believes is current — the window's own fill among them — and
    /// unasked it believes light, whatever colour the dictionary merged over it is. That
    /// is how two windows came to open with a white field behind them while every brush
    /// in the theme was the shade it was meant to be.
    /// </remarks>
    [Fact]
    public void TellsTheLibraryWhichSchemeThisIs()
    {
        var scheme = WindowLayout.Shown(
            () => new Wpf.Ui.Controls.FluentWindow
            {
                Style = (Style)Application.Current.Resources["Window.Shell"],
                Width = 200,
                Height = 200,
            },
            _ => Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());

        scheme.ShouldBe(Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    /// <summary>
    /// That no window restyles a type the theme already styles without deriving from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A style applied by name <b>replaces</b> the implicit one outright, template
    /// included. Two windows wrote <c>&lt;Style x:Key="…" TargetType="ui:Button"&gt;</c> to
    /// set nothing more than an appearance and a margin, and seven buttons across the two
    /// applications came out as bare framework buttons — a white fill with the window's
    /// own light text on it, so the label was invisible. Nothing errored.
    /// </para>
    /// <para>
    /// Read out of the markup as well as out of the picture, because this one says which
    /// line, and because it does not depend on a test having rendered that page.
    /// </para>
    /// </remarks>
    [Fact]
    public void DerivesEveryWindowStyleFromTheOneItReplaces()
    {
        var styled = ImplicitlyStyled().Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

        var declaration = new Regex(@"<Style\b[^>]*?>", RegexOptions.Singleline, TimeSpan.FromSeconds(5));
        var named = new Regex(@"x:Key=""", RegexOptions.None, TimeSpan.FromSeconds(5));
        var derives = new Regex(@"BasedOn=""", RegexOptions.None, TimeSpan.FromSeconds(5));
        var target = new Regex(
            @"TargetType=""(?:\{x:Type\s+)?(?:[A-Za-z0-9]+:)?(\w+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var scanned = 0;

        var offenders = WindowMarkup()
            .SelectMany(file => declaration.Matches(file.Text).Select(m => (file.Name, m.Value)))
            .Where(_ => ++scanned > 0)
            .Where(found => named.IsMatch(found.Value) && !derives.IsMatch(found.Value))
            .Select(found => (found.Name, Type: target.Match(found.Value)))
            .Where(found => found.Type.Success && styled.Contains(found.Type.Groups[1].Value))
            .Select(found => $"{found.Name}: {found.Type.Groups[1].Value} restyled by name with no BasedOn")
            .Order()
            .ToArray();

        // A floor, so a scan that matched nothing at all cannot pass by finding nothing.
        scanned.ShouldBeGreaterThan(2);

        offenders.ShouldBeEmpty(string.Join("; ", offenders));
    }

    /// <summary>
    /// That the launcher does not print a server's address on its front page.
    /// </summary>
    /// <remarks>
    /// The address is the operator's business rather than the player's: it is in the list
    /// file, it moves without anybody being told, and a launcher that prints it has
    /// published it to whoever is watching the player's screen. Checked in the markup
    /// because it is a binding rather than anything a picture would show.
    /// </remarks>
    [Fact]
    public void ShowsNoServerAddressInTheLauncher()
    {
        var address = new Regex(
            @"Binding\s+(?:Path=)?(?:Server\.)?(?:IpAddress|Port|Endpoint)\b",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var offenders = WindowMarkup()
            .Where(file => file.Name is "MainWindow.xaml")
            .SelectMany(file => address.Matches(file.Text).Select(m => $"{file.Name}: {m.Value}"))
            .ToArray();

        offenders.ShouldBeEmpty(string.Join("; ", offenders));
    }

    /// <summary>Every piece of markup that is a window rather than part of the theme.</summary>
    private static IEnumerable<(string Name, string Text)> WindowMarkup() =>
        Directory.EnumerateFiles(System.IO.Path.Combine(Root(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !file.Contains($"{System.IO.Path.DirectorySeparatorChar}Login38.Ui{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (System.IO.Path.GetFileName(file), File.ReadAllText(file)));

    // The palette is the one place a shade is written down. A colour typed straight into a
    // template is a colour that will not move when the palette does.
    [Fact]
    public void KeepsEveryShadeInThePalette()
    {
        var literal = new Regex(
            @"(Background|Foreground|BorderBrush|Fill|Stroke|Color)=""#[0-9A-Fa-f]{3,8}""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var offenders = Dictionaries
            .Where(name => name != "Palette")
            .Select(name => (Name: name, Text: File.ReadAllText(Path(name))))
            .SelectMany(d => literal.Matches(d.Text).Select(m => $"{d.Name}: {m.Value}"))
            .ToArray();

        offenders.ShouldBeEmpty($"shade outside the palette: {string.Join(", ", offenders)}");
    }

    /// <summary>The control types the theme writes an implicit style for.</summary>
    private static HashSet<Type> ImplicitlyStyled() =>
        WindowLayout.OnUi(() => Ours(WindowLayout.Theme()).SelectMany(Keys).OfType<Type>().ToHashSet());

    /// <summary>
    /// The theme's own files, as they sit inside it.
    /// </summary>
    /// <remarks>
    /// Taken out of the merged theme rather than loaded again by name. WPF caches a
    /// dictionary against the address it was loaded from, so a second load hands back the
    /// first one — and a copy first opened on its own has no library beside it, which is
    /// what half of these styles derive from.
    /// </remarks>
    private static IEnumerable<ResourceDictionary> Ours(ResourceDictionary theme) =>
        theme.MergedDictionaries
            .Where(d => d.Source?.OriginalString.Contains(Folder, StringComparison.Ordinal) == true)

            // And the parent's own entries, which is where the styles that derive from the
            // library's have to live.
            .Prepend(theme);

    private const string Folder = "/Login38.Ui;component/Theme/";

    private static IEnumerable<object> Keys(ResourceDictionary dictionary) => dictionary.Keys.Cast<object>();

    /// <summary>
    /// Whether a type is something a theme is expected to style.
    /// </summary>
    /// <remarks>
    /// Controls and text, and nothing else. An implicit style on <c>Border</c> or
    /// <c>Grid</c> would reach inside every template in the theme, including its own — so
    /// those are styled by name where they are used, and are not counted here.
    /// </remarks>
    private static bool Styleable(Type type) =>
        typeof(Control).IsAssignableFrom(type) || typeof(TextBlock).IsAssignableFrom(type);

    private static Type? Resolve(string name)
    {
        foreach (var assembly in new[] { typeof(Control).Assembly, typeof(Wpf.Ui.Controls.Button).Assembly })
        {
            foreach (var space in new[]
                     {
                         "System.Windows.Controls", "System.Windows.Controls.Primitives",
                         "System.Windows", "Wpf.Ui.Controls",
                     })
            {
                if (assembly.GetType($"{space}.{name}", throwOnError: false) is { } type)
                {
                    return type;
                }
            }
        }

        return null;
    }

    /// <summary>Where one of the theme's files is.</summary>
    private static string Path(string dictionary) =>
        System.IO.Path.Combine(Root(), "src", "Login38.Ui", "Theme", dictionary + ".xaml");

    /// <summary>The checkout root, found by walking up to the solution file.</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Login38.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the checkout root was not found above the test assembly");

        return directory!.FullName;
    }
}
