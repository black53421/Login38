using Login38.Encoder.ViewModels;
using Wpf.Ui.Controls;

namespace Login38.Encoder.Views;

/// <summary>The operator's tool.</summary>
/// <remarks>
/// Nothing here but the wiring. Everything the window does is a binding, and everything it
/// decides is in <see cref="EncoderViewModel"/> — which is what lets the whole tool be
/// exercised without a desktop.
/// </remarks>
public partial class EncoderWindow : FluentWindow
{
    public EncoderWindow(EncoderViewModel model)
    {
        DataContext = model;

        InitializeComponent();
    }

    /// <summary>
    /// Looks again for packed tables at the moment the operator opens the list.
    /// </summary>
    /// <remarks>
    /// A list built when the window opened does not know about a table packed since, and
    /// an operator who has just made one and finds the box empty concludes the tool cannot
    /// see it. Refreshing here costs one directory read per drop-down and cannot be stale.
    /// </remarks>
    private void MorphTablesOpened(object sender, EventArgs e)
    {
        if (DataContext is EncoderViewModel model)
        {
            model.RescanMorphTablesCommand.Execute(null);
        }
    }
}
