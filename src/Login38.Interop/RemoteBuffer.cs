namespace Login38.Interop;

/// <summary>
/// A block of memory allocated inside the game process, released on dispose.
/// </summary>
/// <remarks>
/// For data that is handed to a remote call and finished with afterwards — a DLL path
/// passed to <c>LoadLibraryW</c>, say. Code the game will keep jumping to must use
/// <see cref="RemoteProcess.AllocateExecutable"/> instead, which never frees.
/// </remarks>
public sealed class RemoteBuffer : IDisposable
{
    private readonly RemoteProcess _process;
    private bool _freed;

    internal RemoteBuffer(RemoteProcess process, GameAddress address, int size)
    {
        _process = process;
        Address = address;
        Size = size;
    }

    public GameAddress Address { get; }

    public int Size { get; }

    /// <summary>Copies <paramref name="data"/> into the buffer.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_freed, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, Size);
        _process.WriteBytes(Address, data);
    }

    public void Dispose()
    {
        if (_freed)
        {
            return;
        }

        _freed = true;
        _process.Free(Address);
    }
}
