using Login38.Aux.Actions;
using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers keeping the player's buffs up.
/// </summary>
/// <remarks>
/// The rule that makes this work rather than spin is that an entry names an effect number
/// as well as a command, so the helper can ask the client whether the effect is already on
/// the character. Get that wrong and it either never buffs or never stops.
/// </remarks>
public sealed class HelperTaskTests : IDisposable
{
    private const int Haste = 37;
    private const int Bravery = 2;
    private const byte Prince = 0;

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingDispatch _dispatch = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void SendsAnEntryWhoseEffectIsNotOnTheCharacter() =>
        HelperTask.Due(Item("加速藥水", Haste), Table(), None(), TimeSpan.Zero, false).ShouldBeTrue();

    // The whole point. An effect already up is not sent again, however long the list is.
    [Fact]
    public void LeavesAnEffectThatIsAlreadyUpAlone() =>
        HelperTask.Due(Item("加速藥水", Haste), Table(Haste), None(), TimeSpan.Zero, false)
            .ShouldBeFalse();

    // An entry with no effect number could never be told to have worked, so it would go
    // every cooldown for ever. That is what a timer row is for, and the player has those.
    [Fact]
    public void PassesOverAnEntryWithNoEffectNumber() =>
        HelperTask.Due(Item("麵包"), Table(), None(), TimeSpan.Zero, false).ShouldBeFalse();

    [Fact]
    public void PassesOverAnEffectNumberOutsideTheTable() =>
        HelperTask.Due(Item("怪東西", 9999), Table(), None(), TimeSpan.Zero, false).ShouldBeFalse();

    // Long enough for the packet to reach the server and the answer to set the effect byte.
    // Without it everything on the list goes again while the first is still in flight.
    [Fact]
    public void WaitsForTheServerToAnswerBeforeSendingAgain()
    {
        var sent = new Dictionary<(EntryKind, int), TimeSpan> { [(EntryKind.Item, Haste)] = TimeSpan.Zero };

        HelperTask.Due(Item("加速藥水", Haste), Table(), sent, TimeSpan.FromSeconds(4), false)
            .ShouldBeFalse();
        HelperTask.Due(Item("加速藥水", Haste), Table(), sent, TimeSpan.FromSeconds(5), false)
            .ShouldBeTrue();
    }

    // The client casts one skill at a time and answers the rest with an error — and an
    // error means the effect byte is never set, so the helper would try again at once.
    [Fact]
    public void SendsOnlyOneSkillAPass() =>
        HelperTask.Due(Skill("加速術", Haste), Table(), None(), TimeSpan.Zero, castSomething: true)
            .ShouldBeFalse();

    // Items are not skills: using one is instant and does not stop the next.
    [Fact]
    public void StillUsesItemsAfterASkillHasGone() =>
        HelperTask.Due(Item("加速藥水", Haste), Table(), None(), TimeSpan.Zero, castSomething: true)
            .ShouldBeTrue();

    // The cooldowns are per entry, so one buff going out does not hold up another.
    [Fact]
    public void HoldsUpOnlyTheEntryThatWasSent()
    {
        var sent = new Dictionary<(EntryKind, int), TimeSpan> { [(EntryKind.Item, Haste)] = TimeSpan.Zero };

        HelperTask.Due(Item("勇敢藥水", Bravery), Table(), sent, TimeSpan.FromSeconds(1), false)
            .ShouldBeTrue();
    }

    [Fact]
    public void DoesNothingWhileTheMasterSwitchIsOff()
    {
        var settings = Settings(Item("加速藥水", Haste));
        settings.HelperEnabled = false;

        Task().Tick(Context(settings));

        _dispatch.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingWithAnEmptyList()
    {
        Task().Tick(Context(Settings()));

        _dispatch.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void DoesNothingBeforeTheCharacterIsInTheWorld()
    {
        Task().Tick(Context(Settings(Item("加速藥水", Haste)), inWorld: false));

        _dispatch.Sent.ShouldBeEmpty();
    }

    // A whole pass, against a table the test lays out.
    [Fact]
    public void SendsAnEntryOnAWholePass()
    {
        Task().Tick(Context(Settings(Item("加速藥水", Haste))));

        _dispatch.Sent.ShouldBe(["加速藥水"]);
    }

    [Fact]
    public void LeavesAnEffectThatIsUpAloneOnAWholePass()
    {
        Task(Haste).Tick(Context(Settings(Item("加速藥水", Haste))));

        _dispatch.Sent.ShouldBeEmpty();
    }

    // Once, not twice: the cooldown holds it while the packet is in flight and the effect
    // byte has not been set yet.
    [Fact]
    public void SendsItOnceWhileTheServerIsStillAnswering()
    {
        var task = Task();
        var settings = Settings(Item("加速藥水", Haste));

        task.Tick(Context(settings));
        task.Tick(Context(settings));

        _dispatch.Sent.Count.ShouldBe(1);
    }

    // Several items in one pass, because using one is instant and does not stop the next.
    [Fact]
    public void UsesEverythingOnTheListThatIsNotUp()
    {
        Task().Tick(Context(Settings(Item("加速藥水", Haste), Item("勇敢藥水", Bravery))));

        _dispatch.Sent.ShouldBe(["加速藥水", "勇敢藥水"]);
    }

    // But only one skill, because the client casts one at a time.
    [Fact]
    public void CastsOneSkillEvenWhenTwoAreDue()
    {
        _dispatch.Result = DispatchResult.Cast;

        Task().Tick(Context(Settings(Skill("加速術", Haste), Skill("勇敢術", Bravery))));

        _dispatch.Sent.ShouldBe(["加速術"]);
    }

    // A cooldown is for a packet in flight. An entry that sent nothing has nothing in
    // flight, so it is tried again on the next pass rather than five seconds later.
    //
    // The reference's own defect, kept for a while and then found from inside the game: it
    // put an entry on its full cooldown when the dispatch was skipped, with a comment
    // saying this was to stop the log filling up. The usual reason for a skip is "not yet"
    // — an item still being read into the bag, a spell table that has not settled — so the
    // player switched the helper on and watched nothing happen for five seconds.
    [Fact]
    public void TriesAgainOnTheNextPassWhenNothingWasSent()
    {
        _dispatch.Result = DispatchResult.Skipped;

        var task = Task();
        var settings = Settings(Item("加速藥水", Haste));

        task.Tick(Context(settings));
        task.Tick(Context(settings));
        task.Tick(Context(settings));

        _dispatch.Sent.Count.ShouldBe(3);
    }

    // And waits, once something is in flight.
    [Fact]
    public void WaitsOutTheCooldownAfterSomethingWasSent()
    {
        var task = Task();
        var settings = Settings(Item("加速藥水", Haste));

        task.Tick(Context(settings));
        task.Tick(Context(settings));
        task.Tick(Context(settings));

        _dispatch.Sent.Count.ShouldBe(1);
    }

    private StubTask Task(params int[] up) =>
        new(_dispatch, NullLogger<HelperTask>.Instance, Table(up));

    private StubContext Context(AuxSettings settings, bool inWorld = true) =>
        new(_process, settings, inWorld);

    private static AuxSettings Settings(params HelperEntry[] entries) =>
        new() { HelperEnabled = true, HelperEntries = [.. entries] };

    private static HelperEntry Item(string name, int stateId = HelperEntry.NoState) =>
        HelperEntry.ForItem(name, stateId);

    private static HelperEntry Skill(string name, int stateId) =>
        new(stateId, name, EntryKind.Skill, CastTarget.Any(CastKind.OnSelf));

    private static Dictionary<(EntryKind, int), TimeSpan> None() => [];

    private static BuffState Table(params int[] up)
    {
        var bytes = new byte[BuffState.Half * 2];

        foreach (var effect in up)
        {
            bytes[effect] = 1;
        }

        return BuffState.From(bytes, Prince);
    }

    /// <summary>A task reading a table the test laid out rather than the game's.</summary>
    private sealed class StubTask(
        HelperDispatch dispatch, ILogger<HelperTask> logger, BuffState state)
        : HelperTask(dispatch, logger)
    {
        internal override BuffState? Read(RemoteProcess process) => state;
    }

    /// <summary>A pass with nothing in the bag and a chosen world.</summary>
    private sealed class StubContext(RemoteProcess process, AuxSettings settings, bool inWorld)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        public override bool IsInWorld => inWorld;

        public override IReadOnlyList<InventoryItem> Bag => [];
    }

    /// <summary>Remembers what it was handed rather than touching a client.</summary>
    private sealed class RecordingDispatch()
        : HelperDispatch(
            new GameActions(LegacyTextCodec.Auto, NullLogger<GameActions>.Instance),
            new Spells(LegacyTextCodec.Auto, NullLogger<Spells>.Instance),
            new EntityScan(LegacyTextCodec.Auto, NullLogger<EntityScan>.Instance),
            NullLogger<HelperDispatch>.Instance)
    {
        private readonly List<string> _sent = [];

        public DispatchResult Result { get; set; } = DispatchResult.Done;

        public List<string> Sent => _sent;

        public override DispatchResult Send(
            RemoteProcess process, HelperEntry entry, IReadOnlyList<InventoryItem> bag)
        {
            _sent.Add(entry.Name);

            return Result;
        }
    }
}
