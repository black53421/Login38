using System.Windows;
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
