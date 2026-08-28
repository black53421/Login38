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

    /// <summary>The client's own number for a transformed character.</summary>
    private const int Polymorph = BuffState.Transformed;

    /// <summary>
    /// And for the shield the player's report was about.
    /// </summary>
    /// <remarks>
    /// From the client's shield-icon handler, which turns the packet's icon type into an
    /// effect number and falls through to this one.
    /// </remarks>
    private const int Shield = 4;

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
        var sent = Sent((EntryKind.Item, Haste), unanswered: 1);

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
        var sent = Sent((EntryKind.Item, Haste), unanswered: 1);

        HelperTask.Due(Item("勇敢藥水", Bravery), Table(), sent, TimeSpan.FromSeconds(1), false)
            .ShouldBeTrue();
    }

    // ---- buffs the server will not grant -------------------------------------------------
    //
    // The cooldown paces a packet in flight. It does nothing about a buff the server refuses
    // outright, which the effect byte cannot tell apart from one that has not arrived yet:
    // the client sets that byte straight out of the packet — FUN_005355c0, the shield-icon
    // handler, has no condition on it at all — so a byte that stays clear only ever means
    // the packet never came. Asked for every five seconds, that is a character casting all
    // evening and getting nowhere.

    [Fact]
    public void KeepsTheOrdinaryCooldownWhileTheServerIsStillAnswering() =>
        HelperTask.Wait(HelperTask.Tolerated - 1).ShouldBe(HelperTask.Cooldown);

    [Fact]
    public void WaitsTwiceAsLongEachTimeAnEntryDoesNotArrive()
    {
        HelperTask.Wait(HelperTask.Tolerated).ShouldBe(HelperTask.Cooldown * 2);
        HelperTask.Wait(HelperTask.Tolerated + 1).ShouldBe(HelperTask.Cooldown * 4);
        HelperTask.Wait(HelperTask.Tolerated + 2).ShouldBe(HelperTask.Cooldown * 8);
    }

    // A minute, and it stays a minute however long the character stands there — including
    // past the point where the shift behind it would have wrapped.
    [Fact]
    public void StopsStretchingTheWaitAtAMinute()
    {
        HelperTask.Wait(HelperTask.Tolerated + 3).ShouldBe(TimeSpan.FromMinutes(1));
        HelperTask.Wait(HelperTask.Tolerated + 40).ShouldBe(TimeSpan.FromMinutes(1));
        HelperTask.Wait(int.MaxValue).ShouldBe(TimeSpan.FromMinutes(1));
    }

    // What the player reported: a shield the server will not grant to a transformed
    // character, asked for every five seconds for as long as the transformation lasted.
    [Fact]
    public void LeavesABuffTheServerRefusesAloneWhileTheTransformationLasts() =>
        HelperTask.Due(
            Skill("保護罩", Shield),
            Table(Polymorph),
            Sent((EntryKind.Skill, Shield), HelperTask.Tolerated, transformed: true),
            TimeSpan.FromHours(1),
            false).ShouldBeFalse();

    // And asks for it the moment the character is themselves again, without the player
    // having to switch anything off and on.
    [Fact]
    public void AsksForItAgainAsSoonAsTheTransformationEnds() =>
        HelperTask.Due(
            Skill("保護罩", Shield),
            Table(),
            Sent((EntryKind.Skill, Shield), HelperTask.Tolerated, transformed: true),
            TimeSpan.FromSeconds(11),
            false).ShouldBeTrue();

    // Being transformed now is not on its own a reason to stop: the entry has to be one
    // that has never arrived while transformed.
    [Fact]
    public void StillAsksForABuffThatFailedBeforeTheTransformationStarted() =>
        HelperTask.Due(
            Skill("保護罩", Shield),
            Table(Polymorph),
            Sent((EntryKind.Skill, Shield), HelperTask.Tolerated, transformed: false),
            TimeSpan.FromSeconds(11),
            false).ShouldBeTrue();

    // Two unanswered sends is a slow server, not a refusal.
    [Fact]
    public void DoesNotReadAnythingIntoOneOrTwoUnansweredSends() =>
        HelperTask.Due(
            Skill("保護罩", Shield),
            Table(Polymorph),
            Sent((EntryKind.Skill, Shield), HelperTask.Tolerated - 1, transformed: true),
            TimeSpan.FromSeconds(5),
            false).ShouldBeTrue();

    // An effect that arrives clears the record, so a buff that later runs out is replaced
    // on the pass that notices rather than a cooldown after it.
    [Fact]
    public void ForgetsWhatItLearnedOnceTheEffectIsOnTheCharacter()
    {
        var task = Task();
        var settings = Settings(Item("加速藥水", Haste));

        task.Tick(Context(settings));
        task.State = Table(Haste);
        task.Tick(Context(settings));
        task.State = Table();
        task.Tick(Context(settings));

        _dispatch.Sent.ShouldBe(["加速藥水", "加速藥水"]);
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

    private static Dictionary<(EntryKind, int), HelperTask.Attempt> None() => [];

    /// <summary>One entry that has been asked for and has not arrived.</summary>
    private static Dictionary<(EntryKind, int), HelperTask.Attempt> Sent(
        (EntryKind, int) key, int unanswered, bool transformed = false) =>
        new() { [key] = new HelperTask.Attempt(TimeSpan.Zero, unanswered, transformed) };

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
    /// <remarks>
    /// Settable, so that a test can put an effect on the character partway through and
    /// watch what the helper does with it on the next pass.
    /// </remarks>
    private sealed class StubTask(
        HelperDispatch dispatch, ILogger<HelperTask> logger, BuffState state)
        : HelperTask(dispatch, logger)
    {
        public BuffState State { get; set; } = state;

        internal override BuffState? Read(RemoteProcess process) => State;
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
