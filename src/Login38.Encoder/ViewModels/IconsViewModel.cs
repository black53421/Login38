using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Login38.Core.Icons;
using Login38.Encoder.Services;

namespace Login38.Encoder.ViewModels;

/// <summary>
/// Building the package of animated item icons.
/// </summary>
/// <remarks>
/// <para>
/// The client draws an item's icon from one picture. This replaces chosen ones with a
/// sequence: a package of PNGs plus a manifest saying which icon each sequence belongs to
/// and how fast it runs. The launcher reads the manifest into a table the client scans.
/// </para>
/// <para>
/// An existing package can be opened again, which is what makes this editable — the
/// pictures come back out of it and can be added to or replaced without the operator
/// keeping the originals.
/// </para>
/// </remarks>
public sealed partial class IconsViewModel : ObservableObject
{
    /// <summary>Where the numbering of the pictures starts, unless the operator says.</summary>
    public const uint DefaultFirstImageId = 30_000;

    private readonly IFilePrompts _prompts;

    private List<byte[]> _pending = [];

    public IconsViewModel(IFilePrompts prompts) => _prompts = prompts;

    /// <summary>What has been built up so far.</summary>
    public ObservableCollection<IconEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveEntryCommand))]
    private IconEntryViewModel? _selected;

    /// <summary>What the package will be called, without an extension.</summary>
    [ObservableProperty]
    private string _packageName = "123";

    /// <summary>The number the first picture in the package takes.</summary>
    /// <remarks>
    /// High enough not to collide with the client's own numbering. Opening an existing
    /// package sets this to whatever it used, so repacking keeps the same numbers.
    /// </remarks>
    [ObservableProperty]
    private double _firstImageId = DefaultFirstImageId;

    /// <summary>Which of the client's icons the next entry replaces.</summary>
    [ObservableProperty]
    private double _icon;

    /// <summary>How long each picture of it is shown.</summary>
    [ObservableProperty]
    private double _frameMilliseconds = 80;

    /// <summary>And how long it waits before starting again.</summary>
    [ObservableProperty]
    private double _restMilliseconds;

    /// <summary>What the pictures chosen for the next entry are.</summary>
    [ObservableProperty]
    private string _pendingSummary = "尚未選擇幀";

    /// <summary>What happened last, for the operator to read.</summary>
    [ObservableProperty]
    private string _report = string.Empty;

    /// <summary>
    /// Reads an existing package back in.
    /// </summary>
    /// <remarks>
    /// An entry whose pictures are not all in the package is left out rather than stopping
    /// the read, and said so. The reference abandoned the whole package on the first one
    /// missing, so a single damaged entry cost the operator the other forty.
    /// </remarks>
    [RelayCommand]
    public void OpenPackage()
    {
        if (_prompts.OpenFile("選擇 pak(旁邊需有同名 .idx)", "pak (*.pak)|*.pak|所有檔案|*.*") is not { } chosen)
        {
            return;
        }

        var indexPath = Path.ChangeExtension(chosen, ".idx");

        byte[] package;
        byte[] index;

        try
        {
            package = File.ReadAllBytes(chosen);
            index = File.ReadAllBytes(indexPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report = $"pak 讀不出來(旁邊要有同名的 {Path.GetFileName(indexPath)}):{e.Message}";

            return;
        }

        IReadOnlyList<IconAnimation> manifest;

        try
        {
            manifest = DynamicIconManifest.Parse(
                Encoding.UTF8.GetString(
                    SpritePackage.Read(package, index, DynamicIconManifest.FileName).Span));
        }
        catch (Exception e) when (e is DynamicIconException or ArgumentException or FormatException)
        {
            Report = $"pak 內的 {DynamicIconManifest.FileName} 解不開:{e.Message}";

            return;
        }

        var read = new List<IconEntryViewModel>();
        var missing = new List<ushort>();
        var lowest = uint.MaxValue;

        foreach (var animation in manifest)
        {
            var frames = new List<byte[]>();

            foreach (var id in animation.Frames)
            {
                try
                {
                    frames.Add([.. SpritePackage.Read(package, index, $"{id}.png").Span]);
                    lowest = Math.Min(lowest, id);
                }
                catch (Exception e) when (e is DynamicIconException or ArgumentException)
                {
                    missing.Add(animation.Icon);

                    break;
                }
            }

            if (frames.Count == animation.Frames.Count)
            {
                read.Add(new IconEntryViewModel(
                    animation.Icon, animation.FrameMilliseconds, animation.RestMilliseconds, frames));
            }
        }

        Entries.Clear();

        foreach (var entry in read.OrderBy(e => e.Icon))
        {
            Entries.Add(entry);
        }

        if (lowest != uint.MaxValue)
        {
            FirstImageId = lowest;
        }

        if (Path.GetFileNameWithoutExtension(chosen) is { Length: > 0 } stem)
        {
            PackageName = stem;
        }

        Report = missing.Count == 0
            ? string.Create(CultureInfo.CurrentCulture, $"已載入 {read.Count} 筆條目,可繼續編輯後重新打包。")
            : string.Create(CultureInfo.CurrentCulture,
                $"已載入 {read.Count} 筆條目。有 {missing.Count} 筆的圖不在 pak 裡,已略過:{string.Join("、", missing)}。");
    }

    /// <summary>Asks for the pictures the next entry plays through.</summary>
    [RelayCommand]
    public void PickFrames()
    {
        var chosen = _prompts.OpenFiles("選擇 PNG 幀(順序即播放順序)", "PNG (*.png)|*.png|所有檔案|*.*");

        if (chosen.Count == 0)
        {
            return;
        }

        var frames = new List<byte[]>(chosen.Count);

        foreach (var path in chosen)
        {
            try
            {
                frames.Add(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Report = $"{path} 讀不出來:{e.Message}";

                return;
            }
        }

        _pending = frames;
        PendingSummary = string.Create(CultureInfo.CurrentCulture, $"已選 {frames.Count} 幀");
        AddEntryCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddEntry => _pending.Count > 0;

    /// <summary>
    /// Puts the chosen pictures in as one entry.
    /// </summary>
    /// <remarks>
    /// An entry already replacing the same icon is replaced rather than added beside: the
    /// client scans this table and stops at the first match, so two entries for one icon
    /// means the second is never reached.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    public void AddEntry()
    {
        if (Whole(Icon, 1, ushort.MaxValue) is not { } icon)
        {
            Report = $"目標 gfxid 要是 1 到 {ushort.MaxValue} 的數字。";

            return;
        }

        if (Whole(FrameMilliseconds, 1, ushort.MaxValue) is not { } frame)
        {
            Report = $"速度要在 1 到 {ushort.MaxValue} ms 之間。";

            return;
        }

        if (_pending.Count > IconAnimation.MaxFrames)
        {
            Report = $"一個條目最多 {IconAnimation.MaxFrames} 幀,目前是 {_pending.Count} 幀。";

            return;
        }

        var rest = Whole(RestMilliseconds, 0, int.MaxValue) ?? 0;
        var entry = new IconEntryViewModel((ushort)icon, (ushort)frame, rest, _pending);

        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (Entries[i].Icon == entry.Icon)
            {
                Entries.RemoveAt(i);
            }
        }

        Entries.Add(entry);

        // Sorted, because the table the client scans is read in order and an operator
        // comparing two packages should see the same list both times.
        var sorted = Entries.OrderBy(e => e.Icon).ToList();

        Entries.Clear();

        foreach (var sortedEntry in sorted)
        {
            Entries.Add(sortedEntry);
        }

        _pending = [];
        PendingSummary = "尚未選擇幀";
        Selected = entry;
        AddEntryCommand.NotifyCanExecuteChanged();
        Report = string.Create(CultureInfo.CurrentCulture, $"gfxid {entry.Icon} 現在有 {entry.Frames.Count} 幀。");
    }

    private bool CanRemoveEntry => Selected is not null;

    /// <summary>Takes an entry out.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveEntry))]
    public void RemoveEntry()
    {
        if (Selected is not { } entry)
        {
            return;
        }

        var at = Entries.IndexOf(entry);

        Entries.Remove(entry);
        Selected = Entries.Count == 0 ? null : Entries[Math.Min(at, Entries.Count - 1)];
    }

    /// <summary>
    /// Writes the package and its index.
    /// </summary>
    /// <returns>What was written, for a caller that wants to check.</returns>
    [RelayCommand]
    public void Pack()
    {
        if (Entries.Count == 0)
        {
            Report = "沒有條目可以打包。";

            return;
        }

        if (PackageName.Trim() is not { Length: > 0 } name)
        {
            Report = "pak 名不可以留白。";

            return;
        }

        if (_prompts.OpenFolder("選擇輸出資料夾(遊戲目錄)") is not { } directory)
        {
            return;
        }

        var (files, animations, used) = Build(Whole(FirstImageId, 0, uint.MaxValue) ?? DefaultFirstImageId);

        try
        {
            var (package, index) = SpritePackage.Build(files);

            File.WriteAllBytes(Path.Combine(directory, name + ".pak"), package);
            File.WriteAllBytes(Path.Combine(directory, name + ".idx"), index);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Report = $"pak 寫不出來:{e.Message}";

            return;
        }

        Report = string.Create(CultureInfo.CurrentCulture,
            $"""
             已寫入 {directory}:{name}.pak 與 {name}.idx
             {animations.Count} 筆條目 · {used} 張圖
             記得在「客戶端」頁勾選動態道具圖,並把 pak 名填成 {name}。
             """);
    }

    /// <summary>Lays the pictures out and numbers them, and writes the manifest for them.</summary>
    private (List<(string Name, byte[] Content)> Files, List<IconAnimation> Animations, int Pictures) Build(uint first)
    {
        var files = new List<(string Name, byte[] Content)>();
        var animations = new List<IconAnimation>();
        var next = first;

        foreach (var entry in Entries)
        {
            var ids = new List<uint>(entry.Frames.Count);

            foreach (var frame in entry.Frames)
            {
                files.Add(($"{next}.png", frame));
                ids.Add(next);
                next++;
            }

            animations.Add(entry.ToAnimation(ids));
        }

        // First in the package, which is where the launcher looks for it.
        files.Insert(0, (DynamicIconManifest.FileName,
            Encoding.UTF8.GetBytes(DynamicIconManifest.ToXml(animations))));

        return (files, animations, files.Count - 1);
    }

    /// <summary>Rounds and bounds what a box holds, or null when it is outside the range.</summary>
    private static uint? Whole(double value, uint low, uint high)
    {
        if (double.IsNaN(value))
        {
            return null;
        }

        var rounded = Math.Round(value);

        return rounded < low || rounded > high ? null : (uint)rounded;
    }
}
