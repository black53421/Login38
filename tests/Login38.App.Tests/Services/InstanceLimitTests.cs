using Login38.Interop;
using Shouldly;

namespace Login38.App.Tests.Services;

/// <summary>
/// Exercises the multi-instance cap against real named mutexes.
/// </summary>
/// <remarks>
/// A session-local prefix unique to each test keeps these from colliding with each other,
/// with a launcher running on the same machine, or with a previous run that left something
/// behind.
/// </remarks>
public sealed class InstanceLimitTests
{
    private static string Prefix([System.Runtime.CompilerServices.CallerMemberName] string test = "") =>
        $@"Local\L38Test_{test}_{Environment.ProcessId}_";

    // Off means one copy, not none. The reference read the same way and it is worth
    // pinning: a launcher that refuses every launch is the failure mode.
    [Theory]
    [InlineData(false, 0u, 1u)]
    [InlineData(false, 8u, 1u)]
    [InlineData(true, 0u, 0u)]
    [InlineData(true, 3u, 3u)]
    public void DerivesTheLimitInForce(bool allowed, uint configured, uint expected) =>
        InstanceLimit.EffectiveLimit(allowed, configured).ShouldBe(expected);

    [Fact]
    public void ClaimsASlotWhenOneIsFree()
    {
        using var slot = InstanceLimit.TryAcquire(2, Prefix());

        slot.ShouldNotBeNull();
        slot.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public void ClaimsADifferentSlotForEachInstance()
    {
        var prefix = Prefix();

        using var first = InstanceLimit.TryAcquire(3, prefix);
        using var second = InstanceLimit.TryAcquire(3, prefix);

        first!.Name.ShouldNotBe(second!.Name);
    }

    [Fact]
    public void RefusesOnceEverySlotIsTaken()
    {
        var prefix = Prefix();

        using var first = InstanceLimit.TryAcquire(2, prefix);
        using var second = InstanceLimit.TryAcquire(2, prefix);

        InstanceLimit.TryAcquire(2, prefix).ShouldBeNull();
    }

    [Fact]
    public void FreesTheSlotWhenTheGameEnds()
    {
        var prefix = Prefix();

        var first = InstanceLimit.TryAcquire(1, prefix);
        InstanceLimit.TryAcquire(1, prefix).ShouldBeNull();

        first!.Dispose();

        using var again = InstanceLimit.TryAcquire(1, prefix);
        again.ShouldNotBeNull();
    }

    // Zero means unlimited, and the caller still gets something to dispose so it does not
    // have to special-case the configuration everywhere it launches.
    [Fact]
    public void NeverRefusesWhenUnlimited()
    {
        var prefix = Prefix();

        using var first = InstanceLimit.TryAcquire(0, prefix);
        using var second = InstanceLimit.TryAcquire(0, prefix);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        first.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public void RejectsAnEmptyPrefix() =>
        Should.Throw<ArgumentException>(() => InstanceLimit.TryAcquire(1, "   "));

    // Disposing has to be safe from whichever thread happens to do it: the slot is claimed
    // on the launch path and released when the game exits, which is a different one.
    [Fact]
    public async Task ReleasesFromAThreadOtherThanTheOneThatClaimedIt()
    {
        var prefix = Prefix();
        var slot = InstanceLimit.TryAcquire(1, prefix);

        await Task.Run(() => slot!.Dispose());

        using var again = InstanceLimit.TryAcquire(1, prefix);
        again.ShouldNotBeNull();
    }
}
