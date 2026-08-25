using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Core.Servers;
using Login38.Core.Text;
using Login38.Encoder.Services;

namespace Login38.Encoder.ViewModels;

/// <summary>
/// What the launcher does to the client, as the operator decides it.
/// </summary>
/// <remarks>
/// <para>
/// Every switch here becomes a line in <c>config.ini</c>, and every one of those becomes a
/// patch the launcher applies or does not. So this is the operator's half of the patch
/// list — nothing is decided by the launcher itself.
/// </para>
/// <para>
/// The advanced anti-cheat option is not offered. It is in the file format and the
/// launcher clears it on both load and save, so a switch for it would be a switch that
/// does nothing — which is what the reference had, drawn and then disabled.
/// </para>
/// </remarks>
public sealed partial class AuxViewModel : ObservableObject
{
    /// <summary>The code pages the client's own text can be read as.</summary>
    /// <remarks>
    /// All three, including guessing. The reference offered two and mapped guessing onto
    /// one of them, so an operator who had chosen it lost that choice the next time
    /// anything was saved.
    /// </remarks>
    public IReadOnlyList<TextEncodingMode> Encodings { get; } = [.. Enum.GetValues<TextEncodingMode>()];

    [ObservableProperty]
    private bool _packetEncrypt;

    [ObservableProperty]
    private bool _antiCheatBasic;

    /// <summary>Whether the client reads a morph table the operator supplies.</summary>
    [ObservableProperty]
    private bool _morphTable;

    /// <summary>
    /// The packed tables sitting beside the encoder, offered as a starting point.
    /// </summary>
    /// <remarks>
    /// Only a suggestion. What matters is the name in the game's own directory, which is
    /// not necessarily where the encoder is, so the box stays typeable.
    /// </remarks>
    public ObservableCollection<string> MorphTables { get; } = [];

    /// <summary>
    /// Which table the launcher loads, by name.
    /// </summary>
    /// <remarks>
    /// Empty means the one named after the client executable, which is where the reference
    /// always looked and is what an operator with a single table still gets. The
    /// reference drew a dropdown of these and saved it nowhere, so choosing did nothing.
    /// </remarks>
    [ObservableProperty]
    private string _morphTableName = string.Empty;

    /// <summary>Why the list of tables is the way it is, when that needs saying.</summary>
    /// <remarks>
    /// Empty when there is nothing to explain. An operator with a packed table in the
    /// directory and an empty list has to be told which of the two possible reasons it is,
    /// or the tool looks broken — which is exactly what it looked like.
    /// </remarks>
    [ObservableProperty]
    private string _morphTablesNote = string.Empty;

    /// <summary>Whether <see cref="MorphTablesNote"/> has anything to say.</summary>
    public bool MorphTablesNoted => MorphTablesNote.Length > 0;

    /// <summary>Looks again for what has been packed.</summary>
    /// <param name="directory">Where the encoder writes them.</param>
    public void RescanMorphTables(string directory)
    {
        var chosen = MorphTableName;

        MorphTables.Clear();

        foreach (var package in MorphTools.Packages(directory))
        {
            MorphTables.Add(Path.GetFileNameWithoutExtension(package));
        }

        var foreign = MorphTools.ForeignPackages(directory);

        MorphTablesNote = (MorphTables.Count, foreign.Count) switch
        {
            (0, 0) => "這個目錄下還沒有打包好的變身檔,請先到「變身檔」頁打包。",
            (_, 0) => string.Empty,
            (_, _) => string.Create(
                CultureInfo.CurrentCulture,
                $"另外有 {foreign.Count} 個 .pak 不是本工具產出的,登入器會拒絕載入:{string.Join("、", foreign)}"),
        };

        // Kept whatever happened to the directory. A table an operator named is a table
        // they may not have built here, and clearing the box would silently move the
        // launcher back to the client-named one.
        MorphTableName = chosen;
    }

    partial void OnMorphTablesNoteChanged(string value) => OnPropertyChanged(nameof(MorphTablesNoted));

    [ObservableProperty]
    private bool _movePacketPlain;

    [ObservableProperty]
    private bool _helper = true;

    [ObservableProperty]
    private bool _hitPointLimit = true;

    [ObservableProperty]
    private bool _armourLimit = true;

    [ObservableProperty]
    private bool _inventoryLimit = true;

    [ObservableProperty]
    private double _inventorySize = AuxConfig.InventoryLimitBounds.Default;

    [ObservableProperty]
    private bool _equipmentSlots = true;

    [ObservableProperty]
    private bool _imageLimit = true;

    [ObservableProperty]
    private double _imageSize = AuxConfig.ImgLimitBounds.Default;

    [ObservableProperty]
    private bool _dynamicDialog = true;

    [ObservableProperty]
    private bool _dynamicIcons;

    [ObservableProperty]
    private string _iconPackageName = "123";

    [ObservableProperty]
    private bool _pickupToast = true;

    [ObservableProperty]
    private bool _gainDrift = true;

    [ObservableProperty]
    private bool _internalBot;

    [ObservableProperty]
    private bool _multipleCopies;

    [ObservableProperty]
    private double _copyLimit = AuxConfig.MultiInstanceLimitBounds.Default;

    [ObservableProperty]
    private TextEncodingMode _encoding = TextEncodingMode.Big5;

    /// <summary>Fills the switches in from the settings.</summary>
    public void Load(AuxConfig aux)
    {
        ArgumentNullException.ThrowIfNull(aux);

        PacketEncrypt = aux.PacketEncrypt;
        AntiCheatBasic = aux.AntiCheatBasic;
        MorphTable = aux.TransformFile;
        MorphTableName = aux.TransformFileName;
        MovePacketPlain = aux.MovePacketNoEncrypt;
        Helper = aux.LhxAuxEnabled;
        HitPointLimit = aux.HpMpLimitEnabled;
        ArmourLimit = aux.AcMrLimitEnabled;
        InventoryLimit = aux.InventoryLimitEnabled;
        InventorySize = aux.InventoryLimitValue;
        EquipmentSlots = aux.EquipUiEnabled;
        ImageLimit = aux.ImgLimitEnabled;
        ImageSize = aux.ImgLimitValue;
        DynamicDialog = aux.DynamicDialogEnabled;
        DynamicIcons = aux.DynamicIconEnabled;
        IconPackageName = aux.DynamicIconPakName;
        PickupToast = aux.PickupToastEnabled;
        GainDrift = aux.ExpDriftEnabled;
        InternalBot = aux.InternalBotEnabled;
        MultipleCopies = aux.MultiInstance;
        CopyLimit = aux.MultiInstanceLimit;
        Encoding = aux.TextEncoding;
    }

    /// <summary>Reads them back out.</summary>
    public AuxConfig ToConfig() => new AuxConfig
    {
        PacketEncrypt = PacketEncrypt,
        AntiCheatBasic = AntiCheatBasic,
        TransformFile = MorphTable,
        TransformFileName = MorphTableName.Trim(),
        MovePacketNoEncrypt = MovePacketPlain,
        LhxAuxEnabled = Helper,
        HpMpLimitEnabled = HitPointLimit,
        AcMrLimitEnabled = ArmourLimit,
        InventoryLimitEnabled = InventoryLimit,
        InventoryLimitValue = Bounded(
            InventorySize, AuxConfig.InventoryLimitBounds.Min, AuxConfig.InventoryLimitBounds.Max),
        EquipUiEnabled = EquipmentSlots,
        ImgLimitEnabled = ImageLimit,
        ImgLimitValue = Bounded(
            ImageSize, AuxConfig.ImgLimitBounds.Min, AuxConfig.ImgLimitBounds.Max),
        DynamicDialogEnabled = DynamicDialog,
        DynamicIconEnabled = DynamicIcons,

        // Never empty: the launcher opens this by name, and an empty one would have it
        // look for a file called `.pak`.
        DynamicIconPakName = IconPackageName.Trim() is { Length: > 0 } name ? name : "123",
        PickupToastEnabled = PickupToast,
        ExpDriftEnabled = GainDrift,
        InternalBotEnabled = InternalBot,
        MultiInstance = MultipleCopies,
        MultiInstanceLimit = Bounded(
            CopyLimit, AuxConfig.MultiInstanceLimitBounds.Min, AuxConfig.MultiInstanceLimitBounds.Max),
        TextEncoding = Encoding,
    }.Normalized();

    /// <summary>
    /// Rounds and bounds what a box holds.
    /// </summary>
    /// <remarks>
    /// The reference read these back with a parse that fell to the default when it failed,
    /// so an inventory limit typed as <c>25o</c> silently became 255 — and the operator
    /// found out when a player complained.
    /// </remarks>
    private static uint Bounded(double value, uint low, uint high) =>
        double.IsNaN(value) ? low : (uint)Math.Clamp(Math.Round(value), low, high);
}
