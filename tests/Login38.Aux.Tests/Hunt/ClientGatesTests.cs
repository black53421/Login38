using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers which of the client's flags the hunt is willing to force.
/// </summary>
/// <remarks>
/// Reading and writing these needs the client, so what a test can hold is the list itself —
/// and the list is the whole decision. Each one on it is a flag the client sets going into
/// something and clears coming out, with code that only runs while the client is still
/// going; each one left off it belongs to somebody else, and clearing it locally buys a
/// character that disagrees with the server about what it is allowed to do.
/// </remarks>
public sealed class ClientGatesTests
{
    [Fact]
    public void ForcesTheFlagsOnlyTheClientWouldHaveCleared() =>
        ClientGates.All.Select(gate => gate.Address.Value).ShouldBe(
        [
            HuntAddresses.CastQueued.Value,
            HuntAddresses.TickPending.Value,
            HuntAddresses.KickBlockerA.Value,
            HuntAddresses.KickBlockerB.Value,
            HuntAddresses.AttackInFlight.Value,
            HuntAddresses.AutoAttackRunning.Value,
        ]);

    // The one that wedges. The replayed click gives up the whole pass while a cast is
    // queued, and the only thing that fires a queued cast is the walk engine's tail — so
    // with no tick scheduled the two wait for each other until the game is restarted.
    [Fact]
    public void StartsWithTheOneThatDeadlocksAgainstTheReplayedClick() =>
        ClientGates.All[0].Address.ShouldBe(HuntAddresses.CastQueued);

    // A paralysis is the server's to end, and the mode word has a timer of its own in
    // HuntTask. Forcing either from here is a character acting on a permission it does not
    // have.
    [Fact]
    public void LeavesAloneWhatTheServerAndTheHoldTimerOwn() =>
        ClientGates.All.Select(gate => gate.Address.Value)
            .ShouldNotContain(value =>
                value == HuntAddresses.AttackBlocked.Value
                || value == HuntAddresses.MovementBlocked.Value);

    // A byte written as a dword takes the three flags packed beside it with it: TickPending,
    // "stepped" and "suppress relock" all live in the same word as CastQueued.
    [Fact]
    public void KnowsWhichOnesAreBytes() =>
        ClientGates.All.Where(gate => gate.Wide).Select(gate => gate.Address.Value)
            .ShouldBe([HuntAddresses.AttackInFlight.Value, HuntAddresses.AutoAttackRunning.Value]);

    [Fact]
    public void NamesEveryGateForTheLog() =>
        ClientGates.All.ShouldAllBe(gate => !string.IsNullOrWhiteSpace(gate.Name));
}
