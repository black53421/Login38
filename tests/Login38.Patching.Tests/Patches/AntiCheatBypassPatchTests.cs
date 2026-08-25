using Login38.Interop;
using Login38.Patching.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class AntiCheatBypassPatchTests
{
    /// <summary>
    /// <c>mov [ebp-4], eax</c> / <c>mov eax, [ebp-4]</c> / <c>cmp eax, [storedHash]</c> /
    /// <c>jz</c> / <c>cmp [gameState], 3</c> / <c>jnz</c>.
    /// </summary>
    private static readonly byte[] ComputedHashCheck =
    [
        0x89, 0x45, 0xFC, 0x8B, 0x45, 0xFC, 0x3B, 0x05, 0x11, 0x22, 0x33, 0x44,
        0x74, 0x3C, 0x83, 0x3D, 0x55, 0x66, 0x77, 0x88, 0x03, 0x75, 0x33,
    ];

    private const int ComputedHashJump = 12;

    /// <summary><c>add esp, 8</c> / <c>cmp eax, 0x5967</c> / <c>jz</c>.</summary>
    private static readonly byte[] ConstantHashCheck =
        [0x83, 0xC4, 0x08, 0x3D, 0x67, 0x59, 0x00, 0x00, 0x74, 0x2A];

    private const int ConstantHashJump = 8;

    private static AntiCheatBypassPatch Patch() => new(NullLogger<AntiCheatBypassPatch>.Instance);

    [Fact]
    public void TurnsBothChecksIntoUnconditionalJumps()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x000, ComputedHashCheck);
        client.Place(0x100, ConstantHashCheck);

        Patch().Apply(client.NewContext());

        client.ReadByte(0x000 + ComputedHashJump).ShouldBe((byte)0xEB);
        client.ReadByte(0x100 + ConstantHashJump).ShouldBe((byte)0xEB);
    }

    // The displacement decides where the jump lands. Changing the opcode without it is
    // the whole patch; changing both would jump somewhere arbitrary.
    [Fact]
    public void LeavesTheJumpDistanceUntouched()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x000, ComputedHashCheck);
        client.Place(0x100, ConstantHashCheck);

        Patch().Apply(client.NewContext());

        client.ReadByte(0x000 + ComputedHashJump + 1).ShouldBe((byte)0x3C);
        client.ReadByte(0x100 + ConstantHashJump + 1).ShouldBe((byte)0x2A);
    }

    [Fact]
    public void RunningTwiceIsHarmless()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x000, ComputedHashCheck);
        client.Place(0x100, ConstantHashCheck);

        Patch().Apply(client.NewContext());
        Patch().Apply(client.NewContext());

        client.ReadByte(0x000 + ComputedHashJump).ShouldBe((byte)0xEB);
    }

    // Every other patch changes memory the first check hashes, so a client with its
    // integrity checks intact exits as soon as anything else works. Reporting success
    // here would misattribute that.
    [Fact]
    public void FailsWhenNeitherCheckIsFound()
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
    public void PatchesWhatItCanFindWhenOnlyOneCheckIsPresent()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x100, ConstantHashCheck);

        Patch().Apply(client.NewContext());

        client.ReadByte(0x100 + ConstantHashJump).ShouldBe((byte)0xEB);
    }

    // Nothing to wait for: the checks run during startup, and every later patch changes
    // memory the first one hashes.
    [Fact]
    public void AppliesAsSoonAsTheClientIsDecrypted() => Patch().ShouldSatisfyAllConditions(
            p => p.Name.ShouldBe("anti-cheat-bypass"),
            p => p.Phase.ShouldBe(PatchPhase.Startup));
}
