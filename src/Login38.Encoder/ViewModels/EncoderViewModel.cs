using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Login38.Core.Cryptography;
using Login38.Core.Servers;
using Login38.Core.Text;
using Login38.Encoder.Services;

namespace Login38.Encoder.ViewModels;

/// <summary>
/// The operator's tool: what the launcher offers, what it does to the client, and what
/// it looks like doing it.
/// </summary>
/// <remarks>
/// <para>
/// Everything an operator sets here ends up in two files beside the launcher, and nothing
/// else. There is no installer and no registry: they edit here, zip the directory, and
/// hand it to their players.
/// </para>
/// <para>
/// All eight slots are written at once. The reference wrote whichever one was selected,
/// merging it into the file on disk by position — and then dropped the unnamed slots on
/// the way out, so a gap moved everything after it up one and the slot the operator was
/// editing was not the slot they got.
/// </para>
/// </remarks>
public sealed partial class EncoderViewModel : ObservableObject
{
    private readonly EncoderFiles _files;

    public EncoderViewModel(EncoderFiles files, ILegacyTextCodec codec, IFilePrompts prompts)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(prompts);

        _files = files;

        for (var slot = 0; slot < ListFile.MaxServers; slot++)
        {
            var server = new ServerSlotViewModel(slot);

            // Only the first is offered to begin with: an operator setting up one server
            // should not have to empty seven others first.
            if (slot > 0)
            {
                server.Clear();
            }

            Servers.Add(server);
        }

        // The setting is on another page and this is where its consequence is shown.
        Aux.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AuxViewModel.PacketEncrypt))
            {
                OnPropertyChanged(nameof(KeyWarning));
                OnPropertyChanged(nameof(KeyWarned));
            }
        };

        Launcher = new LauncherViewModel(files.Directory, prompts);
        Morph = new MorphViewModel(files.Directory, codec, prompts);
        Icons = new IconsViewModel(prompts);

        // A table packed on one page is a table the other page has to know about.
        Morph.Packed += (_, _) => RescanMorphTables();
        Selected = Servers[0];

        Reload();
    }

    /// <summary>The eight slots, whether or not they hold anything.</summary>
    public ObservableCollection<ServerSlotViewModel> Servers { get; } = [];

    [ObservableProperty]
    private ServerSlotViewModel? _selected;

    /// <summary>What the launcher does to the client.</summary>
    public AuxViewModel Aux { get; } = new();

    /// <summary>What the launcher itself looks like and links to.</summary>
    public LauncherViewModel Launcher { get; }

    /// <summary>The morph table and the server's animation table.</summary>
    public MorphViewModel Morph { get; }

    /// <summary>The package of animated item icons.</summary>
    public IconsViewModel Icons { get; }

    /// <summary>What happened last, for the operator to read.</summary>
    [ObservableProperty]
    private string _report = string.Empty;

    /// <summary>
    /// The key every server in this list is bound with, as typed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One key for the whole list. The format carries a copy in each slot and the client
    /// reads it out of the row the player picked, so all eight copies have to agree — a
    /// per-slot key was eight chances to bind a client to a server that would then reject
    /// it, and only the operator's memory kept them in step.
    /// </para>
    /// <para>
    /// Text rather than numbers so that an empty box means "no key" and stays empty. The
    /// reference showed these but never read them back, so an operator restoring a key
    /// from a backup could type it in and watch it be ignored.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyWarning))]
    [NotifyPropertyChangedFor(nameof(KeyWarned))]
    private string _rsaE = string.Empty;

    /// <inheritdoc cref="RsaE"/>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyWarning))]
    [NotifyPropertyChangedFor(nameof(KeyWarned))]
    private string _rsaD = string.Empty;

    /// <inheritdoc cref="RsaE"/>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyWarning))]
    [NotifyPropertyChangedFor(nameof(KeyWarned))]
    private string _rsaN = string.Empty;

    /// <summary>The key as the format holds it, with zero standing for "none".</summary>
    private Rsa32Key Key => new(Number(RsaE), Number(RsaD), Number(RsaN));

    /// <summary>Whether there is a key at all.</summary>
    /// <remarks>
    /// By the two halves that do the work. <c>E</c> is the client's own and is not what
    /// the relay encrypts with, so a list carrying only that is a list with no key.
    /// </remarks>
    public bool HasKey => Key.D != 0 && Key.N != 0;

    /// <summary>
    /// What is wrong with the key and the setting that turns it on, if anything.
    /// </summary>
    /// <remarks>
    /// The two are set on different pages and neither means anything without the other: a
    /// key with the setting off is never used, and the setting on without a key is a
    /// launch the launcher refuses. Saying so where the key is edited is the only place an
    /// operator would look.
    /// </remarks>
    public string KeyWarning => (Aux.PacketEncrypt, HasKey) switch
    {
        (false, true) => "「封包加密」沒有勾選,這組金鑰不會生效。",
        (true, false) => "已勾選「封包加密」但沒有金鑰,玩家會連不上 — 請按下面的按鈕產生一組。",
        _ => string.Empty,
    };

    /// <summary>Whether <see cref="KeyWarning"/> has anything to say.</summary>
    public bool KeyWarned => KeyWarning.Length > 0;

    /// <summary>Reads both files back in, throwing away anything unsaved.</summary>
    [RelayCommand]
    public void Reload()
    {
        var loaded = _files.Load();

        for (var slot = 0; slot < Servers.Count; slot++)
        {
            if (slot < loaded.File.Servers.Count)
            {
                Servers[slot].Load(loaded.File.Servers[slot]);
            }
            else
            {
                Servers[slot].Clear();
            }
        }

        // Nothing on disk at all: leave one slot with a name in it, so an operator opening
        // this for the first time has something to edit rather than eight empty rows.
        if (loaded.File.Servers.Count == 0)
        {
            Servers[0].Reset();
        }

        // The first slot that carries one wins. They are meant to agree, and an older
        // file written by the reference may have left a key in only one of them.
        var key = loaded.File.Servers.FirstOrDefault(s => s.RsaD != 0 && s.RsaN != 0);

        RsaE = Text(key?.RsaE ?? 0);
        RsaD = Text(key?.RsaD ?? 0);
        RsaN = Text(key?.RsaN ?? 0);

        Aux.Load(loaded.File.Aux);

        RescanMorphTables();
        Launcher.Load(loaded.File.Launcher);
        Selected = Servers[0];

        // Nothing to say when a load went cleanly. The list is on screen, which is the
        // report; a line counting what is already visible is noise, and it carried the
        // whole path with it every time the window opened.
        Report = loaded.Problems.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, loaded.Problems);
    }

    /// <summary>Writes both files.</summary>
    [RelayCommand]
    public void Save()
    {
        var named = Servers.Where(s => s.IsUsed).ToList();

        if (named.Count == 0)
        {
            Report = "沒有任何槽位填了名稱,玩家會看到一份空的伺服器列表。";

            return;
        }

        try
        {
            _files.Save(new ListFile
            {
                Servers = [.. named.Select(s => s.ToServer(Key))],
                Aux = Aux.ToConfig(),
                Launcher = Launcher.ToConfig(),
            });

            Report = string.Create(CultureInfo.CurrentCulture,
                $"已儲存:{EncoderFiles.ServerListName}({named.Count} 台)與 {EncoderFiles.ConfigName}。")
                + (KeyWarned ? Environment.NewLine + KeyWarning : string.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                  or ArgumentException or FormatException)
        {
            Report = $"寫不進去:{e.Message}";
        }
    }

    /// <summary>
    /// Makes the key that binds this client to this operator's server, and gives the
    /// server its half.
    /// </summary>
    /// <remarks>
    /// One key for every server in the list. The reference put it in whichever slot was
    /// selected, which meant an operator with more than one server had to remember to do
    /// it again for each, and a slot they forgot bound a client to nothing.
    /// </remarks>
    [RelayCommand]
    public void GenerateKey()
    {
        var key = Rsa32.Generate();

        RsaE = Text(key.E);
        RsaD = Text(key.D);
        RsaN = Text(key.N);

        try
        {
            Report = string.Create(CultureInfo.CurrentCulture,
                $"""
                 金鑰已產生,伺服器那一半寫到 {_files.WritePackProperties(key)}
                 把那個檔放進伺服器的 ./config/ 目錄,再按「儲存」寫入客戶端那一半。
                 這組金鑰會套用到列表裡的每一台伺服器。
                 """);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report = string.Create(CultureInfo.CurrentCulture,
                $"金鑰已產生,但 {EncoderFiles.PackPropertiesName} 寫不進去:{e.Message}。E={key.E} D={key.D} N={key.N}");
        }
    }

    /// <summary>
    /// Looks again for the packed tables beside the encoder.
    /// </summary>
    /// <remarks>
    /// Run whenever the operator could be about to look at the list, rather than once when
    /// the window opened. A table packed a moment ago is the one they came to choose.
    /// </remarks>
    [RelayCommand]
    public void RescanMorphTables() => Aux.RescanMorphTables(_files.Directory);

    /// <summary>Zero is how the format says there is no key; an empty box says the same.</summary>
    private static string Text(uint value) =>
        value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text"/>
    private static uint Number(string text) =>
        uint.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var value) ? value : 0;
}
