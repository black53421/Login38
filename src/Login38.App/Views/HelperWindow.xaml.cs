using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Login38.App.ViewModels.Helper;
using Wpf.Ui.Controls;

namespace Login38.App.Views;

/// <summary>
/// The helper's settings, for one running game.
/// </summary>
/// <remarks>
/// <para>
/// A window of the launcher's own rather than one attached to the client. The reference
/// made its window a child of the game's, kept it on top by hand, watched for the game to
/// be minimised so it could hide itself, and forwarded mouse wheel messages between them
/// because a child window of another process does not get them. None of that is needed for
/// a window that simply belongs to the launcher.
/// </para>
/// <para>
/// It owns nothing. Everything it shows is in the view model, and everything it does is a
/// binding — the only thing here is when to start and stop looking at the bag.
/// </para>
/// </remarks>
public partial class HelperWindow : FluentWindow
{
    /// <summary>How often the lists of what is in the bag are brought up to date.</summary>
    private static readonly TimeSpan BagCadence = TimeSpan.FromSeconds(1);

    private readonly HelperViewModel _helper;
    private readonly DispatcherTimer _bag;

    public HelperWindow(HelperViewModel helper)
    {
        ArgumentNullException.ThrowIfNull(helper);

        _helper = helper;

        InitializeComponent();

        DataContext = helper;

        _bag = new DispatcherTimer(DispatcherPriority.Background) { Interval = BagCadence };
        _bag.Tick += (_, _) => _helper.RefreshInventory();
    }

    /// <summary>Opens the lists of what to leave alone and what to hunt.</summary>
    /// <remarks>
    /// A handler rather than a command, because opening a window is the view's business and
    /// nothing about it reaches the view model — the dialog is shown against the same
    /// binding context the page has, so every path inside it is the one it always was.
    /// </remarks>
    private void OpenHuntFilters(object sender, RoutedEventArgs e) =>
        Show("怪物過濾", "Hunt.Filters", sender);

    /// <summary>Opens what to do about a monster the character cannot get to.</summary>
    private void OpenHuntStall(object sender, RoutedEventArgs e) =>
        Show("卡住處理", "Hunt.Stall", sender);

    /// <summary>Opens what to cast at whatever the hunt is fighting.</summary>
    private void OpenHuntSkills(object sender, RoutedEventArgs e) =>
        Show("攻擊順序", "Hunt.Skills", sender);

    /// <summary>Opens what to read when a fight has gone badly.</summary>
    private void OpenHuntScrolls(object sender, RoutedEventArgs e) =>
        Show("逃跑卷軸", "Hunt.Scrolls", sender);

    /// <summary>
    /// Shows one of the settings dialogs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The binding context comes from the button rather than from this window, so a dialog
    /// opened from a page that has already narrowed the context — the hunt page binds to one
    /// section of the settings — gets that same narrowing without being told about it.
    /// </para>
    /// <para>
    /// Not awaited. It is shown, the window carries on, and it closes itself; awaiting here
    /// would mean an async void, which turns a mistake inside a dialog into a process that
    /// disappears without a word.
    /// </para>
    /// </remarks>
    private void Show(string title, string template, object sender)
    {
        var dialog = new ContentDialog(DialogHost)
        {
            Title = title,
            Content = new ContentControl
            {
                ContentTemplate = (DataTemplate)Resources[template],
                Content = (sender as FrameworkElement)?.DataContext ?? DataContext,
            },
            CloseButtonText = "關閉",
        };

        _ = dialog.ShowAsync();
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Only from here on does the helper loop pay for reading the bag.
        _helper.Watching = true;
        _bag.Start();
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _bag.Stop();
        _helper.Dispose();

        base.OnClosed(e);
    }
}
