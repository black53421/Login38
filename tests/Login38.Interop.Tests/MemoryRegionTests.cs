using Login38.Interop;
using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// Covers the walk over what is actually mapped in a process.
/// </summary>
/// <remarks>
/// Against this process, which is the only one a test may take apart. Most of a 32-bit
/// address space is not mapped at all, and searching it by reading blind would be tens of
/// thousands of failing calls — asking the kernel where the holes are is what makes a heap
/// search finish this side of a minute.
/// </remarks>
public sealed class MemoryRegionTests : IDisposable
{
    private readonly RemoteProcess _process = RemoteProcess.Open((uint)Environment.ProcessId);

    public void Dispose() => _process.Dispose();

    [Fact]
    public void FindsSomethingMapped() =>
        _process.Regions(new GameAddress(0x0001_0000), new GameAddress(0x7FFF_0000))
            .ShouldNotBeEmpty();

    // Every run given back has to be readable, or the walk is not doing its one job.
    [Fact]
    public void GivesBackRunsThatCanBeRead()
    {
        var buffer = new byte[16];

        foreach (var region in Some())
        {
            _process.TryReadBytes(region.Start, buffer).ShouldBeTrue($"{region.Start} should be readable");
        }
    }

    [Fact]
    public void GivesBackRunsInOrderWithoutOverlapping()
    {
        var previous = GameAddress.Zero;

        foreach (var region in Some())
        {
            region.Start.ShouldBeGreaterThanOrEqualTo(previous);
            region.Size.ShouldBeGreaterThan(0u);

            previous = region.End;
        }
    }

    // The first query lands wherever the caller asked, which is usually inside a run rather
    // than at its start. Handing back the whole run would report memory outside the range.
    [Fact]
    public void ClipsTheFirstAndLastRunToWhatWasAsked()
    {
        var whole = Some().First();

        // Half a page in, so the query lands inside a run rather than on its edge.
        var from = whole.Start + 0x800;
        var to = from + 0x1000;

        var clipped = _process.Regions(from, to).ToList();

        clipped.ShouldNotBeEmpty();
        clipped[0].Start.ShouldBe(from);
        clipped[^1].End.ShouldBeLessThanOrEqualTo(to);
    }

    [Fact]
    public void HasNothingToSayAboutAnEmptyRange() =>
        _process.Regions(new GameAddress(0x0040_0000), new GameAddress(0x0040_0000)).ShouldBeEmpty();

    // A managed process has its own image mapped, so both kinds should turn up. If images
    // stopped being reported the entity scan would search megabytes of them for nothing.
    [Fact]
    public void TellsHeapFromLoadedModules()
    {
        var kinds = _process.Regions(new GameAddress(0x0001_0000), new GameAddress(0x7FFF_0000))
            .Take(4096)
            .Select(r => r.Kind)
            .Distinct()
            .ToList();

        kinds.ShouldContain(MemoryKind.Private);
        kinds.ShouldContain(MemoryKind.Image);
    }

    /// <summary>Enough runs to check a property, without walking the whole space.</summary>
    private List<MemoryRegion> Some() =>
        [.. _process.Regions(new GameAddress(0x0001_0000), new GameAddress(0x7FFF_0000)).Take(64)];
}
