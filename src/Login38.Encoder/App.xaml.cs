using System.Windows;
using Login38.Core.Text;
using Login38.Encoder.Services;
using Login38.Encoder.ViewModels;
using Login38.Encoder.Views;

namespace Login38.Encoder;

/// <summary>
/// The server operator's tool.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is composed from a container. There is one window, one view model and
/// three services with no lifetimes worth managing — a host would be ceremony around four
/// constructor calls.
/// </para>
/// <para>
/// No command line either. The reference offered four subcommands, attaching to the parent
/// console to print to it; every one of them does something this window does, and a
/// windowed program printing to a console it borrowed is a poor way to be scripted.
/// </para>
/// </remarks>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var codec = LegacyTextCodec.Auto;
        var window = new EncoderWindow(
            new EncoderViewModel(new EncoderFiles(codec), codec, new FilePrompts()));

        MainWindow = window;
        window.Show();
    }
}
