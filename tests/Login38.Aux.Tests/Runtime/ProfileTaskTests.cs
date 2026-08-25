using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers when a character's settings are read and written.
/// </summary>
/// <remarks>
/// Which character is playing is not known at launch — the player picks one several screens
/// in and can come back out and pick another. So the interesting cases are all about the
/// moment the name changes, in either direction.
/// </remarks>
public sealed class ProfileTaskTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "login38-profile-task-" + Guid.NewGuid().ToString("N"));

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly AuxSettingsSource _settings = new();
    private readonly ProfileStore _store;
    private readonly ProfileTask _task;

    public ProfileTaskTests()
    {
        _store = new ProfileStore(NullLogger<ProfileStore>.Instance, _directory);
        _task = new ProfileTask(_store, _settings, NullLogger<ProfileTask>.Instance);
    }

    public void Dispose()
    {
        _process.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void LoadsWhenACharacterEntersTheWorld()
    {
        _store.Save("騎士", new AuxSettings { ShoutIntervalSeconds = 42 });

        _task.Tick(Context("騎士"));

        _task.Character.ShouldBe("騎士");
        _settings.Current.ShoutIntervalSeconds.ShouldBe(42u);
    }

    [Fact]
    public void StartsFromTheDefaultsForACharacterWithNoFile()
    {
        _task.Tick(Context("新角色"));

        _task.Character.ShouldBe("新角色");
        _settings.Current.Profile.ShouldBe("新角色");
    }

    // Nothing has changed on most passes, and reloading would throw away whatever the
    // player has just typed into the window.
    [Fact]
    public void DoesNothingWhileTheSameCharacterIsPlaying()
    {
        _task.Tick(Context("騎士"));

        _settings.Publish(new AuxSettings { ShoutIntervalSeconds = 99 });
        _task.Tick(Context("騎士"));
        _task.Tick(Context("騎士"));

        _settings.Current.ShoutIntervalSeconds.ShouldBe(99u);
    }

    // Leaving the world is the only reliable moment to save: the launcher can be closed at
    // any time and the game can exit on its own, and both leave the character screen first.
    [Fact]
    public void SavesWhenTheCharacterLeavesTheWorld()
    {
        _task.Tick(Context("騎士"));
        _settings.Publish(new AuxSettings { ShoutIntervalSeconds = 7 });

        _task.Tick(Context(null));

        _task.Character.ShouldBeNull();
        _store.Load("騎士").ShoutIntervalSeconds.ShouldBe(7u);
    }

    // A player can swap characters without restarting the game. The settings in memory
    // belong to whoever was playing a moment ago.
    [Fact]
    public void SavesOneCharacterBeforeLoadingTheNext()
    {
        _store.Save("法師", new AuxSettings { ShoutIntervalSeconds = 20 });

        _task.Tick(Context("騎士"));
        _settings.Publish(new AuxSettings { ShoutIntervalSeconds = 10 });

        _task.Tick(Context("法師"));

        _store.Load("騎士").ShoutIntervalSeconds.ShouldBe(10u);
        _settings.Current.ShoutIntervalSeconds.ShouldBe(20u);
    }

    // Quitting straight from the world would otherwise lose the session: the loop stops
    // when the game exits, which is after the character has already gone.
    [Fact]
    public void SavesOnTheWayOut()
    {
        _task.Tick(Context("騎士"));
        _settings.Publish(new AuxSettings { ShoutIntervalSeconds = 5 });

        _task.Stopping();

        _store.Load("騎士").ShoutIntervalSeconds.ShouldBe(5u);
    }

    [Fact]
    public void HasNothingToSaveBeforeAnyoneHasPlayed() => Should.NotThrow(_task.Stopping);

    [Fact]
    public void DoesNothingWhileNobodyIsPlaying()
    {
        _task.Tick(Context(null));
        _task.Tick(Context(null));

        _task.Character.ShouldBeNull();
        Directory.Exists(_directory).ShouldBeFalse();
    }

    private StubContext Context(string? character) =>
        new(_process, _settings.Current, character);

    /// <summary>A pass in which a chosen character is, or is not, in the world.</summary>
    private sealed class StubContext : AuxContext
    {
        private readonly string? _character;

        public StubContext(RemoteProcess process, AuxSettings settings, string? character)
            : base(process, settings, LegacyTextCodec.Auto) => _character = character;

        public override string? Character => _character;
    }
}
