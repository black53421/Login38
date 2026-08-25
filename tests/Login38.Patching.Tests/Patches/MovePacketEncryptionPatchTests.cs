using Login38.Core.Servers;
using Login38.Interop;
using Login38.Patching.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

public sealed class MovePacketEncryptionPatchTests
{
    /// <summary><c>movsx eax, byte ptr [edx+0x14]</c> / <c>cmp eax, 8</c> / <c>jz</c>.</summary>
    private static readonly byte[] StateObfuscation =
        [0x0F, 0xBE, 0x42, 0x14, 0x83, 0xF8, 0x08, 0x74, 0x21, 0x8B, 0x0D, 0xB8, 0xD2, 0xC2, 0x00];

    private const int StateObfuscationAt = 0x000;

    private const int StateObfuscationJump = 7;

    /// <summary><c>movsx edx, byte ptr [mode]</c> / <c>cmp edx, 3</c> / <c>jnz</c>.</summary>
    private static readonly byte[] PacketEncryption =
    [
        0x0F, 0xBE, 0x15, 0xE1, 0xAE, 0x9A, 0x00, 0x83, 0xFA, 0x03, 0x75, 0x22,
        0xA1, 0xB8, 0xD2, 0xC2, 0x00, 0x0F, 0xBE, 0x48, 0x15, 0x83, 0xF1, 0x49,
    ];

    private const int PacketEncryptionAt = 0x080;

    private const int PacketEncryptionJump = 10;

    private static readonly AuxConfig Enabled = new() { MovePacketNoEncrypt = true };

    private static MovePacketEncryptionPatch Patch() => new(NullLogger<MovePacketEncryptionPatch>.Instance);

    private static SyntheticClient WholeClient()
    {
        var client = SyntheticClient.Create();
        client.Place(StateObfuscationAt, StateObfuscation);
        client.Place(PacketEncryptionAt, PacketEncryption);
        return client;
    }

    [Fact]
    public void TurnsBothBranchesIntoUnconditionalJumps()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext(Enabled));

        client.ReadByte(StateObfuscationAt + StateObfuscationJump).ShouldBe((byte)0xEB);
        client.ReadByte(PacketEncryptionAt + PacketEncryptionJump).ShouldBe((byte)0xEB);
    }

    [Fact]
    public void LeavesTheJumpDistancesUntouched()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext(Enabled));

        client.ReadByte(StateObfuscationAt + StateObfuscationJump + 1).ShouldBe((byte)0x21);
        client.ReadByte(PacketEncryptionAt + PacketEncryptionJump + 1).ShouldBe((byte)0x22);
    }

    // Unscrambled but still encrypted is not half a fix — the server reads it as a
    // protocol error rather than as a movement packet.
    [Fact]
    public void FailsWhenOnlyOneBranchIsFound()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(StateObfuscationAt, StateObfuscation);

        var context = client.NewContext(Enabled);

        Should.Throw<GameProcessException>(() => Patch().Apply(context));
    }

    [Fact]
    public void RunningTwiceIsHarmless()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = WholeClient();

        Patch().Apply(client.NewContext(Enabled));
        Patch().Apply(client.NewContext(Enabled));

        client.ReadByte(PacketEncryptionAt + PacketEncryptionJump).ShouldBe((byte)0xEB);
    }

    // The code this rewrites is reached from the movement handler, so there is nothing to
    // find until the player is actually moving around the world.
    [Fact]
    public void WaitsUntilThePlayerIsInTheWorld() =>
        Patch().Phase.ShouldBe(PatchPhase.InWorld);

    [Fact]
    public void FollowsTheConfiguredSwitch()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();

        Patch().ShouldSatisfyAllConditions(
            p => p.ShouldApply(client.NewContext(Enabled)).ShouldBeTrue(),
            p => p.ShouldApply(client.NewContext(new AuxConfig { MovePacketNoEncrypt = false })).ShouldBeFalse());
    }
}
