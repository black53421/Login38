using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Login38.App.Services;
using Login38.Core.Configuration;
using Login38.Core.Servers;
using Login38.Patching;
using Microsoft.Extensions.Logging;

namespace Login38.App.ViewModels;

/// <summary>
/// The launcher window: choose a server, choose how it should be displayed, start it.
/// </summary>
/// <remarks>
/// <para>
/// The window stays open after the game starts. It has to: the patch pipeline and the
/// helper features run from this process, and closing it would stop them. Once a game is
/// running the launcher shows what it is doing rather than offering to start another.
/// </para>
/// <para>
/// The reference drove all of this from JavaScript inside an embedded browser, passing
/// JSON both ways. Here it is ordinary bindable state, so the same behaviour is
/// exercisable without a window.
/// </para>
/// <para>
/// <b>Nothing here awaits with <c>ConfigureAwait(false)</c>, deliberately.</b> Every line
/// after an await sets state a window is bound to, and the window will not be touched from
/// anywhere but the thread it was made on. Two of the ways that goes wrong are not obvious:
/// a command's <c>CanExecuteChanged</c> reaches a button synchronously, and a plain
/// <c>PropertyChanged</c> subscriber — which the launcher window is — runs on whichever
/// thread raised it. Neither is marshalled for us the way an ordinary binding is. So the
/// continuations stay on the thread the command was invoked from, which for every command
/// here is the interface thread.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ServerCatalog _catalog;
    private readonly GameLaunchService _launcher;
    private readonly IServerProbe _probe;
    private readonly ILogger<MainViewModel> _logger;
    private readonly string _preferencesPath;
    private readonly IHelperWindows? _helpers;

    private ListFile _list = new();
    private GameSession? _session;

    public MainViewModel(
        ServerCatalog catalog,
        GameLaunchService launcher,
        IServerProbe probe,
        ILogger<MainViewModel> logger,
        string? preferencesPath = null,
        IHelperWindows? helpers = null)
    {
        _catalog = catalog;
        _launcher = launcher;
        _probe = probe;
        _logger = logger;
        _preferencesPath = preferencesPath ?? UserPreferences.DefaultPath;
        _helpers = helpers;

        var preferences = UserPreferences.Load(_preferencesPath);
        Windowed = preferences.Windowed;
        WindowMode = preferences.WindowMode;
    }

    /// <summary>The servers the operator is offering.</summary>
    public ObservableCollection<ServerEntryViewModel> Servers { get; } = [];

    /// <summary>The sizes offered when running in a window.</summary>
    public IReadOnlyList<WindowMode> WindowModes { get; } = [.. Enum.GetValues<WindowMode>()];

    [ObservableProperty]
    private ServerEntryViewModel? _selectedServer;

    [ObservableProperty]
    private bool _windowed;

    [ObservableProperty]
    private WindowMode _windowMode;

    /// <summary>What the launcher is doing, shown under the buttons.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Set when something went wrong that the player needs to act on.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>Whether a game started by this launcher is running.</summary>
    [ObservableProperty]
    private bool _isGameRunning;

    /// <summary>Whether the launcher is busy and should not be asked to start anything.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Where the operator's announcement lives, if they published one.</summary>
    public string? AnnouncementUrl =>
        _list.Launcher.AnnouncementEnabled ? _list.Launcher.AnnouncementUrl : null;

    public string OfficialUrl => _list.Launcher.OfficialUrl;

    public string SupportUrl => _list.Launcher.CustomerServiceUrl;

    /// <summary>The list this view model read, for anything that needs the raw entries.</summary>
    public ListFile List => _list;


    /// <summary>What the notification area says while a game is running.</summary>
    public string TrayCaption =>
        SelectedServer is { } server ? $"天堂 3.8 — {server.Name} 執行中" : "天堂 3.8 登入器";

    /// <summary>Reads the operator's list and offers what is in it.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Error = null;
        Status = "正在讀取伺服器列表…";

        try
        {
            _list = _catalog.Load();
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or InvalidDataException)
        {
            _logger.LogError(e, "Could not read the server list");
            Error = e.Message;
            Status = string.Empty;
            return;
        }

        Servers.Clear();

        foreach (var (server, index) in _list.Servers.Select((s, i) => (s, i)))
        {
            var entry = new ServerEntryViewModel(server, index);

            if (entry.IsOffered)
            {
                Servers.Add(entry);
            }
        }

        SelectedServer = Servers.FirstOrDefault();
        OnPropertyChanged(nameof(AnnouncementUrl));
        OnPropertyChanged(nameof(OfficialUrl));
        OnPropertyChanged(nameof(SupportUrl));

        Status = Servers.Count == 0 ? "目前沒有開放的伺服器。" : string.Empty;

        await RefreshAvailabilityAsync(cancellationToken);
    }

    /// <summary>Checks every offered server, all at once.</summary>
    /// <remarks>
    /// Concurrently rather than in turn: eight servers checked one after another with a
    /// three-second timeout each is twenty-four seconds of a list that says nothing.
    /// </remarks>
    [RelayCommand]
    public async Task RefreshAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var checks = Servers.Select(async entry =>
        {
            var online = await _probe.IsReachableAsync(entry.Server, cancellationToken);

            entry.Availability = online ? ServerAvailability.Online : ServerAvailability.Offline;
        });

        await Task.WhenAll(checks);
    }

    /// <summary>Whether the launcher is in a state where starting a game makes sense.</summary>
    public bool CanLaunch => !IsBusy && !IsGameRunning && SelectedServer is not null;

    /// <summary>Starts the selected server.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    public async Task LaunchAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedServer is not { } selected)
        {
            return;
        }

        Error = null;
        IsBusy = true;
        SavePreferences();

        try
        {
            var request = new GameLaunchRequest(
                AppContext.BaseDirectory, selected.Server, _list.Aux, Windowed, WindowMode);

            var result = _launcher.Launch(request, cancellationToken);

            // The only result without a session is a refused one, so the two are tested
            // together rather than leaving a second branch that cannot be reached.
            if (result is not { Outcome: LaunchOutcome.Started, Session: { } session })
            {
                Error = result.InstanceLimit == 1
                    ? "遊戲已在執行中。這個伺服器一次只允許開一份。"
                    : $"已經開了 {result.InstanceLimit} 份,達到上限。";
                return;
            }

            _session = session;
            session.Helper.Keys.WindowRequested += OnSettingsAskedFor;
            IsGameRunning = true;

            // The window stops being busy here, not when the game ends. Everything past
            // this point is patching a client that is already on screen, and the reference
            // launcher had exited by now — leaving a spinner up for the whole session made
            // a running game look like one that never started.
            IsBusy = false;
            Status = "正在啟動遊戲…";

            await TrackAsync(session);
        }
        catch (Exception e) when (e is FileNotFoundException or IOException
                                  or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogError(e, "Could not start the game");
            Error = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Follows a running game so the window reflects what is happening to it.</summary>
    private async Task TrackAsync(GameSession session)
    {
        var patching = session.Patching;

        // Said as soon as there is a client to look at. What follows takes as long as the
        // client's own unpacking does — half a minute is ordinary — and a single unchanging
        // line for all of it is what "stuck" looks like.
        if (await Task.WhenAny(session.Unpacked, session.Completion) == session.Unpacked)
        {
            Status = "遊戲執行中。剩下的修補進行中…";
        }

        var finished = await Task.WhenAny(patching, session.Completion);

        if (finished == patching)
        {
            var outcomes = await patching;
            var failed = outcomes.Count(o => o.Status == PatchStatus.Failed);

            // Failures are not fatal by design — each one costs a feature, not the game —
            // so this is reported rather than raised as an error.
            Status = failed == 0
                ? "遊戲執行中。"
                : $"遊戲執行中。{outcomes.Count} 項修補中有 {failed} 項沒有套用成功。";
        }

        await session.Completion;

        session.Helper.Keys.WindowRequested -= OnSettingsAskedFor;

        // Stopped here rather than left for the end of the process. The helper's loop is
        // still ticking over a client that has gone, the relay is still holding its port,
        // and the character's settings are written when the scope holding them closes.
        await session.DisposeAsync();

        _session = null;
        Status = string.Empty;

        // Last, because this is what ends the launcher: with the game gone there is nothing
        // left for this process to do, and it says so through the window.
        IsGameRunning = false;
    }

    /// <summary>The player pressed Home in the game.</summary>
    /// <remarks>
    /// On the helper's loop thread. Nothing is marshalled here because the window service
    /// has to do that anyway — it is the only part of this that knows what a dispatcher is.
    /// </remarks>
    private void OnSettingsAskedFor(object? sender, EventArgs e)
    {
        if (_session is { } session)
        {
            _helpers?.Toggle(session);
        }
    }

    /// <summary>Opens one of the operator's links in the player's browser.</summary>
    [RelayCommand]
    public void OpenLink(string? url)
    {
        // Only http(s), and only well-formed: this value comes out of a file the operator
        // distributes, and anything else here would be handing an arbitrary string to the
        // shell.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Refusing to open {Url}", url);
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    partial void OnSelectedServerChanged(ServerEntryViewModel? value) => LaunchCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => LaunchCommand.NotifyCanExecuteChanged();

    partial void OnIsGameRunningChanged(bool value) => LaunchCommand.NotifyCanExecuteChanged();

    partial void OnWindowedChanged(bool value) => SavePreferences();

    partial void OnWindowModeChanged(WindowMode value) => SavePreferences();

    /// <summary>
    /// Writes the player's display choices back beside the launcher.
    /// </summary>
    /// <remarks>
    /// Saved on every change rather than on launch, so a player who sets a size and then
    /// closes the launcher finds it still set. Failures are swallowed: losing a preference
    /// is not worth an error dialog.
    /// </remarks>
    private void SavePreferences() =>
        new UserPreferences { Windowed = Windowed, WindowMode = WindowMode }.TrySave(_preferencesPath);
}
