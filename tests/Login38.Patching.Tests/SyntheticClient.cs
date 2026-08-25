using Login38.Core.Servers;
using Login38.Interop;

namespace Login38.Patching.Tests;

/// <summary>
/// A page of real memory in this process, standing in for the game's image.
/// </summary>
/// <remarks>
/// <para>
/// The patches are worth testing at the level they actually operate: find a shape in a
/// live process, change bytes in it, read them back. A mocked memory interface would
/// prove the patch calls the methods it calls, which is the part that was never in
/// doubt — whereas this catches a wrong offset, a signature that does not match the
/// instructions it describes, and an edit that lands one byte off.
/// </para>
/// <para>
/// The launcher is x86 and <see cref="GameAddress"/> is 32-bit to match, so these are
/// skipped rather than made to lie when the test host is 64-bit.
/// </para>
/// </remarks>
internal sealed class SyntheticClient : IDisposable
{
    private readonly RemoteBuffer _region;

    private SyntheticClient(RemoteProcess process, RemoteBuffer region)
    {
        Process = process;
        _region = region;
    }

    /// <summary>Whether these tests can run at all on this host.</summary>
    public static bool Supported => IntPtr.Size == 4;

    public RemoteProcess Process { get; }

    /// <summary>Where the stand-in image begins.</summary>
    public GameAddress Base => _region.Address;

    public static SyntheticClient Create(int size = 0x1000)
    {
        var process = RemoteProcess.Open((uint)Environment.ProcessId);

        try
        {
            return new SyntheticClient(process, process.AllocateScratch(size));
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>Places a run of bytes at a chosen offset, as if the compiler had.</summary>
    public GameAddress Place(int offset, ReadOnlySpan<byte> code)
    {
        var address = Base + offset;
        Process.WriteBytes(address, code);
        return address;
    }

    public byte[] Read(int offset, int count) => Process.ReadBytes(Base + offset, count);

    public byte ReadByte(int offset) => Process.Read<byte>(Base + offset);

    /// <summary>
    /// A context scoped to this region. Fresh each call, because the image it captures is
    /// cached for the life of the context.
    /// </summary>
    public GamePatchContext NewContext(AuxConfig? aux = null) =>
        new(Process,
            AppContext.BaseDirectory,
            aux ?? new AuxConfig(),
            new ServerInfo("test", "127.0.0.1", 2000),
            Base,
            Base + _region.Size);

    public void Dispose()
    {
        _region.Dispose();
        Process.Dispose();
    }
}
