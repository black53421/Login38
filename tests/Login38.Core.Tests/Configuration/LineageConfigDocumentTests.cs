using System.Buffers.Binary;
using Login38.Core.Configuration;
using Shouldly;

namespace Login38.Core.Tests.Configuration;

public sealed class LineageConfigDocumentTests
{
    private const int HeaderLength = 0x1C;

    [Fact]
    public void MinimalDocumentCarriesTheExpectedHeader()
    {
        var bytes = LineageConfigDocument.CreateMinimal(false, WindowMode.Size800x600).ToBytes();

        bytes.Length.ShouldBeGreaterThan(HeaderLength);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 26).ShouldBe("lineage configuration file");
        bytes[26].ShouldBe((byte)0x1A);
        bytes[27].ShouldBe((byte)0x00);
    }

    [Fact]
    public void MinimalDocumentRoundTripsThroughTheParser()
    {
        var bytes = LineageConfigDocument.CreateMinimal(true, WindowMode.Size1600x1200).ToBytes();

        var parsed = LineageConfigDocument.Parse(bytes);

        parsed.TryGetValue(LineageConfigDocument.FullScreenKey, out var fullScreen).ShouldBeTrue();
        fullScreen[0].ShouldBe((byte)1);
        ReadUInt32(parsed, LineageConfigDocument.WindowModeKey).ShouldBe(7u);
        ReadUInt32(parsed, LineageConfigDocument.PreviousWindowModeKey).ShouldBe(7u);
    }

    [Fact]
    public void SetsAnExistingValueInPlace()
    {
        var document = LineageConfigDocument.Parse(BuildConfig(fullScreen: 1, windowMode: 5));

        document.TrySetByte(LineageConfigDocument.FullScreenKey, 0).ShouldBeTrue();

        document.IsDirty.ShouldBeTrue();
        document.TryGetValue(LineageConfigDocument.FullScreenKey, out var value).ShouldBeTrue();
        value[0].ShouldBe((byte)0);
    }

    // Rewriting an identical value would bump the file's timestamp on every launch.
    [Fact]
    public void WritingTheSameValueDoesNotDirtyTheDocument()
    {
        var document = LineageConfigDocument.Parse(BuildConfig(fullScreen: 0, windowMode: 5));

        document.TrySetByte(LineageConfigDocument.FullScreenKey, 0).ShouldBeTrue();

        document.IsDirty.ShouldBeFalse();
    }

    // An older client's config legitimately lacks records this launcher knows about.
    [Fact]
    public void MissingKeyIsReportedRatherThanInserted()
    {
        var document = LineageConfigDocument.Parse(BuildConfig(fullScreen: 0, windowMode: 5));

        document.TrySetUInt32(LineageConfigDocument.PreviousWindowModeKey, 6).ShouldBeFalse();
        document.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void SizeMismatchIsRejectedRatherThanTruncated()
    {
        var document = LineageConfigDocument.Parse(BuildConfig(fullScreen: 0, windowMode: 5));

        Should.Throw<InvalidDataException>(
            () => document.TrySetUInt32(LineageConfigDocument.FullScreenKey, 1));
    }

    [Fact]
    public void UnknownRecordsArePreservedByAnEdit()
    {
        var original = BuildConfig(fullScreen: 0, windowMode: 5, includeUnknownRecord: true);
        var document = LineageConfigDocument.Parse(original);

        document.TrySetUInt32(LineageConfigDocument.WindowModeKey, 7).ShouldBeTrue();

        var edited = document.ToBytes();
        edited.Length.ShouldBe(original.Length);
        document.TryGetValue(0x99, out var unknown).ShouldBeTrue();
        unknown.ToArray().ShouldBe([0xAA, 0xBB, 0xCC, 0xDD]);
    }

    // String records have no size field, so a walker that assumes one desynchronises
    // and misreads every record after the first string.
    [Fact]
    public void SkipsStringRecordsCorrectly()
    {
        var data = new List<byte>();
        data.AddRange(new byte[HeaderLength]);
        AppendRecord(data, LineageConfigDocument.FullScreenKey, [1]);
        data.AddRange(BitConverter.GetBytes(0x2711u));
        data.AddRange("some/path/here\0"u8);
        AppendRecord(data, LineageConfigDocument.WindowModeKey, BitConverter.GetBytes(6u));
        data.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));

        var document = LineageConfigDocument.Parse([.. data]);

        ReadUInt32(document, LineageConfigDocument.WindowModeKey).ShouldBe(6u);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(HeaderLength)]
    public void RejectsATruncatedFile(int length) =>
        Should.Throw<InvalidDataException>(() => LineageConfigDocument.Parse(new byte[length]));

    [Fact]
    public void RejectsAnImplausibleRecordSize()
    {
        var data = new List<byte>();
        data.AddRange(new byte[HeaderLength]);
        data.AddRange(BitConverter.GetBytes(LineageConfigDocument.FullScreenKey));
        data.AddRange(BitConverter.GetBytes(0x0100_0000u));
        data.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));

        Should.Throw<InvalidDataException>(() => LineageConfigDocument.Parse([.. data]));
    }

    [Fact]
    public void RejectsAnUnterminatedStringRecord()
    {
        var data = new List<byte>();
        data.AddRange(new byte[HeaderLength]);
        data.AddRange(BitConverter.GetBytes(0x2711u));
        data.AddRange("no terminator"u8);

        Should.Throw<InvalidDataException>(() => LineageConfigDocument.Parse([.. data]));
    }

    [Fact]
    public void SaveOnlyWritesWhenDirty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"login38-cfg-{Guid.NewGuid():N}.cfg");
        try
        {
            File.WriteAllBytes(path, BuildConfig(fullScreen: 0, windowMode: 5));
            var document = LineageConfigDocument.Load(path);

            document.TrySetByte(LineageConfigDocument.FullScreenKey, 0);
            document.SaveIfDirty(path).ShouldBeFalse();

            document.TrySetByte(LineageConfigDocument.FullScreenKey, 1);
            document.SaveIfDirty(path).ShouldBeTrue();

            LineageConfigDocument.Load(path)
                .TryGetValue(LineageConfigDocument.FullScreenKey, out var value).ShouldBeTrue();
            value[0].ShouldBe((byte)1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static uint ReadUInt32(LineageConfigDocument document, uint key)
    {
        document.TryGetValue(key, out var value).ShouldBeTrue();
        return BinaryPrimitives.ReadUInt32LittleEndian(value);
    }

    private static byte[] BuildConfig(byte fullScreen, uint windowMode, bool includeUnknownRecord = false)
    {
        var data = new List<byte>();
        data.AddRange(new byte[HeaderLength]);
        AppendRecord(data, LineageConfigDocument.FullScreenKey, [fullScreen]);
        if (includeUnknownRecord)
        {
            AppendRecord(data, 0x99, [0xAA, 0xBB, 0xCC, 0xDD]);
        }

        AppendRecord(data, LineageConfigDocument.WindowModeKey, BitConverter.GetBytes(windowMode));
        data.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
        return [.. data];
    }

    private static void AppendRecord(List<byte> data, uint key, byte[] value)
    {
        data.AddRange(BitConverter.GetBytes(key));
        data.AddRange(BitConverter.GetBytes((uint)value.Length));
        data.AddRange(value);
    }
}
