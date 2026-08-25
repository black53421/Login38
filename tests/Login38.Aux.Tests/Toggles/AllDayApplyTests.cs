using Login38.Aux.Toggles;
using Login38.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Toggles;

/// <summary>
/// Covers when the all-day toggle writes, when it leaves the game alone, and when it
/// refuses.
/// </summary>
/// <remarks>
/// Against real pages in this process, with a made-up list of sites. The client's own
/// addresses cannot be used here: half of what this toggle does is run code at fixed
/// addresses inside the process it is given, and against anything that is not the client
/// those addresses belong to somebody else.
/// </remarks>
public sealed class AllDayApplyTests : IDisposable
{
    private static readonly byte[] FirstOff = [0x55, 0x8B, 0xEC];
    private static readonly byte[] FirstOn = [0xB0, 0x01, 0xC3];
    private static readonly byte[] SecondOff = [0x01];
    private static readonly byte[] SecondOn = [0x0F];

    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);
    private readonly RemoteBuffer _page;
    private readonly AllDayToggle.Site[] _sites;
    private readonly RecordingLighting _lighting = new();

    public AllDayApplyTests()
    {
        _page = _process.AllocateScratch(0x100);

        _sites =
        [
            new("first", _page.Address, FirstOff, FirstOn),
            new("second", _page.Address + 0x10, SecondOff, SecondOn),
        ];
    }

    public void Dispose()
    {
        _page.Dispose();
        _process.Dispose();
    }

    [Fact]
    public void SwitchesEveryPlaceOn()
    {
        WriteBoth(on: false);

        Toggle().Apply(_process, wanted: true).ShouldBeTrue();

        Read(0).ShouldBe(FirstOn);
        Read(1).ShouldBe(SecondOn);
    }

    [Fact]
    public void SwitchesEveryPlaceOffAgain()
    {
        WriteBoth(on: true);

        Toggle().Apply(_process, wanted: false).ShouldBeTrue();

        Read(0).ShouldBe(FirstOff);
        Read(1).ShouldBe(SecondOff);
    }

    // A previous pass that failed part way leaves a client rendering worse than an
    // unpatched one, so a mixed state is something to finish rather than to refuse.
    [Fact]
    public void FinishesAPartlyAppliedChange()
    {
        Write(0, FirstOn);
        Write(1, SecondOff);

        Toggle().Apply(_process, wanted: true).ShouldBeTrue();

        Read(1).ShouldBe(SecondOn);
    }

    // Called twice a second for a whole session, so most passes have nothing to do.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoesNothingWhenItAlreadyMatches(bool wanted)
    {
        WriteBoth(wanted);

        Toggle().Apply(_process, wanted).ShouldBeTrue();

        _lighting.Brightened.ShouldBe(0);
        _lighting.Recomputed.ShouldBe(0);
    }

    // The cached lighting is only worth touching when something actually changed. Doing it
    // every pass would run code inside the game ten times a second.
    [Fact]
    public void BrightensTheCachedLightingWhenItSwitchesOn()
    {
        WriteBoth(on: false);

        Toggle().Apply(_process, wanted: true);

        _lighting.Brightened.ShouldBe(1);
        _lighting.Recomputed.ShouldBe(0);
    }

    [Fact]
    public void AsksTheClientToRecomputeWhenItSwitchesOff()
    {
        WriteBoth(on: true);

        Toggle().Apply(_process, wanted: false);

        _lighting.Recomputed.ShouldBe(1);
        _lighting.Brightened.ShouldBe(0);
    }

    // Neither state at any one place means somebody else has been here, or this is a
    // client build the port has not seen. Writing over it would be a guess.
    [Fact]
    public void RefusesWhenOnePlaceReadsAsSomethingElse()
    {
        Write(0, FirstOff);
        Write(1, [0xCC]);

        Toggle().Apply(_process, wanted: true).ShouldBeFalse();
    }

    // The reason every site is read before any is written: refusing costs nothing to undo.
    [Fact]
    public void LeavesTheOtherPlacesAloneWhenItRefuses()
    {
        Write(0, FirstOff);
        Write(1, [0xCC]);

        Toggle().Apply(_process, wanted: true);

        Read(0).ShouldBe(FirstOff);
        _lighting.Brightened.ShouldBe(0);
    }

    [Fact]
    public void RefusesWhenOnePlaceCannotBeRead()
    {
        AllDayToggle.Site[] sites =
        [
            new("first", _page.Address, FirstOff, FirstOn),
            new("nowhere", new GameAddress(0x10), SecondOff, SecondOn),
        ];

        Write(0, FirstOff);

        new AllDayToggle(NullLogger<AllDayToggle>.Instance, sites, _lighting)
            .Apply(_process, wanted: true).ShouldBeFalse();

        Read(0).ShouldBe(FirstOff);
    }

    // Unwinding after a write fails part way, which is the one case reading first cannot
    // rule out. Restores in reverse, and leaves the site that failed alone.
    [Fact]
    public void PutsBackWhatItWroteBeforeAWriteFailed()
    {
        WriteBoth(on: false);

        AllDayToggle.Unwind(_process, _sites, [FirstOn, SecondOn], failedAt: 1);

        Read(0).ShouldBe(FirstOn);
        Read(1).ShouldBe(SecondOff);
    }

    [Fact]
    public void HasNothingToPutBackWhenTheFirstWriteFailed()
    {
        WriteBoth(on: false);

        AllDayToggle.Unwind(_process, _sites, [FirstOn, SecondOn], failedAt: 0);

        Read(0).ShouldBe(FirstOff);
    }

    private AllDayToggle Toggle() =>
        new(NullLogger<AllDayToggle>.Instance, _sites, _lighting);

    private void WriteBoth(bool on)
    {
        Write(0, on ? FirstOn : FirstOff);
        Write(1, on ? SecondOn : SecondOff);
    }

    private void Write(int site, ReadOnlySpan<byte> bytes) =>
        _process.WriteBytes(_sites[site].Address, bytes);

    private byte[] Read(int site) =>
        _process.ReadBytes(_sites[site].Address, _sites[site].Length);

    /// <summary>Counts what it was asked for instead of touching a client.</summary>
    private sealed class RecordingLighting() : CachedLighting(NullLogger.Instance)
    {
        public int Brightened { get; private set; }

        public int Recomputed { get; private set; }

        public override void ForceBrightest(RemoteProcess process) => Brightened++;

        public override void Recompute(RemoteProcess process) => Recomputed++;
    }
}
