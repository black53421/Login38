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
/// Covers the green line that tells the player the helper is running.
/// </summary>
/// <remarks>
/// It goes through a routine inside the client, so when it is said matters as much as what
/// it says: too early and the call lands in a window the client has not built yet.
/// </remarks>
public sealed class AnnouncementTaskTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly AuxSettings _settings = new();
    private readonly RecordingChat _chat = new();
    private readonly HelperSwitch _switch = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void SaysSoOnceTheCharacterIsInTheWorld()
    {
        Task().Tick(Context(inWorld: true, Loaded));

        _chat.Said.ShouldBe([AnnouncementTask.Started]);
    }

    // Nothing has been started, so there is nothing to announce. The helper is off until
    // the player presses Home, and a line saying it is running would be a lie.
    [Fact]
    public void SaysNothingUntilThePlayerHasStartedTheHelper()
    {
        Idle().Tick(Context(inWorld: true, Loaded));

        _chat.Said.ShouldBeEmpty();
    }

    // A switch with feedback in one direction only is a switch nobody trusts: a player who
    // pressed Home twice by accident has to be told the second one landed.
    [Fact]
    public void SaysSoAgainWhenTheHelperIsStopped()
    {
        var task = Task();

        task.Tick(Context(inWorld: true, Loaded));
        _switch.Toggle();
        task.Tick(Context(inWorld: true, Loaded));

        _chat.Said.ShouldBe([AnnouncementTask.Started, AnnouncementTask.Stopped]);
    }

    [Fact]
    public void SaysStoppingInGreenToo()
    {
        AnnouncementTask.Stopped.ShouldStartWith(GameChat.GreenMark);
        AnnouncementTask.Stopped.ShouldEndWith("天堂喝水輔助關閉");
    }

    // It is what turns the rest on, so it cannot be one of the things that is off.
    [Fact]
    public void RunsWhileTheHelperIsOff() => Idle().RunsWhileOff.ShouldBeTrue();

    [Fact]
    public void SaysWhatThePlayerWasPromised() =>
        AnnouncementTask.Started.ShouldEndWith("天堂喝水輔助啟動");

    // Written into the text rather than passed as the colour argument, which is what the
    // client's own routine reads.
    [Fact]
    public void SaysItInGreen() => AnnouncementTask.Started.ShouldStartWith(GameChat.GreenMark);

    [Fact]
    public void SaysNothingBeforeThereIsACharacter()
    {
        Task().Tick(Context(inWorld: false, Loaded));

        _chat.Said.ShouldBeEmpty();
    }

    // The client says it is in the world before the character's own numbers are there, and
    // a line written into that gap calls a routine whose window does not exist yet.
    [Fact]
    public void WaitsForTheCharacterToFinishLoading()
    {
        var task = Task();

        task.Tick(Context(inWorld: true, Loading));

        _chat.Said.ShouldBeEmpty();

        task.Tick(Context(inWorld: true, Loaded));

        _chat.Said.ShouldBe([AnnouncementTask.Started]);
    }

    [Fact]
    public void SaysItOnlyOnceForOneCharacter()
    {
        var task = Task();

        task.Tick(Context(inWorld: true, Loaded));
        task.Tick(Context(inWorld: true, Loaded));
        task.Tick(Context(inWorld: true, Loaded));

        _chat.Said.Count.ShouldBe(1);
    }

    // Coming back with another character is a new session as far as the player is
    // concerned, and the reference restarted the whole helper at that point.
    [Fact]
    public void SaysItAgainForTheNextCharacter()
    {
        var task = Task();

        task.Tick(Context(inWorld: true, Loaded));
        task.Tick(Context(inWorld: false, Loading));
        task.Tick(Context(inWorld: true, Loaded));

        _chat.Said.ShouldBe([AnnouncementTask.Started, AnnouncementTask.Started]);
    }

    [Theory]
    [InlineData(0u, 0u, false)]
    [InlineData(100u, 0u, false)]
    [InlineData(0u, 100u, false)]
    [InlineData(100u, 100u, true)]
    public void KnowsWhenTheClientIsReadyToBeWrittenTo(uint hitPoints, uint magic, bool ready) =>
        AnnouncementTask.Ready(
            new PlayerState(new Gauge(1, hitPoints), new Gauge(1, magic), 0, 0, 0)).ShouldBe(ready);

    private static PlayerState Loaded =>
        new(new Gauge(500, 500), new Gauge(200, 200), 100, 10, 4);

    private static PlayerState Loading => new(default, default, 0, 0, 0);

    /// <summary>A task whose helper the player has already started, which is the usual case.</summary>
    private AnnouncementTask Task()
    {
        _switch.Toggle();

        return new AnnouncementTask(_chat, _switch, NullLogger<AnnouncementTask>.Instance);
    }

    /// <summary>And one whose helper is still off.</summary>
    private AnnouncementTask Idle() => new(_chat, _switch, NullLogger<AnnouncementTask>.Instance);

    private StubContext Context(bool inWorld, PlayerState player) =>
        new(_process, _settings, inWorld, player);

    /// <summary>A pass with a chosen world and a chosen character.</summary>
    private sealed class StubContext(
        RemoteProcess process, AuxSettings settings, bool inWorld, PlayerState player)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        public override bool IsInWorld => inWorld;

        public override PlayerState Player => player;

        public override IReadOnlyList<InventoryItem> Bag => [];
    }

    /// <summary>Remembers the lines rather than calling into a client.</summary>
    private sealed class RecordingChat()
        : GameChat(LegacyTextCodec.Auto, NullLogger<GameChat>.Instance)
    {
        public List<string> Said { get; } = [];

        public override void Write(RemoteProcess process, string line) => Said.Add(line);
    }
}
