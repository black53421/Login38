using System.Windows;
using System.Windows.Threading;
using Login38.App.Services;
using Login38.App.ViewModels;
using Login38.App.Views;
using Login38.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Login38.App;

/// <summary>
/// Composition root: builds the service provider, then shows the window.
/// </summary>
/// <remarks>
/// One process for the whole session. The patch pipeline and the helper features run
/// here as background tasks, so the launcher stays alive while the game does — which is
/// why the reference's second "stage 2" process is not reproduced.
/// </remarks>
public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateApplicationBuilder()
            .ConfigureLauncher()
            .Build();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        // Nothing shuts this down but the window's own close, or the notification area's
        // menu. It cannot end with the main window: once a game is running the window is
        // hidden, and hiding it would end the patching and the helper with it. So the end
        // of the session is said here, once, for the one window that is the launcher.
        window.Closed += (_, _) => Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Reports what would otherwise be a silent disappearance.
    /// </summary>
    /// <remarks>
    /// A launcher that vanishes takes its patch tasks with it and leaves the player with a
    /// game that is running but unpatched, and nothing on screen to explain it.
    /// </remarks>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogCritical(e.Exception, "Unhandled exception");

        MessageBox.Show(
            e.Exception.Message, "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }
}

file static class HostBuilderExtensions
{
    internal static HostApplicationBuilder ConfigureLauncher(this HostApplicationBuilder builder)
    {
        builder.Logging.AddLauncherLogging();

        builder.Services.AddLauncher();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder;
    }
}
