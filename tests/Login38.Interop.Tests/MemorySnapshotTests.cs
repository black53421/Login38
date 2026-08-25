using Shouldly;

namespace Login38.Interop.Tests;

/// <summary>
/// Exercises the snapshot against real memory in this process.
/// </summary>
/// <remarks>
/// The interesting behaviour is what happens at the edges of what is mapped, and that
/// cannot be faked: a buffer of test data has no unreadable pages in it. Committing one
/// page inside a reserved region produces a real hole at a known address, so the
/// hole-handling assertions are about the same call the launcher makes against the game.
/// </remarks>
public sealed class MemorySnapshotTests
{
    private static bool Is32Bit => IntPtr.Size == 4;

    private const int PageSize = 0x1000;

    private static readonly byte[] Marker = [0x89, 0x45, 0xFC, 0x8B, 0x45, 0xFC, 0x3B, 0x05];

    [Fact]
    public void FindsAPatternItCaptured()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);
        process.WriteBytes(page.Address + 0x40, Marker);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + PageSize);

        snapshot.Find(BytePattern.Exact(Marker)).ShouldBe(page.Address + 0x40);
    }

    [Fact]
    public void FindsEveryOccurrence()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);
        process.WriteBytes(page.Address + 0x10, Marker);
        process.WriteBytes(page.Address + 0x200, Marker);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + PageSize);

        snapshot.FindAll(BytePattern.Exact(Marker))
            .ShouldBe([page.Address + 0x10, page.Address + 0x200]);
    }

    [Fact]
    public void WildcardsMatchWhateverIsThere()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);
        process.WriteBytes(page.Address + 0x80, [0x74, 0x3C, 0x83, 0x3D, 0xAA, 0xBB, 0xCC, 0xDD, 0x03]);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + PageSize);

        snapshot.Find(BytePattern.Parse("74 3C 83 3D ?? ?? ?? ?? 03")).ShouldBe(page.Address + 0x80);
    }

    // A signature that matches twice is not identifying anything, and patching the first
    // hit would silently corrupt the second site.
    [Fact]
    public void RefusesToPickBetweenDuplicateMatches()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);
        process.WriteBytes(page.Address + 0x10, Marker);
        process.WriteBytes(page.Address + 0x200, Marker);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + PageSize);

        Should.Throw<InvalidOperationException>(
            () => snapshot.FindOnly(BytePattern.Exact(Marker), "a test signature"));
    }

    [Fact]
    public void ReportsNothingRatherThanThrowingWhenASignatureIsAbsent()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + PageSize);

        snapshot.FindOnly(BytePattern.Exact([0x11, 0x22, 0x33, 0x44, 0x55, 0x66]), "an absent signature")
            .ShouldBeNull();
    }

    /// <summary>
    /// <c>VirtualAlloc</c> reserves at 64 KB granularity but commits only what was asked
    /// for, so a one-page allocation leaves the next fifteen pages reserved and
    /// unreadable — a hole at a known address, with no cooperation needed from anything
    /// else in the process.
    /// </summary>
    [Fact]
    public void CapturesWhatIsMappedAndSkipsWhatIsNot()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + (PageSize * 4));

        snapshot.ReadableBytes.ShouldBe(PageSize);
    }

    // Without the page-sized retry the whole 64 KB chunk containing the committed page
    // would be discarded, and every signature in it would go missing.
    [Fact]
    public void RecoversTheReadablePartOfAChunkThatCannotBeReadWhole()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);
        process.WriteBytes(page.Address + (PageSize - Marker.Length), Marker);

        // Far wider than the single committed page, and wider than one read chunk.
        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + 0x2_0000);

        snapshot.Find(BytePattern.Exact(Marker)).ShouldBe(page.Address + (PageSize - Marker.Length));
    }

    [Fact]
    public void ReadsBackBytesItCaptured()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);
        process.WriteBytes(page.Address, Marker);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + PageSize);
        var read = new byte[Marker.Length];

        snapshot.TryRead(page.Address, read).ShouldBeTrue();
        read.ShouldBe(Marker);
        snapshot.ReadByte(page.Address).ShouldBe(Marker[0]);
    }

    [Fact]
    public void ReadingAcrossAHoleFails()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + (PageSize * 2));

        snapshot.TryRead(page.Address + (PageSize - 2), new byte[4]).ShouldBeFalse();
    }

    // A stale copy makes idempotency checks lie: the patch keeps finding the signature it
    // just overwrote.
    [Fact]
    public void WritingThroughTheSnapshotUpdatesBothCopies()
    {
        if (!Is32Bit)
        {
            return;
        }

        using var process = RemoteProcess.Open((uint)Environment.ProcessId);
        using var page = process.AllocateScratch(PageSize);
        process.WriteBytes(page.Address + 0x30, Marker);

        var snapshot = MemorySnapshot.Capture(process, page.Address, page.Address + PageSize);
        snapshot.Apply(process, page.Address + 0x30, [0xEB]);

        snapshot.ReadByte(page.Address + 0x30).ShouldBe((byte)0xEB);
        process.Read<byte>(page.Address + 0x30).ShouldBe((byte)0xEB);
        snapshot.Find(BytePattern.Exact(Marker)).ShouldBeNull();
    }

    [Fact]
    public void RejectsAnEmptyRange()
    {
        using var process = RemoteProcess.Open((uint)Environment.ProcessId);

        Should.Throw<ArgumentOutOfRangeException>(
            () => MemorySnapshot.Capture(process, new GameAddress(0x1000), new GameAddress(0x1000)));
    }
}
