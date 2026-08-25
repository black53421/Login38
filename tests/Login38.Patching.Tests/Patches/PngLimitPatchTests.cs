using Login38.Interop;
using Login38.Patching.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class PngLimitPatchTests
{
    private const uint TargetLimit = 100_000;

    private const uint TargetAllocation = TargetLimit * 4;

    /// <summary><c>push 0x1870</c> / <c>call malloc</c> / <c>add esp, 4</c>.</summary>
    private static readonly byte[] Allocation =
        [0x68, 0x70, 0x18, 0x00, 0x00, 0xE8, 0xAA, 0xBB, 0xCC, 0xDD, 0x83, 0xC4, 0x04];

    private const int AllocationAt = 0x000;

    /// <summary><c>mov [ebp-0x10], edx</c> / <c>cmp [ebp-0x10], 0x61C</c> / <c>jge</c>.</summary>
    private static readonly byte[] ConstructionLoop =
        [0x89, 0x55, 0xF0, 0x81, 0x7D, 0xF0, 0x1C, 0x06, 0x00, 0x00, 0x7D, 0x05];

    private const int ConstructionAt = 0x040;

    /// <summary><c>mov [ebp-4], ecx</c> / <c>cmp [ebp-4], 0x61C</c> / <c>jge</c>.</summary>
    private static readonly byte[] TeardownLoop =
        [0x89, 0x4D, 0xFC, 0x81, 0x7D, 0xFC, 0x1C, 0x06, 0x00, 0x00, 0x7D, 0x05];

    private const int TeardownAt = 0x080;

    private static PngLimitPatch Patch() => new(NullLogger<PngLimitPatch>.Instance);

    private static SyntheticClient WholeClient()
    {
        var client = SyntheticClient.Create();
        client.Place(AllocationAt, Allocation);
        client.Place(ConstructionAt, ConstructionLoop);
        client.Place(TeardownAt, TeardownLoop);
        return client;
    }

    [Fact]
    public void WidensAllThreeConstants()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext());

        client.Read(AllocationAt + 1, 4).ShouldBe(BitConverter.GetBytes(TargetAllocation));
        client.Read(ConstructionAt + 6, 4).ShouldBe(BitConverter.GetBytes(TargetLimit));
        client.Read(TeardownAt + 6, 4).ShouldBe(BitConverter.GetBytes(TargetLimit));
    }

    // The array holds pointers, so its size is the count times four. Getting that
    // multiplier wrong under-allocates and the construction loop writes past the end.
    [Fact]
    public void SizesTheArrayForPointersNotForCounts()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext());

        BitConverter.ToUInt32(client.Read(AllocationAt + 1, 4))
            .ShouldBe(BitConverter.ToUInt32(client.Read(ConstructionAt + 6, 4)) * 4);
    }

    [Fact]
    public void LeavesTheSurroundingInstructionsAlone()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext());

        client.Read(AllocationAt + 5, 8).ShouldBe([0xE8, 0xAA, 0xBB, 0xCC, 0xDD, 0x83, 0xC4, 0x04]);
        client.Read(ConstructionAt, 6).ShouldBe([0x89, 0x55, 0xF0, 0x81, 0x7D, 0xF0]);
        client.Read(ConstructionAt + 10, 2).ShouldBe([0x7D, 0x05]);
    }

    [Fact]
    public void RunningTwiceIsHarmless()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext());
        Patch().Apply(client.NewContext());

        client.Read(ConstructionAt + 6, 4).ShouldBe(BitConverter.GetBytes(TargetLimit));
    }

    // A wider construction loop over an array that was never grown writes off the end of
    // it, so a partial result has to fail rather than be left in place.
    [Fact]
    public void FailsWhenOnlySomeOfTheConstantsCanBeFound()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(ConstructionAt, ConstructionLoop);
        client.Place(TeardownAt, TeardownLoop);

        var context = client.NewContext();

        Should.Throw<GameProcessException>(() => Patch().Apply(context));
    }

    [Fact]
    public void LeavesTheClientAloneWhenItFailsPartWayThrough()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(AllocationAt, Allocation);

        var context = client.NewContext();
        Should.Throw<GameProcessException>(() => Patch().Apply(context));

        // The allocation is the one site present, and growing it alone is harmless: the
        // loops still walk the original count.
        client.Read(AllocationAt + 1, 4).ShouldBe(BitConverter.GetBytes(TargetAllocation));
    }
}
