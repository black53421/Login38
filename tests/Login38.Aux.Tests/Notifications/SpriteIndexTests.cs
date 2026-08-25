using System.Text;
using Login38.Aux.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers finding a named file inside the client's sprite archives.
/// </summary>
public sealed class SpriteIndexTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("login38-sprite-index").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ReadsAnEntryOutOfAnIndex()
    {
        var index = Index();

        index.Add(Idx([("1234.tbt", 0x1000u, 512u)]), "Sprite00.pak");

        var entry = index.Find("1234.tbt").ShouldNotBeNull();

        entry.Archive.ShouldBe("Sprite00.pak");
        entry.Offset.ShouldBe(0x1000u);
        entry.Size.ShouldBe(512u);
    }

    [Fact]
    public void ReadsSeveral()
    {
        var index = Index();

        index.Add(Idx([("a.tbt", 1, 2), ("b.png", 3, 4), ("c.tbt", 5, 6)]), "Sprite00.pak");

        index.Count.ShouldBe(3);
        index.Find("b.png")!.Value.Offset.ShouldBe(3u);
    }

    // The declared count is believed. The reference works the number out from the file's
    // length instead, so padding past the last entry becomes entries named after whatever
    // bytes were there.
    [Fact]
    public void StopsAtTheCountTheIndexDeclares()
    {
        byte[] padded = [.. Idx([("a.tbt", 1, 2)]), .. Enumerable.Repeat((byte)'X', 28)];
        var index = Index();

        index.Add(padded, "Sprite00.pak");

        index.Count.ShouldBe(1);
    }

    // And a count larger than the file cannot be believed either.
    [Fact]
    public void StopsAtTheEndOfTheFileWhateverTheCountSays()
    {
        var raw = Idx([("a.tbt", 1, 2)]);

        BitConverter.TryWriteBytes(raw.AsSpan(), 99u);

        var index = Index();

        index.Add(raw, "Sprite00.pak");

        index.Count.ShouldBe(1);
    }

    // The client searches Sprite00 upwards, so the first copy of a name is the one it uses.
    [Fact]
    public void KeepsTheFirstCopyOfANameItSees()
    {
        var index = Index();

        index.Add(Idx([("a.tbt", 1, 2)]), "Sprite00.pak");
        index.Add(Idx([("a.tbt", 9, 9)]), "Sprite01.pak");

        index.Find("a.tbt")!.Value.Archive.ShouldBe("Sprite00.pak");
    }

    [Fact]
    public void IgnoresAnEntryWithNoName()
    {
        var index = Index();

        index.Add(Idx([("", 1, 2), ("a.tbt", 3, 4)]), "Sprite00.pak");

        index.Count.ShouldBe(1);
    }

    [Fact]
    public void IgnoresAFileTooShortToHaveAHeader()
    {
        var index = Index();

        index.Add([1, 2], "Sprite00.pak");

        index.Count.ShouldBe(0);
    }

    [Fact]
    public void HasNothingToSayAboutANameItDoesNotHold() =>
        Index().Find("nothing.tbt").ShouldBeNull();

    // ---- against real files -----------------------------------------------------------

    [Fact]
    public void FindsTheArchivesBesideTheClient()
    {
        Archive("Sprite00", [("1234.tbt", "an icon")]);

        var index = Index();

        index.Load(_directory).ShouldBeTrue();
        index.Count.ShouldBe(1);
    }

    [Fact]
    public void ReadsAFileBackOutOfItsArchive()
    {
        Archive("Sprite00", [("1234.tbt", "an icon")]);

        var index = Index();

        index.Load(_directory);

        Encoding.ASCII.GetString(index.Read("1234.tbt", 64)!).ShouldStartWith("an icon");
    }

    // Both archives are read, and in name order rather than in whatever order the file
    // system happens to hand them over.
    [Fact]
    public void ReadsEveryArchiveInNameOrder()
    {
        Archive("Sprite01", [("shared.tbt", "second")]);
        Archive("Sprite00", [("shared.tbt", "first")]);

        var index = Index();

        index.Load(_directory);

        index.Find("shared.tbt")!.Value.Archive.ShouldEndWith("Sprite00.pak");
    }

    [Fact]
    public void PassesOverAnIndexWithNoArchiveBesideIt()
    {
        File.WriteAllBytes(Path.Combine(_directory, "Sprite00.idx"), Idx([("a.tbt", 0, 1)]));

        Index().Load(_directory).ShouldBeFalse();
    }

    [Fact]
    public void SaysSoRatherThanThrowingWhereTheClientIsNotThere() =>
        Index().Load(Path.Combine(_directory, "nowhere")).ShouldBeFalse();

    [Fact]
    public void HasNothingToReadBeforeItHasBeenLoaded() =>
        Index().Read("1234.tbt", 64).ShouldBeNull();

    private static SpriteIndex Index() => new(NullLogger<SpriteIndex>.Instance);

    /// <summary>Writes a matching pair of index and archive.</summary>
    private void Archive(string name, (string Name, string Content)[] files)
    {
        List<(string Name, uint Offset, uint Size)> entries = [];
        List<byte> pak = [];

        foreach (var (file, content) in files)
        {
            var bytes = Encoding.ASCII.GetBytes(content);

            entries.Add((file, (uint)pak.Count, (uint)bytes.Length));
            pak.AddRange(bytes);
        }

        File.WriteAllBytes(Path.Combine(_directory, name + ".idx"), Idx([.. entries]));
        File.WriteAllBytes(Path.Combine(_directory, name + ".pak"), [.. pak]);
    }

    /// <summary>Lays out an index the way the client's own tools do.</summary>
    private static byte[] Idx((string Name, uint Offset, uint Size)[] entries)
    {
        var raw = new byte[SpriteIndex.HeaderSize + (entries.Length * SpriteIndex.EntrySize)];

        BitConverter.TryWriteBytes(raw, (uint)entries.Length);

        for (var i = 0; i < entries.Length; i++)
        {
            var at = SpriteIndex.HeaderSize + (i * SpriteIndex.EntrySize);

            BitConverter.TryWriteBytes(raw.AsSpan(at), entries[i].Offset);
            Encoding.ASCII.GetBytes(entries[i].Name).CopyTo(raw.AsSpan(at + SpriteIndex.NameOffset));
            BitConverter.TryWriteBytes(raw.AsSpan(at + SpriteIndex.SizeOffset), entries[i].Size);
        }

        return raw;
    }
}
