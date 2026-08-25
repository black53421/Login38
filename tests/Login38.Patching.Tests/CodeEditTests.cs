using Login38.Interop;
using Shouldly;

namespace Login38.Patching.Tests;

/// <summary>
/// Covers the shared find-and-edit step every signature patch is built on.
/// </summary>
public sealed class CodeEditTests
{
    /// <summary><c>cmp eax, 0x5967</c> / <c>jz</c>, with the branch wildcarded.</summary>
    private const string BranchSignature = "83 C4 08 3D 67 59 00 00 ?? 2A";

    private static readonly byte[] BranchCode = [0x83, 0xC4, 0x08, 0x3D, 0x67, 0x59, 0x00, 0x00, 0x74, 0x2A];

    private const int BranchOffset = 8;

    [Fact]
    public void ReplacesTheByteAtTheSignatureOffset()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x100, BranchCode);

        var edit = CodeEdit.Replace(
            client.NewContext(), BranchSignature, "a test branch", BranchOffset, 0x74, 0xEB);

        edit.Status.ShouldBe(SiteStatus.Patched);
        edit.Address.ShouldBe(client.Base + 0x100 + BranchOffset);
        client.ReadByte(0x100 + BranchOffset).ShouldBe((byte)0xEB);
    }

    // Nothing else in the instruction may move: the branch displacement that follows
    // decides where control lands.
    [Fact]
    public void LeavesEverythingAroundTheEditAlone()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x100, BranchCode);

        CodeEdit.Replace(client.NewContext(), BranchSignature, "a test branch", BranchOffset, 0x74, 0xEB);

        client.Read(0x100, BranchCode.Length)
            .ShouldBe([0x83, 0xC4, 0x08, 0x3D, 0x67, 0x59, 0x00, 0x00, 0xEB, 0x2A]);
    }

    // Running twice against the same client has to be distinguishable from running once
    // against a client that does not have the site at all.
    [Fact]
    public void RecognisesASiteItHasAlreadyPatched()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x100, BranchCode);

        CodeEdit.Replace(client.NewContext(), BranchSignature, "a test branch", BranchOffset, 0x74, 0xEB);
        var second = CodeEdit.Replace(
            client.NewContext(), BranchSignature, "a test branch", BranchOffset, 0x74, 0xEB);

        second.Status.ShouldBe(SiteStatus.AlreadyPatched);
        second.Effective.ShouldBeTrue();
    }

    [Fact]
    public void ReportsASignatureThatIsNotThere()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();

        var edit = CodeEdit.Replace(
            client.NewContext(), BranchSignature, "a test branch", BranchOffset, 0x74, 0xEB);

        edit.Status.ShouldBe(SiteStatus.NotFound);
        edit.Effective.ShouldBeFalse();
    }

    // The wildcard makes the signature match whatever byte is at the branch. If that byte
    // is neither the original nor the patched value, the signature is matching something
    // it was not written for and writing to it would corrupt the client.
    [Fact]
    public void RefusesToEditASiteHoldingAnUnexpectedValue()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x100, [0x83, 0xC4, 0x08, 0x3D, 0x67, 0x59, 0x00, 0x00, 0x90, 0x2A]);

        var context = client.NewContext();

        Should.Throw<GameProcessException>(
            () => CodeEdit.Replace(context, BranchSignature, "a test branch", BranchOffset, 0x74, 0xEB));
    }

    [Fact]
    public void RefusesASignatureThatMatchesTwice()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x100, BranchCode);
        client.Place(0x300, BranchCode);

        var context = client.NewContext();

        Should.Throw<InvalidOperationException>(
            () => CodeEdit.Replace(context, BranchSignature, "a test branch", BranchOffset, 0x74, 0xEB));
    }

    [Fact]
    public void RefusesToChangeTheWidthOfWhatItReplaces()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        var context = client.NewContext();

        Should.Throw<ArgumentException>(() => CodeEdit.Replace(
            context, BytePattern.Parse(BranchSignature), "a test branch", BranchOffset, [0x74], [0xEB, 0x00]));
    }

    // ---- immediates ------------------------------------------------------------------

    /// <summary><c>push size</c> / <c>call malloc</c> / <c>add esp, 4</c>.</summary>
    private const string AllocationTemplate = "68 {0} E8 ?? ?? ?? ?? 83 C4 04";

    private static readonly byte[] AllocationCode =
        [0x68, 0x70, 0x18, 0x00, 0x00, 0xE8, 0xAA, 0xBB, 0xCC, 0xDD, 0x83, 0xC4, 0x04];

    [Fact]
    public void RewritesAnImmediateInsideASignature()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x80, AllocationCode);

        var edit = CodeEdit.ReplaceImmediate(
            client.NewContext(), AllocationTemplate, "an allocation", 1,
            BitConverter.GetBytes(0x1870u), BitConverter.GetBytes(400_000u));

        edit.Status.ShouldBe(SiteStatus.Patched);
        client.Read(0x80, 5).ShouldBe([0x68, .. BitConverter.GetBytes(400_000u)]);
    }

    // The immediate is what makes this signature unique, so it cannot be wildcarded and
    // the already-patched case has to be a second search for the new value.
    [Fact]
    public void RecognisesAnImmediateItHasAlreadyRewritten()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        client.Place(0x80, AllocationCode);

        CodeEdit.ReplaceImmediate(
            client.NewContext(), AllocationTemplate, "an allocation", 1,
            BitConverter.GetBytes(0x1870u), BitConverter.GetBytes(400_000u));

        var second = CodeEdit.ReplaceImmediate(
            client.NewContext(), AllocationTemplate, "an allocation", 1,
            BitConverter.GetBytes(0x1870u), BitConverter.GetBytes(400_000u));

        second.Status.ShouldBe(SiteStatus.AlreadyPatched);
        second.Address.ShouldBe(client.Base + 0x81);
    }

    [Fact]
    public void ReportsAnImmediateSignatureThatIsNotThere()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();

        CodeEdit.ReplaceImmediate(
                client.NewContext(), AllocationTemplate, "an allocation", 1,
                BitConverter.GetBytes(0x1870u), BitConverter.GetBytes(400_000u))
            .Status.ShouldBe(SiteStatus.NotFound);
    }

    [Fact]
    public void FillsATemplateWithTheBytesOfAValue()
    {
        var pattern = CodeEdit.Fill("68 {0} E8", BitConverter.GetBytes(0x1870u));

        pattern.Matches([0x68, 0x70, 0x18, 0x00, 0x00, 0xE8]).ShouldBeTrue();
        pattern.Matches([0x68, 0x71, 0x18, 0x00, 0x00, 0xE8]).ShouldBeFalse();
    }

    // ---- known addresses ---------------------------------------------------------------

    [Fact]
    public void ReplacesBytesAtAKnownAddress()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        var at = client.Place(0x200, [0x0F, 0xBE, 0x05, 0x7B, 0x1E, 0xC3, 0x00]);

        var edit = CodeEdit.ReplaceAt(
            client.NewContext(), at, "an AC read",
            [0x0F, 0xBE, 0x05, 0x7B, 0x1E, 0xC3, 0x00],
            [0xA1, 0x00, 0x00, 0x00, 0x20, 0x90, 0x90]);

        edit.Status.ShouldBe(SiteStatus.Patched);
        client.Read(0x200, 7).ShouldBe([0xA1, 0x00, 0x00, 0x00, 0x20, 0x90, 0x90]);
    }

    [Fact]
    public void RecognisesAKnownAddressItHasAlreadyPatched()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        var at = client.Place(0x200, [0x8B, 0x45, 0xE4, 0x90]);

        CodeEdit.ReplaceAt(client.NewContext(), at, "a truncating read",
                [0x0F, 0xB7, 0x45, 0xE4], [0x8B, 0x45, 0xE4, 0x90])
            .Status.ShouldBe(SiteStatus.AlreadyPatched);
    }

    // These sites have no signature to confirm them, so the expected bytes are the only
    // evidence the address still means what it meant. Writing anyway would put the
    // replacement into whatever the client put there instead.
    [Fact]
    public void LeavesAKnownAddressAloneWhenItHoldsSomethingElse()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        var at = client.Place(0x200, [0x11, 0x22, 0x33, 0x44]);

        var edit = CodeEdit.ReplaceAt(client.NewContext(), at, "a truncating read",
            [0x0F, 0xB7, 0x45, 0xE4], [0x8B, 0x45, 0xE4, 0x90]);

        edit.Status.ShouldBe(SiteStatus.NotFound);
        client.Read(0x200, 4).ShouldBe([0x11, 0x22, 0x33, 0x44]);
    }

    [Fact]
    public void DivertsAKnownAddressAndPadsTheRemainder()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        byte[] displaced = [0x0F, 0xBF, 0x48, 0x0E, 0x51, 0x8B, 0xC8];
        var at = client.Place(0x200, displaced);
        var target = client.Base + 0x400;

        CodeEdit.Divert(client.NewContext(), at, "a display hook", displaced, target)
            .Status.ShouldBe(SiteStatus.Patched);

        var written = client.Read(0x200, displaced.Length);
        written[0].ShouldBe((byte)0xE9);
        written[5..].ShouldAllBe(b => b == 0x90);

        var displacement = BitConverter.ToInt32(written, 1);
        unchecked((uint)(at.Value + 5 + displacement)).ShouldBe(target.Value);
    }

    // Re-reading a diverted site as if it held the original instructions would capture
    // this patch's own jump as the bytes to replay.
    [Fact]
    public void LeavesAnAlreadyDivertedSiteAlone()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        byte[] displaced = [0x0F, 0xBF, 0x48, 0x0E, 0x51, 0x8B, 0xC8];
        var at = client.Place(0x200, displaced);

        CodeEdit.Divert(client.NewContext(), at, "a display hook", displaced, client.Base + 0x400);
        var before = client.Read(0x200, displaced.Length);

        CodeEdit.Divert(client.NewContext(), at, "a display hook", displaced, client.Base + 0x600)
            .Status.ShouldBe(SiteStatus.AlreadyPatched);

        client.Read(0x200, displaced.Length).ShouldBe(before);
    }

    [Fact]
    public void RefusesToDivertASiteTooSmallForAJump()
    {
        if (!SyntheticClient.Supported)
        {
            return;
        }

        using var client = SyntheticClient.Create();
        var context = client.NewContext();

        Should.Throw<ArgumentException>(() => CodeEdit.Divert(
            context, client.Base, "a short site", [0x90, 0x90], client.Base + 0x400));
    }
}
