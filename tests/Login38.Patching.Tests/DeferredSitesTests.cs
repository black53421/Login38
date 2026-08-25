using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests;

/// <summary>
/// Covers the watcher two patches rely on to reach code the client decrypts late.
/// </summary>
/// <remarks>
/// Its job is to keep trying without ever becoming a reason the launcher will not close, so
/// what is worth pinning is when it stops: everything settled, the window closed, or the
/// launcher shutting down.
/// </remarks>
public sealed class DeferredSitesTests
{
    private static readonly GameAddress[] Sites =
        [new(0x0040_1000), new(0x0040_2000), new(0x0040_3000)];

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(5);

    // rel32 is measured from the end of the instruction. Off by one here reads the wrong
    // function's address and the site is passed over as not one of ours.
    [Theory]
    [InlineData(0xE8)]
    [InlineData(0xE9)]
    public void DecodesARelativeTarget(byte opcode)
    {
        // A real one: the first description call site, and the displacement that reaches
        // the plain line draw from it.
        var site = new GameAddress(0x0045_B52E);
        byte[] instruction = [opcode, .. BitConverter.GetBytes(0x0001_498D)];

        DeferredSites.RelativeTarget(site, instruction).ShouldBe(new GameAddress(0x0046_FEC0));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x90)]
    [InlineData(0xFF)]
    public void ReportsAnythingElseAsNotACall(byte opcode) =>
        DeferredSites.RelativeTarget(new GameAddress(0x0040_0000), [opcode, 0, 0, 0, 0]).ShouldBeNull();

    // Encrypted bytes decode as anything, including a truncated read.
    [Fact]
    public void ReportsAShortReadAsNotACall() =>
        DeferredSites.RelativeTarget(new GameAddress(0x0040_0000), [0xE8, 0, 0]).ShouldBeNull();

    // A backward call is the normal case for anything in the client's own address space
    // below the site.
    [Fact]
    public void DecodesABackwardTarget()
    {
        var site = new GameAddress(0x0079_7777);
        var target = new GameAddress(0x0046_FEC0);

        DeferredSites.RelativeTarget(site, DeferredSites.BuildCall(site, target)).ShouldBe(target);
    }

    [Fact]
    public void BuildsACallThatDecodesBackToItsTarget()
    {
        var site = new GameAddress(0x0045_B52E);
        var target = new GameAddress(0x0A00_0000);

        var call = DeferredSites.BuildCall(site, target);

        call[0].ShouldBe((byte)0xE8);
        DeferredSites.RelativeTarget(site, call).ShouldBe(target);
    }

    [Fact]
    public async Task StopsAsSoonAsEverySiteHasSettled()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        var tried = new List<GameAddress>();

        var settled = await DeferredSites.WatchAsync(
            process, Sites, site => { tried.Add(site); return SiteAttempt.Settled; },
            "test", NullLogger.Instance, poll: Poll);

        settled.ShouldBe(3);
        tried.ShouldBe(Sites);
    }

    // The point of the whole thing: a site that is not ready on the first pass is tried
    // again, and only that site — the ones already settled are not touched.
    [Fact]
    public async Task KeepsTryingTheSitesThatAreNotReady()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        var attempts = new Dictionary<GameAddress, int>();

        var settled = await DeferredSites.WatchAsync(
            process, Sites,
            site =>
            {
                var count = attempts.GetValueOrDefault(site) + 1;
                attempts[site] = count;

                // The last site takes three passes, the way a panel nobody has opened yet
                // eventually decrypts.
                return site == Sites[^1] && count < 3 ? SiteAttempt.NotReady : SiteAttempt.Settled;
            },
            "test", NullLogger.Instance, poll: Poll);

        settled.ShouldBe(3);
        attempts[Sites[0]].ShouldBe(1);
        attempts[Sites[^1]].ShouldBe(3);
    }

    // A player who never opens the panel is the ordinary case, so the watcher has to give
    // up rather than run for the life of the launcher.
    [Fact]
    public async Task GivesUpWhenTheWindowCloses()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        var settled = await DeferredSites.WatchAsync(
            process, Sites, _ => SiteAttempt.NotReady, "test", NullLogger.Instance,
            window: TimeSpan.FromMilliseconds(50), poll: Poll);

        settled.ShouldBe(0);
    }

    // Partial is the expected outcome, not a failure: the sites that settled stay settled.
    [Fact]
    public async Task ReportsWhatItGotWhenSomeNeverSettle()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        var settled = await DeferredSites.WatchAsync(
            process, Sites, site => site == Sites[0] ? SiteAttempt.Settled : SiteAttempt.NotReady,
            "test", NullLogger.Instance, window: TimeSpan.FromMilliseconds(50), poll: Poll);

        settled.ShouldBe(1);
    }

    // The launcher closing must not wait ten minutes for a watcher.
    [Fact]
    public async Task StopsWhenTheLauncherIsClosing()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(
            () => DeferredSites.WatchAsync(
                process, Sites, _ => SiteAttempt.NotReady, "test", NullLogger.Instance,
                window: TimeSpan.FromMinutes(10), poll: Poll,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task HasNothingToDoWithNoSites()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        var settled = await DeferredSites.WatchAsync(
            process, [], _ => SiteAttempt.Settled, "test", NullLogger.Instance, poll: Poll);

        settled.ShouldBe(0);
    }
}
