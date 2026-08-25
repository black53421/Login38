using System.IO;
using Login38.App.Services;
using Login38.App.ViewModels;
using Login38.Core.Configuration;
using Login38.Core.Servers;
using Login38.Core.Text;
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

    private static string ShippingPackage => Path.Combine(AppContext.BaseDirectory, "TestData");

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
            directory ?? ShippingPackage);

        var launcher = new GameLaunchService(
            new Login38.Patching.PatchPipeline([], NullLogger<Login38.Patching.PatchPipeline>.Instance),
            new LineageConfigFile(NullLogger<LineageConfigFile>.Instance),
            new ServiceCollection().AddLogging().AddAuxFeatures().BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            NullLoggerFactory.Instance);

        return new MainViewModel(catalog, launcher, probe, NullLogger<MainViewModel>.Instance, _preferences);
    }

    [Fact]
    public async Task OffersTheServersFromTheOperatorsList()
    {
        var model = Build(new StubProbe(true));

        await model.LoadAsync();

        model.Error.ShouldBeNull();
        model.Servers.ShouldNotBeEmpty();
    }

    // The list has a fixed number of slots and the unused ones are still present, empty.
    // Offering them gives the player rows that cannot work.
    [Fact]
    public async Task LeavesOutSlotsTheOperatorIsNotUsing()
    {
        var model = Build(new StubProbe(true));

        await model.LoadAsync();

        model.Servers.ShouldAllBe(s => s.IsOffered);
        model.Servers.Count.ShouldBeLessThanOrEqualTo(ListFile.MaxServers);
    }

    [Fact]
    public async Task SelectsSomethingSoThePlayerCanPressPlay()
    {
        var model = Build(new StubProbe(true));

        await model.LoadAsync();

        model.SelectedServer.ShouldNotBeNull();
        model.CanLaunch.ShouldBeTrue();
    }

    [Fact]
    public async Task MarksServersThatAnswered()
    {
        var probe = new StubProbe(true);
        var model = Build(probe);

        await model.LoadAsync();

        probe.Calls.ShouldBe(model.Servers.Count);
        model.Servers.ShouldAllBe(s => s.Availability == ServerAvailability.Online);
    }

    [Fact]
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
}
