using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Login38.Core.Morph;
using Login38.Core.Text;
using Login38.Encoder.Services;

namespace Login38.Encoder.ViewModels;

/// <summary>
/// The morph table: pack one for the launcher, and read the server's animation table
/// out of it.
/// </summary>
/// <remarks>
/// <para>
/// A morph table decides what every sprite in the client looks like and how long each of
/// its animations runs. Two different things need it: the launcher, which feeds it to the
/// client, and the server, which needs the timings to know when an action is over.
/// </para>
/// <para>
/// Both come from the same text file the operator maintains, which is why they are on one
/// page.
/// </para>
/// </remarks>
public sealed partial class MorphViewModel : ObservableObject
{
    private readonly string _root;
    private readonly ILegacyTextCodec _codec;
    private readonly IFilePrompts _prompts;

    /// <param name="root">Where the encoder is, which is where its output goes.</param>
    public MorphViewModel(string root, ILegacyTextCodec codec, IFilePrompts prompts)
    {
        _root = root;
        _codec = codec;
        _prompts = prompts;
    }

    /// <summary>The table the operator maintains.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EncodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateSqlCommand))]
    private string _source = string.Empty;

    /// <summary>What happened last, for the operator to read.</summary>
    [ObservableProperty]
    private string _report = string.Empty;

    /// <summary>
    /// Raised once a table has been packed into the encoder's own directory.
    /// </summary>
    /// <remarks>
    /// The list of tables an operator can choose from is somewhere else entirely, and a
    /// list built when the window opened does not know about a file made since.
    /// </remarks>
    public event EventHandler? Packed;

    /// <summary>Asks for the table.</summary>
    [RelayCommand]
    public void Browse()
    {
        if (_prompts.OpenFile("選擇變身原始檔", "變身原始檔 (*.txt)|*.txt|所有檔案|*.*") is { } chosen)
        {
            Source = chosen;
        }
    }

    private bool CanUseSource => !string.IsNullOrWhiteSpace(Source);

    /// <summary>
    /// Packs the table for the launcher, beside the encoder.
    /// </summary>
    /// <remarks>
    /// The launcher finds it by the client's own name rather than by a setting, so where
    /// this lands is what the operator has to copy into the game's directory.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanUseSource))]
    public void Encode()
    {
        var source = Source.Trim();

        if (!File.Exists(source))
        {
            Report = $"{source} 這個檔案不存在。";

            return;
        }

        try
        {
            var done = MorphTools.Encode(source, MorphTools.OutputFor(_root, source));

            Report = string.Create(CultureInfo.CurrentCulture,
                $"""
                 已打包 {done.Output}
                 原始 {done.TableBytes:N0} bytes · 打包後 {done.PackageBytes:N0} bytes(小了 {done.Saved:F1}%)
                 把它複製到遊戲目錄,檔名跟客戶端執行檔同名。
                 """);

            Packed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is MorphPackageException or IOException or UnauthorizedAccessException)
        {
            Report = $"打包失敗:{e.Message}";
        }
    }

    /// <summary>Writes the server's animation table beside the encoder.</summary>
    [RelayCommand(CanExecute = nameof(CanUseSource))]
    public void GenerateSql()
    {
        var source = Source.Trim();

        if (!File.Exists(source))
        {
            Report = $"{source} 這個檔案不存在。";

            return;
        }

        var output = Path.Combine(_root, MorphTools.SprActionSqlName);

        try
        {
            var rows = MorphTools.WriteSprActionSql(_codec, source, output);

            Report = string.Create(CultureInfo.CurrentCulture,
                $"已寫入 {output} — {rows:N0} 筆。匯入伺服器資料庫即可。");
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            Report = $"動作時間表寫不出來:{e.Message}";
        }
    }

    /// <summary>
    /// Says whether a packed table is one this encoder made.
    /// </summary>
    /// <remarks>
    /// The launcher refuses anything else, so an operator whose players cannot log in
    /// needs a way to ask that question here rather than by watching a log.
    /// </remarks>
    [RelayCommand]
    public void Verify()
    {
        if (_prompts.OpenFile("選擇打包後的變身檔", "變身檔 (*.pak)|*.pak|所有檔案|*.*") is not { } chosen)
        {
            return;
        }

        Report = MorphTools.IsOurs(chosen)
            ? $"{chosen} 是本工具產出的,登入器會載入。"
            : $"{chosen} 不是本工具產出的,登入器會拒絕 — 請重新打包原始文字檔。";
    }
}
