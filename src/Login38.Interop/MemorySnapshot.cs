namespace Login38.Interop;

/// <summary>
/// A local copy of one address range of the game, taken once and searched many times.
/// </summary>
/// <remarks>
/// <para>
/// Nearly every signature patch searches the same executable range. Scanning through
/// <see cref="RemoteProcess.Scan(BytePattern, GameAddress, GameAddress)"/> re-reads that
/// range for every pattern, which is several megabytes of cross-process reads per patch
/// and adds up to a noticeable delay during startup — the worst possible time, since the
/// game is suspended or racing its own decryption.
/// </para>
/// <para>
/// Only valid for as long as the bytes it holds have not changed. Patches that write into
/// the snapshotted range invalidate it for anything reading the same bytes afterwards, so
/// <see cref="Apply"/> keeps the local copy in step when it writes.
/// </para>
/// </remarks>
public sealed class MemorySnapshot
{
    /// <summary>64 KB per read: large enough to amortise the syscall.</summary>
    private const int ChunkSize = 0x10000;

    /// <summary>
    /// Fallback granularity. <c>ReadProcessMemory</c> fails for the whole request if any
    /// page in it is inaccessible, so one guard page would otherwise cost 64 KB of
    /// readable code around it.
    /// </summary>
    private const int PageSize = 0x1000;

    /// <summary>A run of bytes that were readable, as an offset into the buffer.</summary>
    private readonly record struct Segment(int Offset, int Length);

    private readonly byte[] _buffer;
    private readonly Segment[] _segments;

    private MemorySnapshot(GameAddress start, byte[] buffer, Segment[] segments)
    {
        Start = start;
        _buffer = buffer;
        _segments = segments;
    }

    /// <summary>First address covered.</summary>
    public GameAddress Start { get; }

    /// <summary>One past the last address covered.</summary>
    public GameAddress End => Start + _buffer.Length;

    /// <summary>How much of the range was actually readable.</summary>
    public int ReadableBytes => _segments.Sum(s => s.Length);

    /// <summary>Reads <paramref name="start"/> up to <paramref name="end"/> in one pass.</summary>
    /// <remarks>
    /// Unreadable regions are recorded as holes rather than treated as failures: most of a
    /// process address space is unmapped, and a scan range is a generous guess around the
    /// code rather than a map of it.
    /// </remarks>
    public static MemorySnapshot Capture(RemoteProcess process, GameAddress start, GameAddress end)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, $"Must be above {start}.");
        }

        var length = checked((int)((long)end.Value - start.Value));
        var buffer = new byte[length];
        var segments = new List<Segment>();
        var runStart = -1;

        for (var offset = 0; offset < length;)
        {
            var take = Math.Min(ChunkSize, length - offset);
            var read = ReadAsMuchAsPossible(process, start, buffer, offset, take);

            if (read > 0 && runStart < 0)
            {
                runStart = offset;
            }

            if (read < take && runStart >= 0)
            {
                segments.Add(new Segment(runStart, offset + read - runStart));
                runStart = -1;
            }

            offset += take;
        }

        if (runStart >= 0)
        {
            segments.Add(new Segment(runStart, length - runStart));
        }

        return new MemorySnapshot(start, buffer, [.. segments]);
    }

    /// <summary>
    /// Reads a chunk, falling back to page granularity so a single guard page costs one
    /// page of coverage rather than the whole chunk. Returns the readable prefix length.
    /// </summary>
    private static int ReadAsMuchAsPossible(
        RemoteProcess process, GameAddress start, byte[] buffer, int offset, int count)
    {
        if (process.TryReadBytes(start + offset, buffer.AsSpan(offset, count)))
        {
            return count;
        }

        var read = 0;
        while (read < count)
        {
            var take = Math.Min(PageSize, count - read);
            if (!process.TryReadBytes(start + (offset + read), buffer.AsSpan(offset + read, take)))
            {
                break;
            }

            read += take;
        }

        return read;
    }

    /// <summary>Address of the first match, or null.</summary>
    public GameAddress? Find(BytePattern pattern)
    {
        foreach (var hit in Matches(pattern))
        {
            return hit;
        }

        return null;
    }

    /// <summary>Every match, in ascending address order.</summary>
    public IReadOnlyList<GameAddress> FindAll(BytePattern pattern) => [.. Matches(pattern)];

    /// <summary>Address of the only match, or null if there is none.</summary>
    /// <remarks>
    /// For signatures whose whole value is that they are unique. A second match means the
    /// signature is matching something it was not written for, and patching the first hit
    /// would corrupt unrelated code — quieter and far worse than not patching at all.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Matched more than once.</exception>
    public GameAddress? FindOnly(BytePattern pattern, string description)
    {
        var hits = FindAll(pattern);

        return hits.Count switch
        {
            0 => null,
            1 => hits[0],
            _ => throw new InvalidOperationException(
                $"The signature for {description} matched {hits.Count} times " +
                $"({string.Join(", ", hits)}); it is meant to be unique."),
        };
    }

    private IEnumerable<GameAddress> Matches(BytePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        // A match may not straddle a hole: the bytes on either side are not adjacent in
        // the target, whatever the buffer looks like.
        foreach (var segment in _segments)
        {
            for (var searched = 0; searched + pattern.Length <= segment.Length;)
            {
                var hit = pattern.IndexIn(_buffer.AsSpan(segment.Offset + searched, segment.Length - searched));
                if (hit < 0)
                {
                    break;
                }

                yield return Start + (segment.Offset + searched + hit);
                searched += hit + 1;
            }
        }
    }

    /// <summary>Copies out bytes that were captured, or returns false across a hole.</summary>
    public bool TryRead(GameAddress address, Span<byte> destination)
    {
        if (!TryGetOffset(address, destination.Length, out var offset))
        {
            return false;
        }

        _buffer.AsSpan(offset, destination.Length).CopyTo(destination);
        return true;
    }

    /// <summary>The captured byte at an address.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not covered by the snapshot.</exception>
    public byte ReadByte(GameAddress address)
    {
        if (!TryGetOffset(address, 1, out var offset))
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "Not covered by this snapshot.");
        }

        return _buffer[offset];
    }

    /// <summary>
    /// Writes into the game and into this snapshot, so later searches see the new bytes.
    /// </summary>
    /// <remarks>
    /// The alternative — writing through the process and leaving the copy stale — makes
    /// idempotency checks lie: a patch would keep finding the pre-patch signature it had
    /// just overwritten.
    /// </remarks>
    public void Apply(RemoteProcess process, GameAddress address, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(process);

        process.WriteCode(address, data);

        if (TryGetOffset(address, data.Length, out var offset))
        {
            data.CopyTo(_buffer.AsSpan(offset, data.Length));
        }
    }

    private bool TryGetOffset(GameAddress address, int length, out int offset)
    {
        offset = 0;

        if (length <= 0 || address < Start || address + length > End)
        {
            return false;
        }

        var candidate = (int)(address.Value - Start.Value);

        foreach (var segment in _segments)
        {
            if (candidate >= segment.Offset && candidate + length <= segment.Offset + segment.Length)
            {
                offset = candidate;
                return true;
            }
        }

        return false;
    }
}
