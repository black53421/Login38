using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers clearing junk out of the bag.
/// </summary>
/// <remarks>
/// The one feature here that destroys things. A rule that fires when it should not does not
/// waste a potion, it loses something the player spent a week getting, so the guards are
/// pinned rather than left to read correctly.
/// </remarks>
public sealed class DeleteTaskTests : IDisposable
{
    private const string Junk = "破爛的皮";
    private const string Bone = "骨頭";
    private const string Sword = "銀劍";
    private const string Solvent = "溶解劑";

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingActions _actions = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void DestroysWhatIsOnTheList()
    {
        Run(Settings([Junk], []), Bag(Junk));

        _actions.Destroyed.ShouldBe([Junk]);
    }

    [Fact]
    public void LeavesEverythingElseAlone()
    {
        Run(Settings([Junk], []), Bag(Bone));

        _actions.Destroyed.ShouldBeEmpty();
    }

    // The guard that matters. An item being worn or swung is never a candidate, whatever
    // the list says — a player who writes 銀劍 while wielding one means the spare.
    [Fact]
    public void NeverTouchesWhatIsBeingWorn()
    {
        Run(Settings([Sword], []), [Wielded(Sword)]);

        _actions.Destroyed.ShouldBeEmpty();
    }

    [Fact]
    public void StillDestroysTheSpareWhileOneIsBeingSwung()
    {
        Run(Settings([Sword], []), [Wielded(Sword), Item(Sword, 0x300)]);

        _actions.DestroyedIds.ShouldBe([0x300u]);
    }

    // The client shows a stack as "骨頭 (12)" and a player writes the name. Neither side
    // should have to know what the other does about counts.
    [Fact]
    public void MatchesWhateverTheStackCountSays()
    {
        Run(Settings([Bone], []), [Item($"{Bone} (12)", 0x400)]);

        _actions.DestroyedIds.ShouldBe([0x400u]);
    }

    // In full, not as a prefix. "骨" must not take the whole bag with it.
    [Fact]
    public void WillNotMatchPartOfAName()
    {
        Run(Settings(["骨"], []), Bag(Bone));

        _actions.Destroyed.ShouldBeEmpty();
    }

    // The whole stack. The server reads a count of zero as "throw away none of them", so a
    // stack destroyed one at a time never goes away.
    [Fact]
    public void DestroysTheWholeStack()
    {
        Run(Settings([Bone], []), [Item(Bone, 0x400, count: 12)]);

        _actions.Counts.ShouldBe([12u]);
    }

    [Fact]
    public void DissolvesWhatIsOnTheOtherList()
    {
        Run(Settings([], [Junk]), [Item(Junk, 0x100), Item(Solvent, 0x200)]);

        _actions.Dissolved.ShouldBe([(0x200u, 0x100u)]);
    }

    // Nothing is destroyed instead. A player who asked for the value back would rather wait
    // than have the thing thrown away.
    [Fact]
    public void WaitsForASolventRatherThanDestroyingIt()
    {
        Run(Settings([], [Junk]), Bag(Junk));

        _actions.Destroyed.ShouldBeEmpty();
        _actions.Dissolved.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("溶解劑")]
    [InlineData("溶解剂")]
    [InlineData("溶解劑 (3)")]
    public void RecognisesASolventHoweverItIsWritten(string name)
    {
        Run(Settings([], [Junk]), [Item(Junk, 0x100), Item(name, 0x200)]);

        _actions.Dissolved.ShouldBe([(0x200u, 0x100u)]);
    }

    // Destroying is looked for first, so a name on both lists takes the action that cannot
    // fail for want of a solvent.
    [Fact]
    public void DestroysANameThatIsOnBothLists()
    {
        Run(Settings([Junk], [Junk]), [Item(Junk, 0x100), Item(Solvent, 0x200)]);

        _actions.Destroyed.ShouldBe([Junk]);
        _actions.Dissolved.ShouldBeEmpty();
    }

    // The player's own order, so the thing they care least about goes first.
    [Fact]
    public void FollowsTheOrderTheListIsIn()
    {
        Run(Settings([Bone, Junk], []), Bag(Junk, Bone));

        _actions.Destroyed.ShouldBe([Bone]);
    }

    [Fact]
    public void DoesNothingWhileTheSwitchIsOff()
    {
        var settings = Settings([Junk], []);
        settings.DeleteEnabled = false;

        Run(settings, Bag(Junk));

        _actions.Destroyed.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingBeforeTheCharacterIsInTheWorld()
    {
        Run(Settings([Junk], []), Bag(Junk), inWorld: false);

        _actions.Destroyed.ShouldBeEmpty();
    }

    // Nothing on either list means the bag is never even read.
    [Fact]
    public void ReadsNothingAtAllWithBothListsEmpty()
    {
        var context = new StubContext(_process, Settings([], []), Bag(Junk), true);

        Task().Tick(context);

        context.Reads.ShouldBe(0);
    }

    [Fact]
    public void HasNothingToPickFromAnEmptyBag() =>
        DeleteTask.Pick([Junk], [], []).ShouldBeNull();

    [Fact]
    public void PassesOverABlankLineInTheList() =>
        DeleteTask.Pick(["  ", Junk], [], Bag(Junk))?.Item.Name.ShouldBe(Junk);

    private DeleteTask Task() => new(_actions, NullLogger<DeleteTask>.Instance);

    private void Run(AuxSettings settings, IReadOnlyList<InventoryItem> bag, bool inWorld = true)
    {
        _actions.Known = bag;

        Task().Tick(new StubContext(_process, settings, bag, inWorld));
    }

    private static AuxSettings Settings(string[] destroy, string[] dissolve) =>
        new()
        {
            DeleteEnabled = true,
            DeleteList = [.. destroy],
            DissolveList = [.. dissolve],
        };

    private static IReadOnlyList<InventoryItem> Bag(params string[] names) =>
        [.. names.Select((name, i) => Item(name, 0x1000u + ((uint)i * 0x10)))];

    private static InventoryItem Item(string name, uint id, uint count = 1) =>
        new(new GameAddress(id), id, 0, 0, false, count, name);

    private static InventoryItem Wielded(string name) =>
        Item(name + ItemNames.WieldedMark, 0x200);

    /// <summary>A pass with a chosen bag and world.</summary>
    private sealed class StubContext(
        RemoteProcess process, AuxSettings settings, IReadOnlyList<InventoryItem> bag, bool inWorld)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        /// <summary>How many times the game was asked anything.</summary>
        public int Reads { get; private set; }

        public override bool IsInWorld
        {
            get
            {
                Reads++;
                return inWorld;
            }
        }

        public override IReadOnlyList<InventoryItem> Bag
        {
            get
            {
                Reads++;
                return bag;
            }
        }
    }

    /// <summary>Remembers what it was asked for rather than touching a client.</summary>
    private sealed class RecordingActions()
        : GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance)
    {
        private readonly List<(uint Id, uint Count)> _destroyed = [];
        private readonly List<(uint Source, uint Target)> _dissolved = [];

        public IReadOnlyList<uint> DestroyedIds => [.. _destroyed.Select(d => d.Id)];

        public IReadOnlyList<uint> Counts => [.. _destroyed.Select(d => d.Count)];

        public List<(uint Source, uint Target)> Dissolved => _dissolved;

        /// <summary>The bag the test laid out, so ids can be reported as names.</summary>
        public IReadOnlyList<InventoryItem> Known { get; set; } = [];

        /// <summary>What was destroyed, back in the names the test wrote.</summary>
        public IReadOnlyList<string> Destroyed =>
            [.. _destroyed.Select(d => Known.First(i => i.Param == d.Id).Name)];

        public override void Drop(RemoteProcess process, uint itemId, uint count) =>
            _destroyed.Add((itemId, count));

        public override void UseOn(RemoteProcess process, uint source, uint target) =>
            _dissolved.Add((source, target));
    }
}
