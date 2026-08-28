using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Actions;

/// <summary>
/// Covers what each suffix a player can write actually does.
/// </summary>
/// <remarks>
/// A helper entry is a name and a suffix, and the suffix decides between a dozen quite
/// different things: use it, use it on what is being worn, cast it on yourself, cast it at
/// an item. Getting one wrong sends a packet the server answers with an error, or uses the
/// wrong thing up.
/// </remarks>
public sealed class HelperDispatchTests : IDisposable
{
    private const string Scroll = "祝福的卷軸";
    private const string Sword = "銀劍";
    private const string Armour = "鎧甲";
    private const string Potion = "紅色藥水";

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingActions _actions = new();
    private readonly StubSpells _spells = new();
    private readonly StubEntities _entities = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void UsesAPlainItem()
    {
        Send(Potion, Bag(Potion)).ShouldBe(DispatchResult.Done);

        _actions.Used.ShouldBe([0x1000u]);
    }

    // Through the client's own item routine rather than as a packet, so the animation and
    // the sound happen. The potion rules do the opposite, on purpose.
    [Fact]
    public void CallsTheClientRatherThanSendingAPacket()
    {
        Send(Potion, Bag(Potion));

        _actions.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void SaysSoWhenTheItemIsNotInTheBag() =>
        Send(Potion, Bag(Sword)).ShouldBe(DispatchResult.Skipped);

    // A scroll on the armour being worn. The server wants both ids, and using the scroll on
    // its own only opens a cursor nobody is there to click.
    [Fact]
    public void UsesAScrollOnWhateverIsBeingWorn()
    {
        Send($"{Scroll}/IA", [Item(Scroll, 0x100), Worn(Armour, 0x200)])
            .ShouldBe(DispatchResult.Done);

        _actions.Pairs.ShouldBe([(0x100u, 0x200u)]);
    }

    [Fact]
    public void UsesAScrollOnWhateverIsBeingSwung()
    {
        Send($"{Scroll}/IW", [Item(Scroll, 0x100), Wielded(Sword, 0x300)])
            .ShouldBe(DispatchResult.Done);

        _actions.Pairs.ShouldBe([(0x100u, 0x300u)]);
    }

    // The name is a filter on top of the state, not instead of it: "the silver sword, and
    // only while it is the one being swung".
    [Fact]
    public void UsesAScrollOnANamedThingOnlyWhileItIsBeingSwung()
    {
        Send($"{Scroll}/IW={Sword}", [Item(Scroll, 0x100), Wielded("鐵劍", 0x300), Item(Sword, 0x400)])
            .ShouldBe(DispatchResult.Skipped);

        _actions.Pairs.ShouldBeEmpty();
    }

    [Fact]
    public void UsesAScrollOnAnItemNamedOutright()
    {
        Send($"{Scroll}/I={Sword}", [Item(Scroll, 0x100), Item(Sword, 0x400)])
            .ShouldBe(DispatchResult.Done);

        _actions.Pairs.ShouldBe([(0x100u, 0x400u)]);
    }

    [Fact]
    public void SaysSoWhenThereIsNothingToUseItOn() =>
        Send($"{Scroll}/IA", Bag(Scroll)).ShouldBe(DispatchResult.Skipped);

    // Aiming at another character by name. Nothing in the client maps names to the things
    // standing in the world, so this is the one entry that costs a walk of its heap.
    [Fact]
    public void UsesAScrollOnSomebodyStandingThere()
    {
        _entities.Present["某人"] = 0x5000;

        Send($"{Scroll}/IT=某人", [Item(Scroll, 0x100)]).ShouldBe(DispatchResult.Done);

        _actions.Pairs.ShouldBe([(0x100u, 0x5000u)]);
    }

    [Fact]
    public void SaysSoWhenNobodyThereIsCalledThat() =>
        Send($"{Scroll}/IT=某人", Bag(Scroll)).ShouldBe(DispatchResult.Skipped);

    // The scroll still has to be in the bag, and that is checked before the heap is walked:
    // a second of searching for somebody to use a scroll on that is not there is a second
    // the helper is not doing anything else.
    [Fact]
    public void DoesNotGoLookingWhenTheScrollIsNotInTheBag()
    {
        _entities.Present["某人"] = 0x5000;

        Send($"{Scroll}/IT=某人", Bag(Sword)).ShouldBe(DispatchResult.Skipped);

        _entities.Searches.ShouldBe(0);
    }

    [Fact]
    public void CastsOnThePlayer()
    {
        _spells.Known["加速術"] = 42;

        Send("加速術/ME", []).ShouldBe(DispatchResult.Cast);

        _actions.Casts.ShouldBe([(42u, SkillAim.Self)]);
    }

    [Fact]
    public void CastsWithoutNamingATarget()
    {
        _spells.Known["加速術"] = 42;

        Send("加速術/M", []).ShouldBe(DispatchResult.Cast);

        _actions.Casts.ShouldBe([(42u, SkillAim.Whatever)]);
    }

    // Nothing in this client writes a "what the mouse is over" global that a cast can read,
    // so aiming at the cursor is the same as not aiming. Sending a target the client cannot
    // resolve gets an error back that stops the cast entirely.
    [Fact]
    public void TreatsAimingAtTheCursorAsNotAiming()
    {
        _spells.Known["加速術"] = 42;

        Send(
            new HelperEntry(HelperEntry.NoState, "加速術", EntryKind.Skill,
                CastTarget.Any(CastKind.HoverTarget)),
            []);

        _actions.Casts.ShouldBe([(42u, SkillAim.Whatever)]);
    }

    // An item's id is not a world object's id, so this goes out as a packet the server
    // resolves against the player's own bag rather than through the client.
    [Fact]
    public void CastsAtAnItemBeingSwung()
    {
        _spells.Known["鑑定術"] = 7;

        Send("鑑定術/MIW", [Wielded(Sword, 0x300)]).ShouldBe(DispatchResult.Cast);

        _actions.Casts.ShouldBe([(7u, SkillAim.Item)]);
        _actions.CastTargets.ShouldBe([0x300u]);
    }

    [Fact]
    public void SaysSoWhenTheSkillHasNotBeenLearned() =>
        Send("加速術/ME", []).ShouldBe(DispatchResult.Skipped);

    // The function keys belong to the status page's own macro system. Nothing on a timer
    // should fire one, or a timer would start pressing keys at the player.
    [Fact]
    public void RefusesAKeyMacro() =>
        Send("加速術/KEY=F1", []).ShouldBe(DispatchResult.Skipped);

    // Not a packet at all — it puts the player's condition in the log, for someone working
    // out why a rule is not firing. It cannot fail the pass either, because the moment it
    // is most useful is the one where the numbers are not there.
    [Fact]
    public void PrintsThePlayersConditionWithoutSendingAnything()
    {
        Send("任何/INFO", []).ShouldBe(DispatchResult.Done);

        _actions.Used.ShouldBeEmpty();
        _actions.Casts.ShouldBeEmpty();
    }

    private DispatchResult Send(string text, IReadOnlyList<InventoryItem> bag) =>
        Send(HelperEntrySyntax.Parse(text), bag);

    private DispatchResult Send(HelperEntry entry, IReadOnlyList<InventoryItem> bag) =>
        new HelperDispatch(_actions, _spells, _entities, NullLogger<HelperDispatch>.Instance)
            .Send(_process, entry, bag);

    private static IReadOnlyList<InventoryItem> Bag(params string[] names) =>
        [.. names.Select((name, i) => Item(name, 0x1000u + ((uint)i * 0x10)))];

    private static InventoryItem Item(string name, uint id) =>
        new(new GameAddress(id), id, 0, 0, false, 1, name);

    private static InventoryItem Worn(string name, uint id) =>
        Item(name + ItemNames.InUseMark, id);

    private static InventoryItem Wielded(string name, uint id) =>
        Item(name + ItemNames.WieldedMark, id);

    /// <summary>Remembers what it was asked for rather than touching a client.</summary>
    private sealed class RecordingActions()
        : GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance)
    {
        private readonly List<uint> _used = [];
        private readonly List<uint> _sent = [];
        private readonly List<(uint Source, uint Target)> _pairs = [];
        private readonly List<(uint Packed, SkillAim Aim)> _casts = [];
        private readonly List<uint> _castTargets = [];

        public List<uint> Used => _used;

        public List<uint> Sent => _sent;

        public List<(uint Source, uint Target)> Pairs => _pairs;

        public List<(uint Packed, SkillAim Aim)> Casts => _casts;

        public List<uint> CastTargets => _castTargets;

        public override void UseItem(RemoteProcess process, GameAddress entry) => _used.Add(entry.Value);

        public override void SendUseItem(RemoteProcess process, uint itemId) => _sent.Add(itemId);

        public override void UseOn(RemoteProcess process, uint source, uint target) =>
            _pairs.Add((source, target));

        public override void Cast(
            RemoteProcess process, uint packed, SkillTarget target, GameAddress record = default)
        {
            _casts.Add((packed, target.Aim));
            _castTargets.Add(target.Id);
        }
    }

    /// <summary>A spell book with whatever the test says is in it.</summary>
    private sealed class StubSpells() : Spells(LegacyTextCodec.Auto, NullLogger<Spells>.Instance)
    {
        public Dictionary<string, uint> Known { get; } = [];

        public override uint? Find(RemoteProcess process, string name) =>
            Known.TryGetValue(name, out var packed) ? packed : null;
    }

    /// <summary>A world with whoever the test says is standing in it.</summary>
    private sealed class StubEntities()
        : EntityScan(LegacyTextCodec.Auto, NullLogger<EntityScan>.Instance)
    {
        public Dictionary<string, uint> Present { get; } = [];

        /// <summary>How many times the heap was walked.</summary>
        public int Searches { get; private set; }

        public override Entity? Find(RemoteProcess process, string name)
        {
            Searches++;

            return Present.TryGetValue(name, out var id)
                ? new Entity(new GameAddress(0xDEAD0000), id, name)
                : null;
        }
    }
}
