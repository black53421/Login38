using System.Text;
using Login38.Core.Cryptography;
using Login38.Core.Morph;
using Shouldly;

namespace Login38.Core.Tests.Morph;

/// <summary>
/// Covers the morph table container: what it accepts, what it refuses, and that the two
/// halves of the format agree.
/// </summary>
/// <remarks>
/// The reader and the writer are the whole test surface for a format with no other
/// implementation to compare against. The refusals matter as much as the round trip — the
/// tag is the only thing standing between a third-party file and a buffer written into a
/// running client.
/// </remarks>
public sealed class MorphPackageTests
{
    private const string Table = "1234\t5678\tcloak\n2345\t6789\thelm\n";

    private static byte[] Plain => Encoding.ASCII.GetBytes(Table);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTripsATable(bool compress) =>
        MorphPackage.Decrypt(MorphPackage.Encrypt(Plain, compress))
            .ShouldBe([MorphPackage.Marker, .. Plain]);

    // The client reads this byte first and stops without it, so it is part of the format
    // rather than a detail of how the file was stored.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlwaysProducesTheMarkerByte(bool compress) =>
        MorphPackage.Decrypt(MorphPackage.Encrypt(Plain, compress))[0].ShouldBe(MorphPackage.Marker);

    // Compressed and uncompressed are told apart by whether the marker survived the
    // scramble, not by a flag — so a table that happens not to compress must still read
    // back correctly.
    [Fact]
    public void ReadsBackATableThatDidNotCompress()
    {
        var incompressible = Convert.FromHexString("53" + string.Concat(
            Enumerable.Range(0, 64).Select(i => (i * 37 % 256).ToString("X2", null))));

        MorphPackage.Decrypt(MorphPackage.Encrypt(incompressible, compress: false))
            .ShouldBe([MorphPackage.Marker, .. incompressible]);
    }

    [Fact]
    public void AcceptsWhatItProduced() =>
        MorphPackage.IsPackage(MorphPackage.Encrypt(Plain)).ShouldBeTrue();

    // The whole point of the tag: a file from someone else's tool is refused rather than
    // decrypted into a buffer that gets written into the client.
    [Fact]
    public void RefusesAFileWithoutTheOperatorsTag()
    {
        var forged = MorphPackage.Encrypt(Plain);
        forged[^1] ^= 0xFF;

        MorphPackage.IsPackage(forged).ShouldBeFalse();
        Should.Throw<MorphPackageException>(() => MorphPackage.Decrypt(forged));
    }

    // The tag covers the header too, so retagging is the only way to alter a package —
    // which is exactly what a third-party tool cannot do.
    [Fact]
    public void RefusesAPackageWhoseBodyWasEdited()
    {
        var edited = MorphPackage.Encrypt(Plain);
        edited[MorphPackage.MinimumLength] ^= 0x01;

        MorphPackage.IsPackage(edited).ShouldBeFalse();
    }

    [Fact]
    public void RefusesAFileTooShortToBeOne()
    {
        MorphPackage.IsPackage(new byte[MorphPackage.MinimumLength - 1]).ShouldBeFalse();
        Should.Throw<MorphPackageException>(() => MorphPackage.Decrypt(new byte[8]));
    }

    // An empty table is still a table. The header, tag and marker are all still there, so
    // there is nothing about it to reject.
    [Fact]
    public void HandlesAnEmptyTable() =>
        MorphPackage.Decrypt(MorphPackage.Encrypt([])).ShouldBe([MorphPackage.Marker]);

    // Rejected before anything is inflated: a claimed size in the hundreds of megabytes is
    // a corrupt or hostile header, and acting on it costs the machine either way.
    [Fact]
    public void RefusesAnImplausiblePlainSize()
    {
        var package = MorphPackage.Encrypt(Plain);
        BitConverter.GetBytes(200_000_000u).CopyTo(package, 0);
        MorphAuth.ComputeTag(package.AsSpan(..^MorphAuth.TagLength), package.AsSpan(^MorphAuth.TagLength..));

        MorphPackage.IsPackage(package).ShouldBeFalse();
        Should.Throw<MorphPackageException>(() => MorphPackage.Decrypt(package));
    }

    // Each package carries its own key, so two builds of the same table share no bytes
    // past the length field. Otherwise a player could diff two releases to find the change.
    [Fact]
    public void UsesADifferentKeyEachTime()
    {
        var first = MorphPackage.Encrypt(Plain);
        var second = MorphPackage.Encrypt(Plain);

        first.ShouldNotBe(second);
        first.AsSpan(4, 16).ToArray().ShouldNotBe(second.AsSpan(4, 16).ToArray());
    }

    // The header records the plain length, which is what the encoder reports to the
    // operator. Wrong, and the size it prints is not the size of anything.
    [Fact]
    public void RecordsThePlainLengthIncludingTheMarker() =>
        BitConverter.ToUInt32(MorphPackage.Encrypt(Plain)).ShouldBe((uint)Plain.Length + 1);

    // Operators edit tables as text and only run the encoder to publish. A plain file has
    // to work as-is, or the feature cannot be tested without the encoder.
    [Fact]
    public void ReadsAPlainTextTableWithoutDecryptingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"morph-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, Plain);

        try
        {
            MorphPackage.Load(path).ShouldBe([MorphPackage.Marker, .. Plain]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadsAPackageFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"morph-{Guid.NewGuid():N}.pak");
        File.WriteAllBytes(path, MorphPackage.Encrypt(Plain));

        try
        {
            MorphPackage.IsPackage(path).ShouldBeTrue();
            MorphPackage.Load(path).ShouldBe([MorphPackage.Marker, .. Plain]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Asked before the feature is turned on at all, so a file that is not there is an
    // answer rather than a failure.
    [Fact]
    public void ReportsAMissingFileAsNotAPackage() =>
        MorphPackage.IsPackage(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.pak"))
            .ShouldBeFalse();
}
