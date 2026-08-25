using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers the two keys the helper answers to inside the game.
/// </summary>
/// <remarks>
/// Insert starts and stops it; Home shows and hides its settings. The key state they read
/// belongs to the machine rather than to any window, so most of what is pinned here is
/// about the presses they must not act on: one made in somebody else's window, one that is
/// the same press acted on a tenth of a second ago, one that was already being held when
/// the game started, and one made before there is a character to act on.
/// </remarks>
public sealed class HelperKeyTaskTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly AuxSettings _settings = new();
    private readonly HelperSwitch _switch = new();
    private readonly HashSet<int> _down = [];

    private uint _foreground;
    private int _asked;

    public HelperKeyTaskTests() => _foreground = _process.Id;

    public void Dispose() => _process.Dispose();

    // ------------------------------------------------------------- Insert

    [Fact]
    public void StartsTheHelperOnInsert()
    {
        Press(Watching(), KeyboardState.Insert);

        _switch.IsOn.ShouldBeTrue();
    }

    [Fact]
    public void StopsItOnTheNextInsert()
    {
        var task = Watching();

        Press(task, KeyboardState.Insert);
        Press(task, KeyboardState.Insert);

        _switch.IsOn.ShouldBeFalse();
    }

    // A key held down is one press. Ten passes a second on a held key would start and stop
    // the helper until it was let go.
    [Fact]
    public void ActsOnlyOnceWhileInsertIsHeld()
    {
        var task = Watching();

        _down.Add(KeyboardState.Insert);
        task.Tick(Context());
        task.Tick(Context());
        task.Tick(Context());

        _switch.IsOn.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- Home

    [Fact]
    public void AsksForTheSettingsOnHome()
    {
        Press(Watching(), KeyboardState.Home);

        _asked.ShouldBe(1);
    }

    [Fact]
    public void AsksAgainOnTheNextHome()
    {
        var task = Watching();

        Press(task, KeyboardState.Home);
        Press(task, KeyboardState.Home);

        _asked.ShouldBe(2);
    }

    // Reaching for the settings before anything is running is a player who wants it
    // running. Making them find a second key to say so answers a question they have
    // already answered.
    [Fact]
    public void StartsTheHelperOnTheFirstHomeAsWell()
    {
        Press(Watching(), KeyboardState.Home);

        _switch.IsOn.ShouldBeTrue();
        _asked.ShouldBe(1);
    }

    // And only the first. After that the two keys are separate jobs, which is why there
    // are two of them: showing the settings must not stop the helper, and stopping it must
    // not take the window away.
    [Fact]
    public void LeavesTheSwitchAloneOnEveryHomeAfterTheFirst()
    {
        var task = Watching();

        Press(task, KeyboardState.Home);
        Press(task, KeyboardState.Home);
        Press(task, KeyboardState.Home);

        _switch.IsOn.ShouldBeTrue();
        _asked.ShouldBe(3);
    }

    // Including after the player has switched it off again: Home is not a way back on once
    // Insert has been used, or the two keys would fight over the same state.
    [Fact]
    public void DoesNotStartItAgainWithHomeAfterItHasBeenStopped()
    {
        var task = Watching();

        Press(task, KeyboardState.Home);
        Press(task, KeyboardState.Insert);

        _switch.IsOn.ShouldBeFalse();

        Press(task, KeyboardState.Home);

        _switch.IsOn.ShouldBeFalse();
        _asked.ShouldBe(2);
    }

    // A player who used Insert first has already said what they want, so their first Home
    // is only the window.
    [Fact]
    public void LeavesTheSwitchAloneWhenInsertCameFirst()
    {
        var task = Watching();

        Press(task, KeyboardState.Insert);
        Press(task, KeyboardState.Insert);

        _switch.IsOn.ShouldBeFalse();

        Press(task, KeyboardState.Home);

        _switch.IsOn.ShouldBeFalse();
    }

    [Fact]
    public void LeavesTheWindowAloneWhenTheHelperIsSwitched()
    {
        Press(Watching(), KeyboardState.Insert);

        _asked.ShouldBe(0);
    }

    // -------------------------------------------------- what neither acts on

    // Nothing the helper does means anything without a character: no gauges to read, no
    // bag to look in, no chat window to say it started in, and no settings worth showing.
    [Theory]
    [InlineData(KeyboardState.Insert)]
    [InlineData(KeyboardState.Home)]
    public void IgnoresBothBeforeTheCharacterIsInTheWorld(int key)
    {
        Press(Watching(), key, inWorld: false);

        _switch.IsOn.ShouldBeFalse();
        _asked.ShouldBe(0);
    }

    // The press happened, and there was nobody to act on. Holding a key through the
    // loading screen is not a press made in the world.
    [Fact]
    public void DoesNotActOnAPressHeldFromTheCharacterScreenIntoTheWorld()
    {
        var task = Watching();

        _down.Add(KeyboardState.Insert);
        task.Tick(Context(inWorld: false));
        task.Tick(Context(inWorld: true));

        _switch.IsOn.ShouldBeFalse();
    }

    // The key state is the machine's. Without this a player with two clients up would
    // start both helpers, and an Insert pressed in a browser would start one of them.
    [Theory]
    [InlineData(KeyboardState.Insert)]
    [InlineData(KeyboardState.Home)]
    public void IgnoresAPressWhileSomethingElseIsInFront(int key)
    {
        _foreground = _process.Id + 1;

        Press(Watching(), key);

        _switch.IsOn.ShouldBeFalse();
        _asked.ShouldBe(0);
    }

    [Fact]
    public void IgnoresAPressWhenNothingIsInFront()
    {
        _foreground = 0;

        Press(Watching(), KeyboardState.Insert);

        _switch.IsOn.ShouldBeFalse();
    }

    [Fact]
    public void DoesNotActOnAPressMadeElsewhereWhenTheGameComesForward()
    {
        var task = Watching();

        _foreground = _process.Id + 1;
        _down.Add(KeyboardState.Insert);
        task.Tick(Context());

        _foreground = _process.Id;
        task.Tick(Context());

        _switch.IsOn.ShouldBeFalse();
    }

    // A launcher started from a keyboard shortcut can find a key already down on its first
    // pass, which is not somebody asking for anything.
    [Theory]
    [InlineData(KeyboardState.Insert)]
    [InlineData(KeyboardState.Home)]
    public void DoesNotCountAKeyThatWasAlreadyDownWhenItStarted(int key)
    {
        _down.Add(key);

        Watching().Tick(Context());

        _switch.IsOn.ShouldBeFalse();
        _asked.ShouldBe(0);
    }

    // ------------------------------------------------------------- the rest

    [Fact]
    public void RunsOnEveryPassOfTheLoop() => Watching().Interval.ShouldBe(AuxHost.Cadence);

    // It is what turns the rest on, so it cannot be one of the things that is off.
    [Fact]
    public void RunsWhileTheHelperIsOff() => Watching().RunsWhileOff.ShouldBeTrue();

    [Fact]
    public void WatchesTheTwoKeysTheWindowsName()
    {
        KeyboardState.Insert.ShouldBe(0x2D);
        KeyboardState.Home.ShouldBe(0x24);
    }

    [Theory]
    [InlineData(0u, 100u, false)]
    [InlineData(100u, 100u, true)]
    [InlineData(101u, 100u, false)]
    public void KnowsWhetherTheWindowInFrontIsTheGame(uint foreground, uint game, bool ours) =>
        HelperKeyTask.Ours(foreground, game).ShouldBe(ours);

    // Zero means a window is being switched, which is the moment a stray press is least
    // likely to have been meant for the game — and a game whose process id was zero is not
    // a game at all.
    [Fact]
    public void TreatsNothingInFrontAsNotTheGameEvenIfTheGameIsNothing() =>
        HelperKeyTask.Ours(0, 0).ShouldBeFalse();

    private HelperKeyTask Watching()
    {
        var task = new HelperKeyTask(
            _switch, NullLogger<HelperKeyTask>.Instance, _down.Contains, () => _foreground);

        task.WindowRequested += (_, _) => _asked++;

        return task;
    }

    private void Press(HelperKeyTask task, int key, bool inWorld = true)
    {
        _down.Add(key);
        task.Tick(Context(inWorld));

        _down.Remove(key);
        task.Tick(Context(inWorld));
    }

    private StubContext Context(bool inWorld = true) => new(_process, _settings, inWorld);

    /// <summary>A pass with a chosen world, since the real one reads the client.</summary>
    private sealed class StubContext(RemoteProcess process, AuxSettings settings, bool inWorld)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        public override bool IsInWorld => inWorld;

        public override PlayerState Player => default;

        public override IReadOnlyList<InventoryItem> Bag => [];
    }
}
