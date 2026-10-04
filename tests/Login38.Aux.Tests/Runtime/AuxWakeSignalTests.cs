using System.Diagnostics;
using Login38.Aux.Runtime;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

public sealed class AuxWakeSignalTests
{
    [Fact]
    public async Task PendingSignalWakesWithoutWaitingForTheCadenceTimeout()
    {
        var wake = new AuxWakeSignal();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var clock = Stopwatch.StartNew();

        wake.Signal();
        await wake.WaitAsync(TimeSpan.FromMinutes(1), cancellation.Token);

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RepeatedSignalsAreCoalescedInsteadOfOverflowing()
    {
        var wake = new AuxWakeSignal();

        Should.NotThrow(() =>
        {
            wake.Signal();
            wake.Signal();
            wake.Signal();
        });
    }
}
