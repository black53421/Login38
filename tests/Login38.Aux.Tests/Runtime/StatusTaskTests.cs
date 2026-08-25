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
/// Covers the four small things the status page does.
/// </summary>
/// <remarks>
/// Each is a condition and an action, and each keeps its own interval — a second for the
/// two that want catching up with, five for the two the server answers with an animation.
/// </remarks>
public sealed class StatusTaskTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RecordingActions _actions = new();

    public void Dispose() => _process.Dispose();

    // Never having happened counts as due, so switching a feature on does the thing rather
    // than waiting out its first interval in silence.
    [Fact]
    public void LetsSomethingHappenTheFirstTime() =>
        StatusTask.Due(null, TimeSpan.FromSeconds(5), TimeSpan.Zero).ShouldBeTrue();

    [Fact]
    public void HoldsItBackUntilTheIntervalHasPassed()
    {
        StatusTask.Due(TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4)).ShouldBeFalse();
        StatusTask.Due(TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)).ShouldBeTrue();
    }

    // The settings window writes 名字_option_編號 and the packet wants the middle part.
    [Theory]
    [InlineData("狼人_werewolf_2354", "werewolf")]
    [InlineData("死靈_death 80_1102", "death 80")]
    public void PullsTheFormOutOfWhatTheWindowWrote(string line, string form) =>
        StatusTask.Option(line).ShouldBe(form);

    // A player who typed the form in by hand gets what they typed, which is why the number
    // on the end has to be a number for the line to be read the long way.
    [Theory]
    [InlineData("re werewolf", "re werewolf")]
    [InlineData("狼人_werewolf_不是數字", "狼人_werewolf_不是數字")]
    [InlineData("狼人_werewolf", "狼人_werewolf")]
    public void LeavesAnythingElseAsTyped(string line, string form) =>
        StatusTask.Option(line).ShouldBe(form);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ReadsNothingAsAnOrdinaryPotion(string? line) =>
        StatusTask.Option(line).ShouldBeEmpty();

    // With all four switched off the game is never even asked anything.
    [Fact]
    public void ReadsNothingAtAllWithEverythingOff()
    {
        var context = new StubContext(_process, new AuxSettings(), [], true);

        Task().Tick(context);

        context.Reads.ShouldBe(0);
    }

    [Fact]
    public void DoesNothingBeforeTheCharacterIsInTheWorld()
    {
        var settings = new AuxSettings { EatWhenHungry = true, KeepWeaponSharp = true };

        Task().Tick(new StubContext(_process, settings, [], inWorld: false));

        _actions.Used.ShouldBeEmpty();
        _actions.Pairs.ShouldBeEmpty();
    }

    // A pass over a client that is not there does nothing, and does it without throwing.
    //
    // Against a process that has gone, rather than against this one. This test used to run
    // over the test host and assert that nothing happened, on the assumption that the
    // game's fixed addresses could not be read here — and most of the time they cannot.
    // Sometimes the runtime has something mapped at one of them, the read succeeds, the
    // bytes read as a hungry character, and the task eats. It failed perhaps one run in
    // eight and passed every time it was re-run on its own, which is what a test reading
    // arbitrary memory looks like from the outside.
    [Fact]
    public void DoesNothingRatherThanFailingAgainstAClientThatIsNotThere()
    {
        using var gone = Gone();

        var settings = new AuxSettings
        {
            EatWhenHungry = true,
            KeepWeaponSharp = true,
            AntidoteEnabled = true,
            AntidoteItem = "解毒藥水",
            TransformEnabled = true,
            TransformItem = "狼人變身藥水",
        };

        Should.NotThrow(() => Task().Tick(new StubContext(gone, settings, Bag(), true)));

        _actions.Used.ShouldBeEmpty();
        _actions.Pairs.ShouldBeEmpty();
    }

    /// <summary>A handle to a process that has exited, so every read fails and stays failed.</summary>
    /// <remarks>
    /// The handle outlives the process: Windows keeps the object alive while anybody holds
    /// one, so it can still be opened and asked, and every answer is no. That is exactly
    /// the case being covered — a helper still ticking over a client that has gone.
    /// </remarks>
    private static RemoteProcess Gone()
    {
        using var child = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            })!;

        // Opened while it is certainly still a process, then waited out.
        var opened = RemoteProcess.Open((uint)child.Id);

        child.WaitForExit();

        return opened;
    }

    // A weapon that is not being swung is not sharpened, whatever is in the bag.
    [Fact]
    public void LeavesAWeaponAloneWhileItIsNotBeingSwung()
    {
        var settings = new AuxSettings { KeepWeaponSharp = true };

        Task().Tick(new StubContext(_process, settings, Bag(), true));

        _actions.Pairs.ShouldBeEmpty();
    }

    private StatusTask Task() =>
        new(_actions, new StubSpells(), LegacyTextCodec.Auto, NullLogger<StatusTask>.Instance);

    private static IReadOnlyList<InventoryItem> Bag() =>
    [
        new(new GameAddress(0x1000), 0x1000, 0, 0, false, 1, StatusTask.Whetstone),
        new(new GameAddress(0x1010), 0x1010, 0, 0, false, 1, StatusTask.Meat),
        new(new GameAddress(0x1020), 0x1020, 0, 0, false, 1, "解毒藥水"),
    ];

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
        private readonly List<GameAddress> _used = [];
        private readonly List<(uint Source, uint Target)> _pairs = [];

        public List<GameAddress> Used => _used;

        public List<(uint Source, uint Target)> Pairs => _pairs;

        public override void UseItem(RemoteProcess process, GameAddress entry) => _used.Add(entry);

        public override void UseOn(RemoteProcess process, uint source, uint target) =>
            _pairs.Add((source, target));
    }

    /// <summary>An empty spell book.</summary>
    private sealed class StubSpells() : Spells(LegacyTextCodec.Auto, NullLogger<Spells>.Instance)
    {
        public override uint? Find(RemoteProcess process, string name) => null;
    }
}
