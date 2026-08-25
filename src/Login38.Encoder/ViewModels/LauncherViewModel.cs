using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Login38.Core.Servers;
using Login38.Encoder.Services;

namespace Login38.Encoder.ViewModels;

/// <summary>
/// What the launcher itself looks like and links to.
/// </summary>
/// <remarks>
/// None of this reaches the client. It is the shell a player sees before they press play:
/// which look it wears, whether it opens an announcement, and where its two links go.
/// </remarks>
public sealed partial class LauncherViewModel : ObservableObject
{
    private readonly string _root;
    private readonly IFilePrompts _prompts;

    /// <param name="root">Where the encoder is, which is where <c>skins/</c> lives.</param>
    public LauncherViewModel(string root, IFilePrompts prompts)
    {
        _root = root;
        _prompts = prompts;

        Rescan();
    }

    /// <summary>The looks that are installed.</summary>
    public ObservableCollection<string> Skins { get; } = [];

    [ObservableProperty]
    private string _skin = SkinCatalog.Default;

    [ObservableProperty]
    private bool _announcement = true;

    [ObservableProperty]
    private string _announcementUrl = string.Empty;

    [ObservableProperty]
    private bool _listUpdate;

    [ObservableProperty]
    private string _listUpdateUrl = string.Empty;

    [ObservableProperty]
    private bool _autoUpdate;

    [ObservableProperty]
    private string _autoUpdateUrl = string.Empty;

    [ObservableProperty]
    private string _officialUrl = string.Empty;

    [ObservableProperty]
    private string _supportUrl = string.Empty;

    /// <summary>An image to put in as the chosen look's background.</summary>
    [ObservableProperty]
    private string _background = string.Empty;

    /// <summary>Fills everything in from the settings.</summary>
    public void Load(LauncherConfig launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);

        // Rescanned first, because the look the settings name may have been installed
        // since this window opened — and a name that is not in the list cannot be shown.
        Rescan();

        Skin = Skins.Contains(launcher.ActiveSkin) ? launcher.ActiveSkin : Skins[0];
        Announcement = launcher.AnnouncementEnabled;
        AnnouncementUrl = launcher.AnnouncementUrl;
        ListUpdate = launcher.ListUpdateEnabled;
        ListUpdateUrl = launcher.ListUpdateUrl;
        AutoUpdate = launcher.AutoUpdateEnabled;
        AutoUpdateUrl = launcher.AutoUpdateUrl;
        OfficialUrl = launcher.OfficialUrl;
        SupportUrl = launcher.CustomerServiceUrl;
    }

    /// <summary>Reads it back out.</summary>
    public LauncherConfig ToConfig() => new()
    {
        ActiveSkin = Skin,
        AnnouncementEnabled = Announcement,
        AnnouncementUrl = AnnouncementUrl.Trim(),
        ListUpdateEnabled = ListUpdate,
        ListUpdateUrl = ListUpdateUrl.Trim(),
        AutoUpdateEnabled = AutoUpdate,
        AutoUpdateUrl = AutoUpdateUrl.Trim(),
        OfficialUrl = OfficialUrl.Trim(),
        CustomerServiceUrl = SupportUrl.Trim(),
    };

    /// <summary>Looks again for what is installed, keeping the chosen one if it survived.</summary>
    [RelayCommand]
    public void Rescan()
    {
        var chosen = Skin;

        Skins.Clear();

        foreach (var skin in SkinCatalog.Scan(_root))
        {
            Skins.Add(skin);
        }

        Skin = Skins.Contains(chosen) ? chosen : Skins[0];
    }

    /// <summary>Opens the folder they live in, making it if it is not there.</summary>
    [RelayCommand]
    public void OpenFolder()
    {
        var directory = SkinCatalog.DirectoryIn(_root);

        try
        {
            Directory.CreateDirectory(directory);

            using var explorer = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                  or System.ComponentModel.Win32Exception)
        {
            _prompts.Say($"skins 資料夾開不起來:{e.Message}");
        }
    }

    /// <summary>Asks for an image to use as the background.</summary>
    [RelayCommand]
    public void BrowseBackground()
    {
        if (_prompts.OpenFile("選擇背景圖片", "圖片 (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|所有檔案|*.*") is { } chosen)
        {
            Background = chosen;
        }
    }

    private bool CanApplyBackground => !string.IsNullOrWhiteSpace(Background);

    /// <summary>Copies it in as the chosen look's background.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyBackground))]
    public void ApplyBackground()
    {
        try
        {
            _prompts.Say($"已套用背景圖到 {SkinCatalog.ApplyBackground(_root, Skin, Background)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                  or DirectoryNotFoundException or ArgumentException)
        {
            _prompts.Say($"背景圖套用失敗:{e.Message}");
        }
    }

    partial void OnBackgroundChanged(string value) => ApplyBackgroundCommand.NotifyCanExecuteChanged();
}
