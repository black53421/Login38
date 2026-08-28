using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Login38.App.ViewModels.Helper;
using Login38.App.Views;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.App.Tests.Views;

/// <summary>
/// Covers the templates the settings dialogs are built from.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else reaches them. The page tests walk what a tab shows, and these are shown by a
/// button, so a template can name a resource that is not there — or one declared further down
/// the same dictionary than the template wanting it — and every other test passes. What that
/// looks like in the hand is the dialog throwing on the way open, from inside
/// <c>StaticResourceHolder</c>, with the name of the resource nowhere in the message.
/// </para>
/// <para>
/// A <c>StaticResource</c> cannot look forwards inside a dictionary, and that is the trap
/// here rather than a typo: the page markup lower down the same file uses the same keys quite
/// happily, because the page is parsed after all of them. Only a template can be parsed
/// before what it needs.
/// </para>
/// </remarks>
public sealed class DialogTemplateTests : IDisposable
{
    /// <summary>Every template a button opens, by the key the code-behind asks for.</summary>
    /// <remarks>
    /// Written out rather than discovered. A dialog added without being listed here should be
    /// a failure rather than a silence, and a template renamed on one side only fails exactly
    /// as quietly as a missing one.
    /// </remarks>
    private static readonly string[] Dialogs =
        ["Hunt.Filters", "Hunt.Stall", "Hunt.Skills", "Hunt.Scrolls"];

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"l38-dialog-{Guid.NewGuid():N}");

    public DialogTemplateTests() => Directory.CreateDirectory(_directory);

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

    [Theory]
    [InlineData("Hunt.Filters")]
    [InlineData("Hunt.Stall")]
    [InlineData("Hunt.Skills")]
    [InlineData("Hunt.Scrolls")]
    public void BuildsWithoutReachingForAResourceThatIsNotThereYet(string key)
    {
        var realised = WindowLayout.Shown(
            () =>
            {
                var window = new HelperWindow(Helper());
                var template = (DataTemplate)window.Resources[key];
                var helper = (HelperViewModel)window.DataContext;

                // Putting it in the window is what resolves the static references. Reading
                // the template out of the dictionary does not, which is why this has to
                // replace the window's content rather than merely look at it.
                //
                // Against the section the page narrows to, not the whole settings. A dialog
                // takes its context from the button that opened it, and the hunt page binds
                // to one section — given the whole thing instead, a template whose only
                // content is a list would bind to nothing and realise as an empty box.
                window.Content = new ContentControl
                {
                    ContentTemplate = template,
                    Content = key.StartsWith("Hunt.", StringComparison.Ordinal)
                        ? helper.Hunt
                        : helper,
                };

                return window;
            },
            WindowLayout.Realised);

        // A floor as well as a pass: a template that threw would not get here, and one that
        // resolved to nothing would realise a handful of elements and look like success.
        realised.ShouldBeGreaterThan(20);
    }

    /// <summary>
    /// Opens every dialog the way a player does, and insists none of them throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The theory above builds each template into the window, which catches a resource that
    /// is not there. It does not catch what a dialog does differently, and a dialog does
    /// one thing differently that matters: the content is built off to one side of the
    /// window rather than in it, so a style whose setter reaches for the palette resolves
    /// against a chain that no longer leads anywhere. What comes back is UnsetValue, and
    /// <c>Foreground</c> is one of the few properties that refuses it outright — the dialog
    /// dies on the way open, blaming whichever control had that style, with the palette key
    /// nowhere in the message.
    /// </para>
    /// <para>
    /// That has now cost two rounds of guessing from a screenshot, so it is a test: press
    /// the buttons, and let anything that goes wrong go wrong here. It runs every tab
    /// rather than the one that has dialogs today.
    /// </para>
    /// </remarks>
    [Fact]
    public void OpensEveryDialogTheWayAButtonDoes()
    {
        var (opened, failures) = WindowLayout.Shown(
            () => new HelperWindow(Helper()),
            window =>
            {
                var broke = new List<string>();
                var pressed = 0;

                void OnFailure(object sender, DispatcherUnhandledExceptionEventArgs e)
                {
                    broke.Add(Describe(e.Exception));
                    e.Handled = true;
                }

                window.Dispatcher.UnhandledException += OnFailure;

                try
                {
                    // A tab builds nothing until it is the one showing, so every one of
                    // them has to be visited rather than searched for.
                    foreach (var tabs in Descendants<TabControl>(window))
                    {
                        for (var i = 0; i < tabs.Items.Count; i++)
                        {
                            tabs.SelectedIndex = i;
                            window.UpdateLayout();

                            foreach (var button in Openers(window))
                            {
                                pressed++;
                                Press(window, button, broke);
                            }
                        }
                    }
                }
                finally
                {
                    window.Dispatcher.UnhandledException -= OnFailure;
                }

                return (pressed, broke);
            });

        failures.ShouldBeEmpty();

        // A dialog that quietly stopped being reachable would otherwise pass by having
        // nothing to go wrong.
        opened.ShouldBe(Dialogs.Length);
    }

    private static void Press(Window window, ButtonBase button, List<string> broke)
    {
        try
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // The dialog opens on its own turn of the loop, so the failure lands after the
            // click returns unless the queue is drained here.
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            broke.Add($"{button.Content}: {Describe(e)}");
        }
    }

    /// <summary>Every button whose ellipsis says it opens a window.</summary>
    private static IEnumerable<ButtonBase> Openers(DependencyObject root) =>
        Descendants<ButtonBase>(root)
            .Where(button => button.Content?.ToString()?.EndsWith('…') == true);

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T wanted)
            {
                yield return wanted;
            }

            foreach (var found in Descendants<T>(child))
            {
                yield return found;
            }
        }
    }

    /// <summary>The whole chain, because WPF puts the answer in the innermost one.</summary>
    private static string Describe(Exception exception)
    {
        var chain = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" <- ", chain);
    }

    [Fact]
    public void HasATemplateForEveryButtonThatOpensOne()
    {
        var present = WindowLayout.OnUi(() =>
        {
            var window = new HelperWindow(Helper());

            return Dialogs.Where(key => window.Resources.Contains(key)).ToArray();
        });

        present.ShouldBe(Dialogs);
    }

    private HelperViewModel Helper() => new(
        new AuxSettingsSource(),
        new ItemCatalog(
            new LegacyTextCodec(TextEncodingMode.Big5),
            NullLogger<ItemCatalog>.Instance,
            _directory));
}
