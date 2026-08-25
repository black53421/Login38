using Login38.Core.Servers;
using Login38.Interop;
using Login38.Patching.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class ImageLimitPatchTests
{
    /// <summary><c>push 0</c> / <c>push tag</c> / <c>push 7000</c> / <c>call allocRange</c>.</summary>
    private static readonly byte[] ResourceRange =
        [0x6A, 0x00, 0x68, 0x11, 0x22, 0x33, 0x44, 0x68, 0x58, 0x1B, 0x00, 0x00, 0xE8];

    private const int ResourceRangeAt = 0x000;

    /// <summary><c>cmp dword ptr [ebp-0x10], 6295</c>.</summary>
    private static readonly byte[] BoundCheckShortDisplacement =
        [0x81, 0x7D, 0xF0, 0x97, 0x18, 0x00, 0x00];

    private const int BoundShortAt = 0x040;

    /// <summary><c>cmp dword ptr [ebp+disp32], 6295</c>.</summary>
    private static readonly byte[] BoundCheckLongDisplacement =
        [0x81, 0xBD, 0x11, 0x22, 0x33, 0x44, 0x97, 0x18, 0x00, 0x00];

    private const int BoundLongAt = 0x080;

    /// <summary><c>push 25180</c> — the surface array's size in bytes.</summary>
    private static readonly byte[] ArrayAllocation = [0x68, 0x5C, 0x62, 0x00, 0x00];

    private const int ArrayAllocationAt = 0x0C0;

    private static ImageLimitPatch Patch() => new(NullLogger<ImageLimitPatch>.Instance);

    private static SyntheticClient WholeClient()
    {
        var client = SyntheticClient.Create();
        client.Place(ResourceRangeAt, ResourceRange);
        client.Place(BoundShortAt, BoundCheckShortDisplacement);
        client.Place(BoundLongAt, BoundCheckLongDisplacement);
        client.Place(ArrayAllocationAt, ArrayAllocation);
        return client;
    }

    [Fact]
    public void WidensEverySiteThatEncodesALimit()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();
        var limit = BitConverter.GetBytes(50_000u);

        Patch().Apply(client.NewContext(new AuxConfig { ImgLimitValue = 50_000 }));

        client.Read(ResourceRangeAt + 8, 4).ShouldBe(limit);
        client.Read(BoundShortAt + 3, 4).ShouldBe(limit);
        client.Read(BoundLongAt + 6, 4).ShouldBe(limit);
        client.Read(ArrayAllocationAt + 1, 4).ShouldBe(BitConverter.GetBytes(50_000u * 4));
    }

    // The compiler emitted the same check once per register it kept the index in, so
    // handling only the form that happened to be looked at first leaves live bounds
    // checks against the old limit.
    [Fact]
    public void HandlesEveryAddressingFormOfTheBoundCheck()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x000, [0x81, 0x79, 0x08, 0x97, 0x18, 0x00, 0x00]);
        client.Place(0x020, [0x81, 0x7B, 0x08, 0x97, 0x18, 0x00, 0x00]);
        client.Place(0x040, [0x81, 0x7E, 0x08, 0x97, 0x18, 0x00, 0x00]);
        client.Place(0x060, [0x81, 0x7F, 0x08, 0x97, 0x18, 0x00, 0x00]);

        Patch().Apply(client.NewContext(new AuxConfig { ImgLimitValue = 50_000 }));

        foreach (var at in (int[])[0x000, 0x020, 0x040, 0x060])
        {
            client.Read(at + 3, 4).ShouldBe(BitConverter.GetBytes(50_000u));
        }
    }

    [Fact]
    public void LeavesTheInstructionAroundTheImmediateAlone()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext(new AuxConfig { ImgLimitValue = 50_000 }));

        client.Read(ResourceRangeAt, 8).ShouldBe([0x6A, 0x00, 0x68, 0x11, 0x22, 0x33, 0x44, 0x68]);
        client.Read(BoundLongAt, 6).ShouldBe([0x81, 0xBD, 0x11, 0x22, 0x33, 0x44]);
    }

    [Theory]
    [InlineData(1u, 6295u)]           // below the client's own limit
    [InlineData(9_000_000u, 500_000u)] // far past what the array can hold
    public void ClampsTheConfiguredLimit(uint configured, uint expected)
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext(new AuxConfig { ImgLimitValue = configured }));

        client.Read(ResourceRangeAt + 8, 4).ShouldBe(BitConverter.GetBytes(expected));
    }

    [Fact]
    public void RunningTwiceIsHarmless()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext(new AuxConfig { ImgLimitValue = 50_000 }));
        Patch().Apply(client.NewContext(new AuxConfig { ImgLimitValue = 50_000 }));

        client.Read(BoundShortAt + 3, 4).ShouldBe(BitConverter.GetBytes(50_000u));
    }

    [Fact]
    public void FailsWhenNoSiteIsFound()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        var context = client.NewContext();

        Should.Throw<GameProcessException>(() => Patch().Apply(context));
    }

    [Fact]
    public void FollowsTheConfiguredSwitch()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();

        Patch().ShouldSatisfyAllConditions(
            p => p.ShouldApply(client.NewContext(new AuxConfig { ImgLimitEnabled = true })).ShouldBeTrue(),
            p => p.ShouldApply(client.NewContext(new AuxConfig { ImgLimitEnabled = false })).ShouldBeFalse());
    }
}
