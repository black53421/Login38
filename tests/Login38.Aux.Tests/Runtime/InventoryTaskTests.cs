using Login38.Aux.Game;
using Login38.Aux.Runtime;
using Login38.Aux.Settings;
using Login38.Core.Text;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Runtime;

/// <summary>
/// Covers what the helper window is offered when it asks what is in the bag.
/// </summary>
/// <remarks>
/// Reading the bag means walking the client's memory across a process boundary, so what is
/// pinned here is as much about when it is not read as about what comes back.
/// </remarks>
public sealed class InventoryTaskTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly InventoryWatch _watch = new();

    public void Dispose() => _process.Dispose();

    [Fact]
    public void OffersNothingBeforeAnybodyLooks()
    {
        Run(Bag("治癒藥水"));

        _watch.Names.ShouldBeEmpty();
    }

    // The window is the only thing that wants this, and it is shut most of the time.
    [Fact]
    public void ReadsNothingBeforeAnybodyLooks()
    {
        var context = Run(Bag("治癒藥水"));

        context.Reads.ShouldBe(0);
    }

    [Fact]
    public void OffersTheBagOnceSomebodyLooks()
    {
        _watch.Wanted = true;

        Run(Bag("治癒藥水", "破爛的短劍"));

        _watch.Names.ShouldBe(["治癒藥水", "破爛的短劍"]);
    }

    // Names carry the stack count, which changes as the game is played. A setting naming
    // "金幣 (17,099)" would stop matching the moment one more coin was picked up.
    [Fact]
    public void OffersTheNameWithoutTheCount()
    {
        _watch.Wanted = true;

        Run(Bag("金幣 (17,099)"));

        _watch.Names.ShouldBe(["金幣"]);
    }

    [Fact]
    public void OffersWhatIsWornWithoutTheMark()
    {
        _watch.Wanted = true;

        Run(Bag("銀劍" + ItemNames.WieldedMark));

        _watch.Names.ShouldBe(["銀劍"]);
    }

    // Several of the same thing are one row in a list of choices.
    [Fact]
    public void OffersTheSameNameOnce()
    {
        _watch.Wanted = true;

        Run(Bag("治癒藥水 (5)", "治癒藥水 (12)"));

        _watch.Names.ShouldBe(["治癒藥水"]);
    }

    // Out of the world the bag reads as the last character's, or as nothing at all.
    [Fact]
    public void OffersNothingWhileNobodyIsPlaying()
    {
        _watch.Wanted = true;

        Run(Bag("治癒藥水"), inWorld: false);

        _watch.Names.ShouldBeEmpty();
    }

    [Fact]
    public void ForgetsTheBagOnceNobodyIsLooking()
    {
        _watch.Wanted = true;
        Run(Bag("治癒藥水"));
        _watch.Names.ShouldNotBeEmpty();

        _watch.Wanted = false;
        Run(Bag("治癒藥水"));

        _watch.Names.ShouldBeEmpty();
    }

    private StubContext Run(IReadOnlyList<InventoryItem> bag, bool inWorld = true)
    {
        var context = new StubContext(_process, new AuxSettings(), bag, inWorld);

        new InventoryTask(_watch).Tick(context);

        return context;
    }

    private static IReadOnlyList<InventoryItem> Bag(params string[] names) =>
        [.. names.Select((name, i) =>
            new InventoryItem(new GameAddress(0x1000u + ((uint)i * 0x10)), 0, 0x33, 0, false, 1, name))];

    /// <summary>A pass with a chosen bag and world, counting what was asked of the game.</summary>
    private sealed class StubContext(
        RemoteProcess process, AuxSettings settings, IReadOnlyList<InventoryItem> bag, bool inWorld)
        : AuxContext(process, settings, LegacyTextCodec.Auto)
    {
        public int Reads { get; private set; }

        public override bool IsInWorld
        {
            get
            {
                Reads++;
                return inWorld;
            }
        }

        public override IReadOnlyList<InventoryItem> Bag
        {
            get
            {
                Reads++;
                return bag;
            }
        }
    }
}
