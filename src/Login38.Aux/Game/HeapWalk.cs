using Login38.Interop;

namespace Login38.Aux.Game;

/// <summary>
/// Finds records of a known class in the client's heap.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps no index of the things standing in the world. Everything that wants one
/// — the scroll target, the monster colours — has to walk its heap looking for allocations
/// whose first word is the class's vtable pointer, which is the one field every instance of
/// a class shares and nothing else has a reason to hold.
/// </para>
/// <para>
/// One walk, shared. The reference has two copies of it, in two modules, with two sets of
/// the same bugs: both stop after the first sixteen megabytes of any region and then skip
/// the rest of it, which on a client that has been up for hours silently loses whatever is
/// past that point.
/// </para>
/// </remarks>
internal static class HeapWalk
{
    /// <summary>
    /// The player class's vtable, which every character, monster and summon record begins
    /// with.
    /// </summary>
    internal static GameAddress PlayerVtable => new(0x008DC08C);

    /// <summary>Where the heap starts. Below this is the client's own image.</summary>
    internal static GameAddress HeapStart => new(0x0100_0000);

    /// <summary>And the top of a 32-bit user address space, less a boundary page.</summary>
    internal static GameAddress HeapEnd => new(0x7FFF_0000);

    /// <summary>1 MB per read while walking a region.</summary>
    private const int ChunkSize = 0x10_0000;

    /// <summary>Called for each record found, with its fixed part already in hand.</summary>
    /// <param name="address">Where the record is.</param>
    /// <param name="record">Its first <c>recordLength</c> bytes.</param>
    internal delegate void Found(GameAddress address, ReadOnlySpan<byte> record);

    /// <summary>
    /// Walks a span of the client's address space, calling back for every record.
    /// </summary>
    /// <returns>How many bytes were read, for the log.</returns>
    internal static long Records(
        RemoteProcess process,
        GameAddress vtable,
        int recordLength,
        GameAddress from,
        GameAddress to,
        Found found)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(found);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordLength, 4);

        Span<byte> wanted = stackalloc byte[4];
        BitConverter.TryWriteBytes(wanted, vtable.Value);

        var buffer = new byte[ChunkSize];
        var straddling = new byte[recordLength];
        var read = 0L;

        foreach (var region in process.Regions(from, to))
        {
            // Module images are skipped: a record is allocated, and the client's own data
            // holds this vtable's address as a constant, which would match and is not one.
            if (region.Kind == MemoryKind.Image)
            {
                continue;
            }

            // Right through the region rather than stopping at a cap. A client that has
            // been running for hours has heap regions well past any figure worth
            // hard-coding, and giving up part way through one loses the rest silently.
            for (var offset = 0u; offset < region.Size;)
            {
                var take = (int)Math.Min(ChunkSize, region.Size - offset);
                var window = buffer.AsSpan(0, take);

                if (!process.TryReadBytes(region.Start + offset, window))
                {
                    // The client is free to unmap between the walk and the read.
                    break;
                }

                read += take;

                // Four at a time: a vtable pointer is always aligned, so three quarters of
                // the possible positions cannot hold one.
                for (var i = 0; i + 4 <= take; i += 4)
                {
                    if (!window.Slice(i, 4).SequenceEqual(wanted))
                    {
                        continue;
                    }

                    var address = region.Start + offset + i;

                    if (i + recordLength <= take)
                    {
                        found(address, window.Slice(i, recordLength));
                    }
                    else if (process.TryReadBytes(address, straddling))
                    {
                        // A record that runs past the end of this chunk, read on its own
                        // rather than dropped or waited for.
                        found(address, straddling);
                    }
                }

                offset += (uint)take;
            }
        }

        return read;
    }
}
