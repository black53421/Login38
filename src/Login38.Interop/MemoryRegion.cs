namespace Login38.Interop;

/// <summary>Where a run of pages came from.</summary>
public enum MemoryKind
{
    /// <summary>Allocated by the process — the heap, thread stacks, anything from <c>new</c>.</summary>
    Private,

    /// <summary>A view of a file or a shared section.</summary>
    Mapped,

    /// <summary>Part of a loaded executable or DLL.</summary>
    Image,
}

/// <summary>
/// A run of committed, readable pages in the game.
/// </summary>
/// <param name="Start">Where the run begins.</param>
/// <param name="Size">How many bytes long it is.</param>
/// <param name="Kind">Whether it is heap, a mapped file, or part of a loaded module.</param>
public readonly record struct MemoryRegion(GameAddress Start, uint Size, MemoryKind Kind)
{
    /// <summary>The first address past the run.</summary>
    public GameAddress End => Start + Size;
}
