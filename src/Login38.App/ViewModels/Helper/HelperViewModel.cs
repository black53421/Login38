using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Login38.Aux.Settings;

namespace Login38.App.ViewModels.Helper;

/// <summary>
/// The helper's settings: everything the player can switch on for one character.
/// </summary>
/// <remarks>
/// <para>
/// A change here is published to the settings the helper loop reads, and takes effect on
/// its next pass — a tenth of a second later. Nothing is applied from this thread. The
/// reference wrote its patches from the window's own callbacks, which is why every switch
/// needed a live game handle and why a switch moved with no game running was lost.
/// </para>
/// <para>
/// Nothing here saves. Settings belong to a character, and the character is not known
/// until they walk into the world; the profile task loads them then and writes them back
/// when they leave. So this reads what was published and publishes what was changed, and
/// the file takes care of itself.
/// </para>
/// </remarks>
public sealed partial class HelperViewModel : ObservableObject, IDisposable
{
    private readonly AuxSettingsSource _settings;
    private readonly ItemCatalog _catalog;
    private readonly InventoryWatch? _bag;
    private readonly SpellWatch? _spells;
    private readonly HuntSwitch? _hunting;
    private readonly TimerTask? _timers;
    private readonly Action<Action> _post;

    /// <summary>Carried rather than owned: nothing in this window edits these.</summary>
    private List<HelperEntry> _inventoryEntries = [];

    /// <summary>The last settings this published, to tell its own changes from anybody else's.</summary>
    private AuxSettings? _mine;

    private bool _loading;
    private bool _closed;

    /// <param name="settings">What the helper loop is acting on.</param>
    /// <param name="catalog">The names the player's own file offers.</param>
    /// <param name="bag">What is in the bag, when there is a game to read one from.</param>
    /// <param name="timers">The timers, for the per-row button that starts one again.</param>
    /// <param name="post">
    /// How to get onto the thread that owns the window. The settings can be replaced by the
    /// helper loop — that is what happens when a character walks into the world — and the
    /// controls showing them may only be touched from the thread that made them.
    /// </param>
    public HelperViewModel(
        AuxSettingsSource settings,
        ItemCatalog catalog,
        InventoryWatch? bag = null,
        TimerTask? timers = null,
        SpellWatch? spells = null,
        HuntSwitch? hunting = null,
        Action<Action>? post = null,
        bool huntingOffered = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        HuntingOffered = huntingOffered;

        _settings = settings;
        _catalog = catalog;
        _bag = bag;
        _timers = timers;
        _spells = spells;
        _hunting = hunting;
        _post = post ?? (work => work());

        // Asked for from here rather than from a checkbox, because unlike the bag there is
        // nothing to weigh up: the loop reads one pointer a second and rebuilds only when the
        // player has changed character. The window is made once per game and hidden rather
        // than closed, so this stays on for as long as the game does.
        if (_spells is not null)
        {
            _spells.Wanted = true;
        }

        Potions = [.. Enumerable.Range(0, AuxSettings.PotionRows).Select(_ => new PotionRowViewModel())];
        Macros = [.. Enumerable.Range(1, AuxSettings.FunctionKeyMacros).Select(key => new MacroViewModel(key))];
        Timers = [.. Enumerable.Range(0, AuxSettings.Timers).Select(row => new TimerRowViewModel(row))];

        foreach (var part in Parts())
        {
            part.PropertyChanged += (_, _) => Apply();
        }

        foreach (var list in Lists())
        {
            list.Items.CollectionChanged += OnListChanged;
        }

        RefreshChoices();
        Load(settings.Current);

        settings.Published += OnPublished;
    }

    // ---- 喝水 -------------------------------------------------------------------------

    /// <summary>The drinking rules, in the order they are tried.</summary>
    public IReadOnlyList<PotionRowViewModel> Potions { get; }

    /// <summary>The rule for topping up mana only while it is safe to.</summary>
    public ManaWhenSafeViewModel Mana { get; } = new();

    /// <summary>Whether a drinking threshold is a percentage rather than an amount.</summary>
    [ObservableProperty]
    private bool _usePercent;

    /// <summary>Whether the drinking list offers what is in the bag as well.</summary>
    [ObservableProperty]
    private bool _showInventory;

    /// <summary>What to offer for a drinking rule.</summary>
    public ObservableCollection<string> HealingChoices { get; } = [];

    /// <summary>And for the mana rule.</summary>
    public ObservableCollection<string> ManaChoices { get; } = [];

    // ---- 輔助 -------------------------------------------------------------------------

    /// <summary>Whether the helper keeps anything up.</summary>
    [ObservableProperty]
    private bool _helperEnabled;

    /// <summary>What the player's file knows how to keep up.</summary>
    public ObservableCollection<string> StateChoices { get; } = [];

    /// <summary>What this character keeps up, in the order it is tried.</summary>
    public ChoiceListViewModel Buffs { get; } = new();

    // ---- 狀態 -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _showExperience;

    [ObservableProperty]
    private bool _keepWeaponSharp;

    [ObservableProperty]
    private bool _eatWhenHungry;

    [ObservableProperty]
    private bool _transformEnabled;

    /// <summary>The scroll to use.</summary>
    [ObservableProperty]
    private string? _transformItem;

    /// <summary>Which shape to answer it with.</summary>
    [ObservableProperty]
    private string? _transformCondition;

    public ObservableCollection<string> TransformItemChoices { get; } = [];

    public ObservableCollection<string> TransformationChoices { get; } = [];

    [ObservableProperty]
    private bool _antidoteEnabled;

    [ObservableProperty]
    private string? _antidoteItem;

    public ObservableCollection<string> AntidoteChoices { get; } = [];

    /// <summary>The function keys, F1 upwards.</summary>
    public IReadOnlyList<MacroViewModel> Macros { get; }

    // ---- 刪物 -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _deleteEnabled;

    /// <summary>What to destroy outright.</summary>
    public ChoiceListViewModel Delete { get; } = new();

    /// <summary>What to dissolve, which needs a solvent in the bag.</summary>
    public ChoiceListViewModel Dissolve { get; } = new();

    /// <summary>What is in the bag, for picking something to get rid of.</summary>
    public ObservableCollection<string> BagChoices { get; } = [];

    /// <summary>
    /// What this character has learned that acts on a target, for the rotation.
    /// </summary>
    /// <remarks>
    /// Typed names were the only way to fill a rotation row before this, and a mistyped one
    /// is silent: the hunt says once in the log that the character never learned it and then
    /// gets on with the rest of the list. The box stays editable so a name the client spells
    /// differently can still be written by hand.
    /// </remarks>
    public ObservableCollection<string> SkillChoices { get; } = [];

    // ---- 喊話 -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _shoutEnabled;

    /// <summary>How long between messages, in seconds.</summary>
    [ObservableProperty]
    private double _shoutIntervalSeconds = HelperNumbers.ShortestInterval;

    /// <summary>What to say, one per interval, in turn.</summary>
    public ChoiceListViewModel Shout { get; } = new();

    // ---- 狩獵 -------------------------------------------------------------------------

    /// <summary>What to attack, and when to stop trying.</summary>
    public HuntViewModel Hunt { get; } = new();

    /// <summary>
    /// Whether this build offers automatic hunting, which decides if its page is there.
    /// </summary>
    /// <remarks>
    /// The operator's switch, from the server list rather than from anything the player can
    /// reach — see <c>AuxConfig.InternalBotEnabled</c>. Fixed for the life of the window,
    /// which is why nothing raises a change for it.
    /// </remarks>
    public bool HuntingOffered { get; }

    // ---- 其他 -------------------------------------------------------------------------

    /// <summary>The switches that reach into the client.</summary>
    public MiscViewModel Misc { get; } = new();

    // ---- 定時 -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _timersEnabled;

    /// <summary>The timed commands.</summary>
    public IReadOnlyList<TimerRowViewModel> Timers { get; }

    /// <summary>
    /// Starts one row's interval again from now.
    /// </summary>
    /// <remarks>
    /// For a player who has just done by hand what the row does, and does not want it done
    /// again in two seconds. It reaches the task that is counting rather than a flag it
    /// might notice — the reference pushed a counter the timer thread watched for, through
    /// a weak reference that could quietly have gone.
    /// </remarks>
    [RelayCommand]
    private void RestartTimer(TimerRowViewModel? row)
    {
        if (row is not null)
        {
            _timers?.Restart(row.Row);
        }
    }

    // ---- 讀寫 -------------------------------------------------------------------------

    /// <summary>
    /// Whether the window is open, so the loop knows whether to read the bag.
    /// </summary>
    /// <remarks>
    /// Reading the bag means walking the client's memory across a process boundary. Worth
    /// doing once a second while somebody is choosing from a list of it, and worth doing
    /// never otherwise.
    /// </remarks>
    public bool Watching
    {
        get => _bag?.Wanted ?? false;
        set
        {
            if (_bag is not null)
            {
                _bag.Wanted = value;
            }
        }
    }

    /// <summary>
    /// Shows a different character's settings.
    /// </summary>
    /// <remarks>
    /// Called when the profile task publishes — a character walked into the world, or the
    /// player came back out and picked a different one. Nothing is published back while
    /// this runs, or every control filled in would be a change to publish.
    /// </remarks>
    public void Load(AuxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _loading = true;

        try
        {
            Profile = settings.Profile;
            UsePercent = settings.PotionUsePercent;
            ShowInventory = settings.PotionShowInventory;

            for (var i = 0; i < Potions.Count; i++)
            {
                Potions[i].Load(settings.Potions[i]);
            }

            Mana.Load(settings.ManaWhenSafe);

            HelperEnabled = settings.HelperEnabled;
            Buffs.Load(settings.HelperEntries.Select(HelperEntrySyntax.ToSettingText));

            ShowExperience = settings.ShowExperience;
            KeepWeaponSharp = settings.KeepWeaponSharp;
            EatWhenHungry = settings.EatWhenHungry;
            TransformEnabled = settings.TransformEnabled;
            TransformItem = settings.TransformItem;
            TransformCondition = settings.TransformCondition;
            AntidoteEnabled = settings.AntidoteEnabled;
            AntidoteItem = settings.AntidoteItem;

            for (var i = 0; i < Macros.Count; i++)
            {
                Macros[i].Load(settings.Macros[i]);
            }

            DeleteEnabled = settings.DeleteEnabled;
            Delete.Load(settings.DeleteList);
            Dissolve.Load(settings.DissolveList);

            ShoutEnabled = settings.ShoutEnabled;
            ShoutIntervalSeconds = Math.Clamp(
                settings.ShoutIntervalSeconds,
                HelperNumbers.ShortestInterval,
                HelperNumbers.LongestInterval);
            Shout.Load(settings.ShoutMessages);

            Hunt.Load(settings.Hunt);
            Misc.Load(settings.Misc);

            TimersEnabled = settings.TimersEnabled;

            for (var i = 0; i < Timers.Count; i++)
            {
                Timers[i].Load(settings.TimerRows[i]);
            }

            // Dropping these on a round trip would lose whatever put them there.
            _inventoryEntries = settings.HelperInventoryEntries;

            ApplyCeilings();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Whose settings are on screen, or empty before a character is in the world.</summary>
    [ObservableProperty]
    private string _profile = string.Empty;

    /// <summary>Builds the settings the helper loop should act on.</summary>
    public AuxSettings ToSettings() => new()
    {
        Profile = Profile,
        PotionUsePercent = UsePercent,
        PotionShowInventory = ShowInventory,
        Potions = [.. Potions.Select(row => row.ToRow())],
        ManaWhenSafe = Mana.ToRule(),

        HelperEnabled = HelperEnabled,
        HelperEntries = [.. Buffs.Items.Select(HelperEntrySyntax.Parse)],
        HelperInventoryEntries = _inventoryEntries,

        ShowExperience = ShowExperience,
        KeepWeaponSharp = KeepWeaponSharp,
        EatWhenHungry = EatWhenHungry,
        TransformEnabled = TransformEnabled,
        TransformItem = TransformItem?.Trim() ?? string.Empty,
        TransformCondition = TransformCondition?.Trim() ?? string.Empty,
        AntidoteEnabled = AntidoteEnabled,
        AntidoteItem = AntidoteItem?.Trim() ?? string.Empty,
        Macros = [.. Macros.Select(macro => macro.ToMacro())],

        DeleteEnabled = DeleteEnabled,
        DeleteList = [.. Delete.Items],
        DissolveList = [.. Dissolve.Items],

        ShoutEnabled = ShoutEnabled,
        ShoutIntervalSeconds = HelperNumbers.Whole(
            ShoutIntervalSeconds, HelperNumbers.ShortestInterval, HelperNumbers.LongestInterval),
        ShoutMessages = [.. Shout.Items],

        // Off rather than as last saved, when the operator has not offered it. A settings
        // file written on a build that did would otherwise leave the character hunting with
        // no page to say so and no way to stop it.
        Hunt = HuntingOffered ? Hunt.ToSettings() : Hunt.ToSettings().Off(),
        Misc = Misc.ToToggles(),

        TimersEnabled = TimersEnabled,
        TimerRows = [.. Timers.Select(row => row.ToRow())],
    };

    /// <summary>Where the file of names lives, for the player who wants to edit it.</summary>
    public string ItemListPath => _catalog.Path;

    /// <summary>Reads the player's file again, for one they edited while this was open.</summary>
    [RelayCommand]
    public void ReloadItemList()
    {
        _catalog.Forget();
        RefreshChoices();
    }

    /// <summary>
    /// Opens the file of names in whatever the player edits text with.
    /// </summary>
    /// <remarks>
    /// Written out first if it is not there. The reference told the player where the file
    /// was in a comment inside the file, which they could only read by finding it.
    /// </remarks>
    [RelayCommand]
    private void EditItemList()
    {
        _catalog.Ensure();

        try
        {
            using var editor = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_catalog.Path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
                                  or InvalidOperationException or System.IO.FileNotFoundException)
        {
            // Nothing is registered for .ini, or the file could not be written. Neither is
            // worth interrupting a game for: every list still has the shipped copy behind it.
        }
    }

    /// <summary>
    /// Offers whatever is in the bag now.
    /// </summary>
    /// <remarks>
    /// Called on a timer while the window is open. The lists are rebuilt only when they
    /// would come out different, because replacing the contents of a list that a dropdown
    /// is showing closes the dropdown.
    /// </remarks>
    public void RefreshInventory()
    {
        // A task has read an escape scroll and turned the hunt off in the settings. The
        // checkbox has to follow, and not because it looks untidy: this window writes the
        // whole of its state back whenever anything on it changes, so a tick left standing
        // here starts the character hunting again the next time its owner adjusts a potion
        // row — at the worst possible moment, since the reason it stopped was low health.
        if (_hunting?.Taken() == true)
        {
            Hunt.Enabled = false;
        }

        var bag = _bag?.Names ?? [];

        Sync(BagChoices, bag);
        Sync(SkillChoices, _spells?.Names ?? []);
        Sync(HealingChoices, ShowInventory
            ? _catalog.Offer(ItemCatalog.Healing).Concat(bag)
            : _catalog.Offer(ItemCatalog.Healing));
    }

    /// <summary>Fills every list of suggestions from the player's file.</summary>
    private void RefreshChoices()
    {
        Sync(ManaChoices, _catalog.Offer(ItemCatalog.ManaConversion));
        Sync(TransformItemChoices, _catalog.Offer(ItemCatalog.TransformItems));
        Sync(TransformationChoices, _catalog.Offer(ItemCatalog.Transformations));
        Sync(AntidoteChoices, _catalog.Offer(ItemCatalog.Antidotes));

        // Through the syntax both ways, so the list of what can be kept up is spelled the
        // same as the list of what is being kept up. They come from different places and
        // the file's own spelling is the older one.
        Sync(StateChoices, _catalog.Offer(ItemCatalog.States)
            .Select(line => HelperEntrySyntax.ToSettingText(HelperEntrySyntax.Parse(line))));

        RefreshInventory();
    }

    /// <summary>Everything that is a change to the settings when the player touches it.</summary>
    /// <remarks>
    /// The rotation's rows were missing from here, and a row is its own object — so filling one
    /// in raised nothing anything was listening to, nothing was published, and the hunt went on
    /// with whatever was saved before. From the window it looked entirely done: the name was in
    /// the box and stayed there. The only way to make a skill take was to touch some other
    /// setting afterwards and have it carry the row along.
    /// </remarks>
    private IEnumerable<INotifyPropertyChanged> Parts() =>
    [
        this, Mana, Misc, Hunt, .. Potions, .. Macros, .. Timers, .. Hunt.Skills,
        Buffs, Delete, Dissolve, Shout, Hunt.Blacklist, Hunt.Whitelist,
    ];

    private IEnumerable<ChoiceListViewModel> Lists() =>
        [Buffs, Delete, Dissolve, Shout, Hunt.Blacklist, Hunt.Whitelist];

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => Apply();

    /// <inheritdoc/>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        ArgumentNullException.ThrowIfNull(e);

        if (e.PropertyName == nameof(UsePercent))
        {
            ApplyCeilings();
        }

        if (e.PropertyName == nameof(ShowInventory))
        {
            RefreshInventory();
        }
    }

    /// <summary>Publishes what the loop should act on from here.</summary>
    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        var settings = ToSettings();

        _mine = settings;
        _settings.Publish(settings);
    }

    /// <summary>
    /// Shows what somebody else published.
    /// </summary>
    /// <remarks>
    /// Which in practice means the profile task: a character walked into the world, or the
    /// player came back out and picked a different one, and these are that character's
    /// settings. Told apart from this window's own changes by identity — the settings that
    /// come back are the very object that was handed over — because re-filling every
    /// control on each keystroke would fight with whoever was typing.
    /// </remarks>
    private void OnPublished(object? sender, AuxSettings published)
    {
        if (ReferenceEquals(published, _mine))
        {
            return;
        }

        _post(() =>
        {
            if (!_closed)
            {
                Load(published);
            }
        });
    }

    /// <summary>Tells the drinking rules how large a number they may hold.</summary>
    private void ApplyCeilings()
    {
        var ceiling = UsePercent ? HelperNumbers.Percent : HelperNumbers.Points;

        foreach (var row in Potions)
        {
            row.Ceiling = ceiling;
        }
    }

    /// <summary>Stops watching the game and stops listening for other people's changes.</summary>
    public void Dispose()
    {
        _closed = true;
        _settings.Published -= OnPublished;

        Watching = false;
    }

    /// <summary>
    /// Makes a list of suggestions match, without replacing it when it already does.
    /// </summary>
    private static void Sync(ObservableCollection<string> target, IEnumerable<string> wanted)
    {
        var list = wanted.ToList();

        if (target.SequenceEqual(list, StringComparer.Ordinal))
        {
            return;
        }

        target.Clear();

        foreach (var item in list)
        {
            target.Add(item);
        }
    }
}
