using System.Collections.Concurrent;
using System.IO;
using Login38.App.Services;
using Login38.App.ViewModels;
using Login38.Core.Configuration;
using Login38.Core.Servers;
using Login38.Core.Text;
using Login38.TestSupport;
using Login38.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.App.Tests.ViewModels;

/// <summary>
/// Exercises the launcher window's behaviour without a window.
/// </summary>
/// <remarks>
/// Against the real encrypted list that ships to players, so this also proves the
/// decrypt-and-decode path the operator's file actually needs rather than one built for
/// the test.
/// </remarks>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _preferences = Path.Combine(Path.GetTempPath(), $"l38-{Guid.NewGuid():N}.ini");

    private static string PackageDirectory => Login38.Tests.ShippingPackage.Directory;

    /// <summary>Answers without touching the network.</summary>
    private sealed class StubProbe(bool reachable) : IServerProbe
    {
        public int Calls { get; private set; }

        public Task<bool> IsReachableAsync(ServerInfo server, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(reachable);
        }
    }

    private MainViewModel Build(IServerProbe probe, string? directory = null)
    {
        var catalog = new ServerCatalog(
            new LegacyTextCodec(TextEncodingMode.Big5),
            NullLogger<ServerCatalog>.Instance,
            directory ?? PackageDirectory);

        var launcher = new GameLaunchService(
            new Login38.Patching.PatchPipeline([], NullLogger<Login38.Patching.PatchPipeline>.Instance),
            new LineageConfigFile(NullLogger<LineageConfigFile>.Instance),
            new ServiceCollection().AddLogging().AddAuxFeatures().BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            NullLoggerFactory.Instance);

        return new MainViewModel(catalog, launcher, probe, NullLogger<MainViewModel>.Instance, _preferences);
    }

    [ShippingPackageFact]
    public async Task OffersTheServersFromTheOperatorsList()
    {
        var model = Build(new StubProbe(true));

        await model.LoadAsync();

        model.Error.ShouldBeNull();
        model.Servers.ShouldNotBeEmpty();
    }

    // The list has a fixed number of slots and the unused ones are still present, empty.
    // Offering them gives the player rows that cannot work.
    [ShippingPackageFact]
    public async Task LeavesOutSlotsTheOperatorIsNotUsing()
    {
        var model = Build(new StubProbe(true));

        await model.LoadAsync();

        model.Servers.ShouldAllBe(s => s.IsOffered);
        model.Servers.Count.ShouldBeLessThanOrEqualTo(ListFile.MaxServers);
    }

    [ShippingPackageFact]
    public async Task SelectsSomethingSoThePlayerCanPressPlay()
    {
        var model = Build(new StubProbe(true));

        await model.LoadAsync();

        model.SelectedServer.ShouldNotBeNull();
        model.CanLaunch.ShouldBeTrue();
    }

    [ShippingPackageFact]
    public async Task MarksServersThatAnswered()
    {
        var probe = new StubProbe(true);
        var model = Build(probe);

        await model.LoadAsync();

        probe.Calls.ShouldBe(model.Servers.Count);
        model.Servers.ShouldAllBe(s => s.Availability == ServerAvailability.Online);
    }

    [ShippingPackageFact]
    public async Task MarksServersThatDidNot()
    {
        var model = Build(new StubProbe(false));

        await model.LoadAsync();

        model.Servers.ShouldAllBe(s => s.Availability == ServerAvailability.Offline);
    }

    // A missing list is the operator's packaging gone wrong. Saying so beats an empty
    // window that looks like every server is down.
    [Fact]
    public async Task ReportsAMissingListRatherThanShowingNothing()
    {
        var model = Build(new StubProbe(true), Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}"));

        await model.LoadAsync();

        model.Error.ShouldNotBeNullOrWhiteSpace();
        model.Servers.ShouldBeEmpty();
        model.CanLaunch.ShouldBeFalse();
    }

    [Fact]
    public void RemembersDisplayChoicesAcrossRuns()
    {
        var first = Build(new StubProbe(true));
        first.Windowed = false;
        first.WindowMode = WindowMode.Size1200x900;

        var second = Build(new StubProbe(true));

        second.Windowed.ShouldBeFalse();
        second.WindowMode.ShouldBe(WindowMode.Size1200x900);
    }

    // These come out of a file the operator distributes. Handing an arbitrary string to
    // the shell is how a server list becomes a way to run something.
    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void RefusesToOpenAnythingThatIsNotAWebPage(string? url)
    {
        var model = Build(new StubProbe(true));

        // Nothing to assert but that it returns: a scheme that got through would start a
        // process, which is exactly what must not happen.
        Should.NotThrow(() => model.OpenLink(url));
    }

    public void Dispose() => File.Delete(_preferences);

    // The helper's settings are not offered from the launcher at all: they belong to the
    // character playing, and the way in is Home once that character is in the world.
    [Fact]
    public void DoesNotOfferTheHelperSettingsFromTheLauncher()
    {
        var offered = typeof(MainViewModel)
            .GetMembers()
            .Select(member => member.Name)
            .Where(name => name.Contains("Helper", StringComparison.Ordinal))
            .ToArray();

        offered.ShouldBeEmpty(string.Join(", ", offered));
    }

    // ------------------------------------------ the thread the changes are raised on

    // The launcher window subscribes to this view model's PropertyChanged directly and
    // touches windows from it, and a command's CanExecuteChanged reaches a button the same
    // way. Neither is marshalled the way an ordinary binding is, so a change raised on a
    // pool thread is an error dialog in the player's face. That is what a ConfigureAwait
    // (false) in here bought, every time a game exited.
    [ShippingPackageFact]
    public void RaisesEveryChangeOnTheThreadItsCommandsWereCalledFrom()
    {
        var wrong = new ConcurrentBag<int>();
        var expected = 0;
        var raised = 0;

        Pump.Run(async () =>
        {
            expected = Environment.CurrentManagedThreadId;

            var model = Build(new SlowProbe());

            model.PropertyChanged += (_, _) => Seen();
            model.Servers.CollectionChanged += (_, e) =>
            {
                Seen();

                foreach (var added in e.NewItems?.OfType<ServerEntryViewModel>() ?? [])
                {
                    added.PropertyChanged += (_, _) => Seen();
                }
            };

            await model.LoadAsync();

            void Seen()
            {
                Interlocked.Increment(ref raised);

                if (Environment.CurrentManagedThreadId != expected)
                {
                    wrong.Add(Environment.CurrentManagedThreadId);
                }
            }
        });

        raised.ShouldBeGreaterThan(0);
        wrong.ShouldBeEmpty($"{wrong.Count} of {raised} changes were raised off the interface thread");
    }

    // And the same rule where the test above cannot reach: the tail of a launch only runs
    // when a real client exits, which is exactly where the dialog came from.
    [Fact]
    public void NeverHandsAContinuationToAnotherThreadInAViewModel()
    {
        var offenders = Directory
            .EnumerateDirectories(Path.Combine(Checkout.Root, "src"), "ViewModels", SearchOption.AllDirectories)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(file => !Checkout.IsBuildOutput(file))
            .SelectMany(file => File.ReadLines(file)
                .Select((text, index) => (Name: Path.GetFileName(file), Line: index + 1, Text: text)))
            .Where(line => line.Text.Contains("ConfigureAwait(false)", StringComparison.Ordinal))
            // The rule itself is written down in a comment, which is not a place it
            // can be broken.
            .Where(line => !line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(line => $"{line.Name}:{line.Line}")
            .ToArray();

        offenders.ShouldBeEmpty(
            $"a view model stepped off the interface thread at {string.Join(", ", offenders)}");
    }

    /// <summary>A probe that finishes somewhere else, the way a real socket does.</summary>
    private sealed class SlowProbe : IServerProbe
    {
        public async Task<bool> IsReachableAsync(ServerInfo server, CancellationToken cancellationToken = default)
        {
            // Long enough to be a real continuation rather than a synchronous return, which
            // is what puts the rest of the caller on whichever thread the timer fired on.
            await Task.Delay(10, cancellationToken);

            return true;
        }
    }

    /// <summary>Runs asynchronous work with one thread behind it, the way a window does.</summary>
    /// <remarks>
    /// A test host has no synchronization context, so every continuation lands on a pool
    /// thread and a view model that steps off the interface thread looks exactly like one
    /// that stays on it. This puts one in place: everything posted to it runs on the thread
    /// that called <see cref="Run"/>, and nothing else does.
    /// </remarks>
    private sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Work, object? State)> _queue = new();

        public static void Run(Func<Task> work)
        {
            var previous = Current;
            using var pump = new Pump();

            SetSynchronizationContext(pump);

            try
            {
                var task = work();

                // Off the pump's own thread, or the signal to stop would be queued behind
                // the work it is the signal for.
                _ = task.ContinueWith(_ => pump._queue.CompleteAdding(), TaskScheduler.Default);

                foreach (var (callback, state) in pump._queue.GetConsumingEnumerable())
                {
                    callback(state);
                }

                task.GetAwaiter().GetResult();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
            {
                // The work finished while this was on its way, so there is nothing left
                // running to run it on.
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => d?.Invoke(state);

        public void Dispose() => _queue.Dispose();
    }
}
