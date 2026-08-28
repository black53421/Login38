using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.Aux.Tests.Hunt;

/// <summary>
/// Covers the reading that decides whether the client is already busy walking.
/// </summary>
/// <remarks>
/// The click itself is <see cref="ClickDetourTests"/>; what is left here is when to ask for
/// one. One run of the walk engine is one step of the character, so asking while the client
/// is already stepping is a character that suddenly moves at double speed — which is what it
/// did, and this is the reading that stopped it.
/// </remarks>
public sealed class AttackChainTests
{
    [Fact]
    public void FindsATickAlreadyInTheQueue() =>
        AttackChain.HoldsTick(Queue(0x005B0350, 0x005A4010, HuntAddresses.WalkEngineTick.Value))
            .ShouldBeTrue();

    // A queue full of the client's other work. This is what a busy idle character looks
    // like — twenty entries, none of them the walk engine.
    [Fact]
    public void SaysNothingIsQueuedWhenTheWalkEngineIsAbsent() =>
        AttackChain.HoldsTick(Queue(0x005B0350, 0x004EE850, 0x005A4010, 0x005A6F20))
            .ShouldBeFalse();

    [Fact]
    public void SaysNothingIsQueuedForAnEmptyQueue() =>
        AttackChain.HoldsTick([]).ShouldBeFalse();

    // Twelve bytes an entry, and the callback is the middle four. Read at the wrong stride
    // this address appears as a due time or an argument, and neither is a queued tick.
    [Fact]
    public void ReadsTheCallbackRatherThanTheTimeOrTheArgument()
    {
        var address = BitConverter.GetBytes(HuntAddresses.WalkEngineTick.Value);

        AttackChain.HoldsTick([.. address, .. new byte[8]]).ShouldBeFalse();
        AttackChain.HoldsTick([.. new byte[8], .. address]).ShouldBeFalse();
        AttackChain.HoldsTick([.. new byte[4], .. address, .. new byte[4]]).ShouldBeTrue();
    }

    // A partial entry at the end, which is what a read that caught the client mid-write
    // looks like. Running off the end of it would be a crash of the launcher.
    [Fact]
    public void IgnoresAnEntryThatWasCutShort() =>
        AttackChain.HoldsTick([.. Queue(0x005A4010), 0x00, 0x00]).ShouldBeFalse();

    /// <summary>Scheduler entries: a due time, a callback, an argument.</summary>
    private static byte[] Queue(params uint[] callbacks) =>
        [.. callbacks.SelectMany(callback => new byte[4]
            .Concat(BitConverter.GetBytes(callback))
            .Concat(new byte[4]))];
}
