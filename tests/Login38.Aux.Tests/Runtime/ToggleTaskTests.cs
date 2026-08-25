using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Aux.Toggles;
using Login38.Core.Text;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers the pass that keeps the game matching the switches in the helper window.
/// </summary>
public sealed class ToggleTaskTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly HelperSwitch _switch = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void AsksEveryToggleWhatItWants()
    {
        var first = new RecordingToggle("first", wanted: true);
        var second = new RecordingToggle("second", wanted: false);

        Running([first, second]).Tick(Context(new AuxSettings()));

        first.Applied.ShouldBe([true]);
        second.Applied.ShouldBe([false]);
    }

    // Some of these write data the client rewrites for its own reasons, so putting it back
    // every pass is the point — the task must not skip a toggle whose switch has not moved.
    [Fact]
    public void AppliesOnEveryPassAndNotOnlyOnChange()
    {
        var toggle = new RecordingToggle("steady", wanted: true);
        var task = Running([toggle]);
        var context = new AuxSettings();

        task.Tick(Context(context));
        task.Tick(Context(context));
        task.Tick(Context(context));

        toggle.Applied.ShouldBe([true, true, true]);
    }

    [Fact]
    public void ReadsWhatEachToggleWantsFromThisPassesSettings()
    {
        var toggle = new SettingsToggle();
        var task = Running([toggle]);

        task.Tick(Context(new AuxSettings { Misc = new MiscToggles { ShowClock = true } }));
        task.Tick(Context(new AuxSettings { Misc = new MiscToggles { ShowClock = false } }));

        toggle.Applied.ShouldBe([true, false]);
    }

    [Fact]
    public void DoesNothingWithNoToggles() =>
        Should.NotThrow(() => Running([]).Tick(Context(new AuxSettings())));

    // The window's switches should feel immediate without a toggle whose address has moved
    // being retried ten times a second for a whole session.
    [Fact]
    public void RunsTwiceASecond() =>
        Running([]).Interval.ShouldBe(TimeSpan.FromMilliseconds(500));

    // Every switch in the window is off until the player has started the helper, whatever
    // the boxes say — those are what it will do once it is running.
    [Fact]
    public void AsksForNothingUntilThePlayerHasStartedTheHelper()
    {
        var toggle = new RecordingToggle("keen", wanted: true);

        new ToggleTask([toggle], _switch).Tick(Context(new AuxSettings()));

        toggle.Applied.ShouldBe([false]);
    }

    // And keeps asking, because switching off is itself something to do: a toggle left
    // applied is the helper still acting on a game it was told to leave alone.
    [Fact]
    public void RunsWhileTheHelperIsOff() => Running([]).RunsWhileOff.ShouldBeTrue();

    [Fact]
    public void PutsEverythingBackWhenTheHelperIsStopped()
    {
        var toggle = new RecordingToggle("keen", wanted: true);
        var task = Running([toggle]);

        task.Tick(Context(new AuxSettings()));
        _switch.Toggle();
        task.Tick(Context(new AuxSettings()));

        toggle.Applied.ShouldBe([true, false]);
    }

    /// <summary>A task whose helper the player has started.</summary>
    private ToggleTask Running(IGameToggle[] toggles)
    {
        if (!_switch.IsOn)
        {
            _switch.Toggle();
        }

        return new ToggleTask(toggles, _switch);
    }

    private AuxContext Context(AuxSettings settings) =>
        new(_process, settings, LegacyTextCodec.Auto);

    /// <summary>A toggle that writes nothing and remembers what it was asked for.</summary>
    private sealed class RecordingToggle : IGameToggle
    {
        private readonly bool _wanted;
        private readonly List<bool> _applied = [];

        public RecordingToggle(string name, bool wanted)
        {
            Name = name;
            _wanted = wanted;
        }

        public string Name { get; }

        public IReadOnlyList<bool> Applied => _applied;

        public bool WantedBy(AuxSettings settings) => _wanted;

        public bool Apply(RemoteProcess process, bool wanted)
        {
            _applied.Add(wanted);
            return true;
        }
    }

    /// <summary>A toggle that follows a real setting, to check which copy is read.</summary>
    private sealed class SettingsToggle : IGameToggle
    {
        private readonly List<bool> _applied = [];

        public string Name => "settings";

        public IReadOnlyList<bool> Applied => _applied;

        public bool WantedBy(AuxSettings settings) => settings.Misc.ShowClock;

        public bool Apply(RemoteProcess process, bool wanted)
        {
            _applied.Add(wanted);
            return true;
        }
    }
}
