using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Login38.App.Services;
using Login38.App.ViewModels;
using Login38.App.Views;
using Login38.Core.Diagnostics;
using Login38.Patching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private const string StartupDebugBuildMarker =
        "2026-09-06-companion-collision-probe-debug-v1";

    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        WriteStartupDebugSnapshot("before-host-build", services: null);

        _host = Host.CreateApplicationBuilder()
            .ConfigureLauncher()
            .Build();

        WriteStartupDebugSnapshot("after-host-build", _host.Services);
        Stamp(_host.Services.GetService<ILogger<App>>());

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        // Nothing shuts this down but the window's own close, or the notification area's
        // menu. It cannot end with the main window: once a game is running the window is
        // hidden, and hiding it would end the patching and the helper with it. So the end
        // of the session is said here, once, for the one window that is the launcher.
        window.Closed += (_, _) => Shutdown();
    }

    /// <summary>
    /// Writes a logger-independent startup snapshot for launcher deployment debugging.
    /// </summary>
    /// <remarks>
    /// This intentionally bypasses ILogger so it can prove which executable is running
    /// even when file logging itself is the feature under investigation.
    /// </remarks>
    private static void WriteStartupDebugSnapshot(string stage, IServiceProvider? services)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"marker={StartupDebugBuildMarker}");
        text.AppendLine(CultureInfo.InvariantCulture, $"stage={stage}");
        text.AppendLine(CultureInfo.InvariantCulture, $"utc={DateTimeOffset.UtcNow:O}");
        text.AppendLine(CultureInfo.InvariantCulture, $"process_id={Environment.ProcessId}");
        text.AppendLine(CultureInfo.InvariantCulture, $"process_path={Environment.ProcessPath ?? "<null>"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"base_directory={AppContext.BaseDirectory}");
        text.AppendLine(CultureInfo.InvariantCulture, $"current_directory={Environment.CurrentDirectory}");
        text.AppendLine(CultureInfo.InvariantCulture, $"is_64_bit_process={Environment.Is64BitProcess}");
        text.AppendLine(CultureInfo.InvariantCulture, $"command_line={Environment.CommandLine}");

        AppendRunningImageIdentity(text);

        text.AppendLine("environment:");
        var variables = Environment.GetEnvironmentVariables();
        var login38Variables = new List<(string Name, string Value)>();

        foreach (System.Collections.DictionaryEntry entry in variables)
        {
            var name = entry.Key?.ToString();
            if (name is null || !name.StartsWith("LOGIN38_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            login38Variables.Add((name, entry.Value?.ToString() ?? string.Empty));
        }

        login38Variables.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

        if (login38Variables.Count == 0)
        {
            text.AppendLine("  <none>");
        }
        else
        {
            foreach (var (name, value) in login38Variables)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {name}={value}");
            }
        }

        if (services is not null)
        {
            AppendResolvedServices(text, services);
        }

        TryWriteStartupDebugFile(text.ToString(), stage);
    }

    private static void AppendRunningImageIdentity(StringBuilder text)
    {
        var path = Environment.ProcessPath;
        if (path is null)
        {
            text.AppendLine("image_sha256=<process path unavailable>");
            return;
        }

        try
        {
            using var file = File.OpenRead(path);
            text.AppendLine(CultureInfo.InvariantCulture, $"image_sha256={Convert.ToHexString(SHA256.HashData(file))}");
            text.AppendLine(CultureInfo.InvariantCulture, $"image_last_write_utc={File.GetLastWriteTimeUtc(path):O}");
            text.AppendLine(CultureInfo.InvariantCulture, $"image_length={new FileInfo(path).Length}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"image_identity_error={e.GetType().Name}: {e.Message}");
        }
    }

    private static void AppendResolvedServices(StringBuilder text, IServiceProvider services)
    {
        var options = services.GetService<IOptions<LauncherLogOptions>>()?.Value;
        if (options is null)
        {
            text.AppendLine("launcher_log_options=<not resolved>");
        }
        else
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"launcher_log_write_to_file={options.WriteToFile}");
            text.AppendLine(CultureInfo.InvariantCulture, $"launcher_log_general_path={options.GeneralFilePath}");
            text.AppendLine(CultureInfo.InvariantCulture, $"launcher_log_startup_path={options.StartupFilePath}");
        }

        var patches = services.GetServices<IGamePatch>()
            .Select(static patch => patch.GetType().FullName ?? patch.GetType().Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        text.AppendLine(CultureInfo.InvariantCulture, $"registered_game_patch_count={patches.Length}");
        foreach (var patch in patches)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  patch={patch}");
        }

        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"companion_collision_probe_registered={patches.Any(static name => name.EndsWith(".CompanionCollisionProbePatch", StringComparison.Ordinal))}");
    }

    private static void TryWriteStartupDebugFile(string contents, string stage)
    {
        var fileName = $"login38_startup_debug_{stage}.txt";
        var primaryPath = Path.Combine(AppContext.BaseDirectory, fileName);

        try
        {
            File.WriteAllText(primaryPath, contents, Encoding.UTF8);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _ = e;
            // Fall through to TEMP so a read-only deployment folder still leaves evidence.
        }

        try
        {
            var fallbackPath = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(fallbackPath, contents, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _ = e;
            // Debug output must never prevent the launcher from starting.
        }
    }

    /// <summary>
    /// Says which build wrote the log, in the log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A launcher is deployed by copying one file over another, and the copy fails silently
    /// while the old one is running. Three separate sessions were then spent reading a log
    /// for behaviour that had already been fixed, because nothing in it said which build had
    /// produced it and the only way to tell was to hash the file afterwards and hope nobody
    /// had swapped it since.
    /// </para>
    /// <para>
    /// The hash of the running executable is what makes the log answer that on its own. It is
    /// the same number a deployment quotes, which is the point — anything derived instead
    /// from the assembly would be correct and still not comparable to what was handed over.
    /// </para>
    /// </remarks>
    private static void Stamp(ILogger<App>? logger)
    {
        if (logger is null)
        {
            return;
        }

        try
        {
            var path = Environment.ProcessPath;

            if (path is null)
            {
                logger.LogInformation("launcher started; the running image could not be named");

                return;
            }

            using var file = File.OpenRead(path);

            logger.LogInformation(
                "launcher started; {Path} build {Build} of {Written:u}",
                path,
                Convert.ToHexString(SHA256.HashData(file))[..16],
                File.GetLastWriteTimeUtc(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Worth a session that cannot name itself rather than one that will not start.
            logger.LogInformation("launcher started; the running image could not be read: {Message}", e.Message);
        }
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
            Explain(e.Exception), "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }

    /// <summary>
    /// The whole chain of an exception, outermost first.
    /// </summary>
    /// <remarks>
    /// Only the outermost used to be shown, and for the failures that reach here that is
    /// the one line with nothing in it. WPF wraps everything a window does in
    /// "Initialization of 'X' threw an exception" or "provide value on
    /// 'StaticResourceHolder' threw"; which resource, which property and which value are
    /// all in the exception underneath. Two of these have now been diagnosed from a
    /// screenshot of a message box that had already thrown that away.
    /// </remarks>
    private static string Explain(Exception exception)
    {
        var chain = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(Environment.NewLine + "  ← ", chain);
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
