using Login38.Interop;

namespace Login38.Aux.Toggles;

/// <summary>What was found at a place the launcher wants to patch.</summary>
public enum SiteState
{
    /// <summary>Exactly what the client shipped.</summary>
    Stock,

    /// <summary>The jump this launcher wrote.</summary>
    Ours,

    /// <summary>Something else, which is nobody's business to overwrite.</summary>
    Foreign,
}

/// <summary>
/// One place in the client's code that a detour replaces.
/// </summary>
/// <remarks>
/// <para>
/// Every site is a run of whole instructions ending on an instruction boundary, replaced by
/// a five-byte jump and padded with no-ops. The bytes that were there are carried here
/// rather than read at install time, because the point is to recognise the client this port
/// was built against — reading first and trusting whatever is found would happily detour a
/// different build into code written for this one.
/// </para>
/// <para>
/// The same check does the other half of the job on the way out: a site holding something
/// that is neither the client's bytes nor this launcher's jump belongs to somebody else,
/// and writing the client's bytes over it would break whatever put it there.
/// </para>
/// </remarks>
/// <param name="Address">Where the run starts.</param>
/// <param name="Stock">The bytes the client shipped there.</param>
/// <param name="Resume">Where the detour comes back to, once it has replayed the run.</param>
public readonly record struct HookSite(GameAddress Address, byte[] Stock, GameAddress Resume)
{
    /// <summary>A near jump, which is what every site is replaced by.</summary>
    internal const int JumpLength = 5;

    /// <summary>How many bytes the site covers.</summary>
    public int Length => Stock.Length;

    /// <summary>
    /// The jump that replaces the run, padded out to its full length.
    /// </summary>
    /// <remarks>
    /// The padding is no-ops rather than nothing: the bytes after the jump are still
    /// reachable, because the client branches into the middle of this run from elsewhere,
    /// and leaving half an instruction there would be executed.
    /// </remarks>
    public byte[] JumpTo(GameAddress target)
    {
        if (Length < JumpLength)
        {
            throw new InvalidOperationException(
                $"A site of {Length} bytes at {Address} is too short to hold a jump.");
        }

        var patch = new byte[Length];

        Array.Fill(patch, (byte)0x90);
        patch[0] = 0xE9;
        BitConverter.TryWriteBytes(
            patch.AsSpan(1), unchecked((int)(target.Value - (Address.Value + JumpLength))));

        return patch;
    }

    /// <summary>What is there now.</summary>
    public SiteState Read(RemoteProcess process, byte[]? ours)
    {
        ArgumentNullException.ThrowIfNull(process);

        var current = new byte[Length];

        if (!process.TryReadBytes(Address, current))
        {
            return SiteState.Foreign;
        }

        return Classify(current, Stock, ours);
    }

    /// <inheritdoc cref="Read"/>
    internal static SiteState Classify(ReadOnlySpan<byte> current, ReadOnlySpan<byte> stock, byte[]? ours) =>
        current.SequenceEqual(stock) ? SiteState.Stock
        : ours is not null && current.SequenceEqual(ours) ? SiteState.Ours
        : SiteState.Foreign;
}
