using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Login38.Interop.Win32;
using Microsoft.Win32.SafeHandles;

namespace Login38.Interop;

/// <summary>
/// A handle on the running game, with the memory operations the patching layer needs.
/// </summary>
/// <remarks>
/// Everything this launcher does to the game happens from outside it: read, write,
/// allocate, suspend. No managed code is ever injected. Requires the elevated token
/// declared in the application manifest, because <c>PROCESS_ALL_ACCESS</c> against
/// another process needs it.
/// </remarks>
public sealed class RemoteProcess : IDisposable
{
    /// <summary>64 KB per read while scanning: large enough to amortise the syscall.</summary>
    private const int ScanChunkSize = 0x10000;

    private const uint MemMapped = 0x4_0000;

    private const uint MemImage = 0x100_0000;

    /// <summary>Every protection value that permits a read on x86.</summary>
    private const MemoryProtection AnyAccess =
        MemoryProtection.ReadOnly | MemoryProtection.ReadWrite | MemoryProtection.WriteCopy
        | MemoryProtection.Execute | MemoryProtection.ExecuteRead
        | MemoryProtection.ExecuteReadWrite | MemoryProtection.ExecuteWriteCopy;

    private readonly SafeProcessHandle _handle;

    internal RemoteProcess(SafeProcessHandle handle, uint processId)
    {
        _handle = handle;
        Id = processId;
    }

    /// <summary>Opens an already running process.</summary>
    /// <exception cref="GameProcessException">The process could not be opened.</exception>
    public static RemoteProcess Open(uint processId)
    {
        var handle = Kernel32.OpenProcess(ProcessAccess.AllAccess, false, processId);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw Failure($"OpenProcess({processId})");
        }

        return new RemoteProcess(handle, processId);
    }

    public uint Id { get; }

    internal SafeProcessHandle Handle => _handle;

    /// <summary>
    /// Whether the process is still alive. A process handle becomes signalled when the
    /// process exits, so a zero-timeout wait that times out means it is still running.
    /// </summary>
    public bool IsRunning => Kernel32.WaitForProcess(_handle, 0) == WaitResult.Timeout;

    /// <summary>Waits for the process to exit.</summary>
    /// <remarks>
    /// A wait rather than a poll. A process handle is signalled the moment the process ends,
    /// so the thread pool can hand this back at that instant; the reference polled, and so
    /// did the first version of this. Quarter of a second of a launcher that has not noticed
    /// yet does not sound like much, and it sits on the one transition the player is looking
    /// straight at — the game closing.
    /// </remarks>
    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsRunning)
        {
            return;
        }

        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Borrowed rather than owned, and counted: the wait must not close the handle the
        // rest of this class reads through, and the handle must not go away underneath a
        // registered wait.
        var borrowed = false;
        _handle.DangerousAddRef(ref borrowed);

        try
        {
            using var waitable = new BorrowedHandle(_handle);

            var registered = ThreadPool.RegisterWaitForSingleObject(
                waitable,
                static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
                exited,
                Timeout.Infinite,
                executeOnlyOnce: true);

            try
            {
                await exited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                registered.Unregister(null);
            }
        }
        finally
        {
            if (borrowed)
            {
                _handle.DangerousRelease();
            }
        }
    }

    /// <summary>A wait handle over a process handle this class already holds.</summary>
    /// <remarks>
    /// <see cref="WaitHandle"/> is the only thing the thread pool waits on, and the
    /// framework offers no public one for a process opened by handle. It owns nothing:
    /// closing it would take away the handle every read here goes through.
    /// </remarks>
    private sealed class BorrowedHandle : WaitHandle
    {
        internal BorrowedHandle(SafeProcessHandle process) =>
            SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
    }

    // ---- reading -------------------------------------------------------------------

    /// <summary>Reads a value.</summary>
    /// <exception cref="GameProcessException">The read failed or was short.</exception>
    public unsafe T Read<T>(GameAddress address)
        where T : unmanaged
    {
        T value;
        var span = new Span<byte>(&value, sizeof(T));
        ReadBytes(address, span);
        return value;
    }

    /// <summary>
    /// Reads a value, returning false instead of throwing. For polling loops where an
    /// unmapped address is an expected state rather than an error.
    /// </summary>
    public unsafe bool TryRead<T>(GameAddress address, out T value)
        where T : unmanaged
    {
        Unsafe.SkipInit(out value);
        fixed (T* target = &value)
        {
            return TryReadBytes(address, new Span<byte>(target, sizeof(T)));
        }
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes.</summary>
    public byte[] ReadBytes(GameAddress address, int count)
    {
        var buffer = new byte[count];
        ReadBytes(address, buffer);
        return buffer;
    }

    /// <summary>Fills <paramref name="destination"/> completely.</summary>
    /// <exception cref="GameProcessException">The read failed or was short.</exception>
    public void ReadBytes(GameAddress address, Span<byte> destination)
    {
        if (!TryReadBytes(address, destination, out var bytesRead))
        {
            throw Failure($"ReadProcessMemory({address}, {destination.Length} bytes)");
        }

        if (bytesRead != destination.Length)
        {
            throw new GameProcessException(
                $"Short read at {address}: expected {destination.Length} bytes, got {bytesRead}.");
        }
    }

    /// <summary>Fills <paramref name="destination"/>, returning false on any failure.</summary>
    public bool TryReadBytes(GameAddress address, Span<byte> destination) =>
        TryReadBytes(address, destination, out var read) && read == destination.Length;

    private unsafe bool TryReadBytes(GameAddress address, Span<byte> destination, out int bytesRead)
    {
        fixed (byte* buffer = destination)
        {
            var ok = Kernel32.ReadProcessMemory(
                _handle, address.ToPointer(), buffer, destination.Length, out var read);
            bytesRead = (int)read;
            return ok;
        }
    }

    // ---- writing -------------------------------------------------------------------

    /// <summary>Writes a value into already-writable memory.</summary>
    public unsafe void Write<T>(GameAddress address, in T value)
        where T : unmanaged
    {
        fixed (T* source = &value)
        {
            WriteBytes(address, new ReadOnlySpan<byte>(source, sizeof(T)));
        }
    }

    /// <summary>Writes into already-writable memory.</summary>
    /// <exception cref="GameProcessException">The write failed or was short.</exception>
    public unsafe void WriteBytes(GameAddress address, ReadOnlySpan<byte> data)
    {
        fixed (byte* buffer = data)
        {
            if (!Kernel32.WriteProcessMemory(_handle, address.ToPointer(), buffer, data.Length, out var written))
            {
                throw Failure($"WriteProcessMemory({address}, {data.Length} bytes)");
            }

            if ((int)written != data.Length)
            {
                throw new GameProcessException(
                    $"Short write at {address}: expected {data.Length} bytes, wrote {written}.");
            }
        }
    }

    /// <summary>
    /// Writes over executable memory: makes the page writable, writes, restores the
    /// original protection, then flushes the instruction cache.
    /// </summary>
    /// <remarks>
    /// The flush is not optional. Without it the CPU can keep executing the pre-patch
    /// bytes from its instruction cache, which shows up as a patch that "sometimes does
    /// not take" — the worst kind of bug to chase in a live game.
    /// </remarks>
    public void WriteCode(GameAddress address, ReadOnlySpan<byte> data)
    {
        if (!Kernel32.VirtualProtectEx(
                _handle, address.ToPointer(), data.Length, MemoryProtection.ExecuteReadWrite, out var original))
        {
            throw Failure($"VirtualProtectEx({address}, {data.Length} bytes)");
        }

        try
        {
            WriteBytes(address, data);
        }
        finally
        {
            // Restoring protection must happen even if the write threw, or the page is
            // left writable for the rest of the session.
            Kernel32.VirtualProtectEx(_handle, address.ToPointer(), data.Length, original, out _);
        }

        if (!Kernel32.FlushInstructionCache(_handle, address.ToPointer(), data.Length))
        {
            throw Failure($"FlushInstructionCache({address}, {data.Length} bytes)");
        }
    }

    // ---- allocation ----------------------------------------------------------------

    /// <summary>
    /// Allocates executable memory that is never freed.
    /// </summary>
    /// <remarks>
    /// Deliberately permanent. Shellcode and hook trampolines are jumped to by patched
    /// game code for the rest of the session; freeing them would leave a jump into
    /// unmapped memory. The leak is bounded — a few pages per session — and the process
    /// reclaims it on exit. Use <see cref="AllocateScratch"/> for anything temporary.
    /// </remarks>
    public GameAddress AllocateExecutable(int size)
    {
        var address = Kernel32.VirtualAllocEx(
            _handle, 0, size, AllocationType.Commit | AllocationType.Reserve,
            MemoryProtection.ExecuteReadWrite);

        return address == 0
            ? throw Failure($"VirtualAllocEx({size} bytes, executable)")
            : new GameAddress((uint)address);
    }

    /// <summary>
    /// Allocates readable/writable memory that is never freed, for data patched code reads.
    /// </summary>
    /// <remarks>
    /// Permanent for the same reason <see cref="AllocateExecutable"/> is — a table a
    /// detour consults is read for as long as the detour is in the client, and there is no
    /// moment at which the last thread is known to have left. Separate from it because a
    /// table of addresses is not code and has no business being executable.
    /// </remarks>
    public GameAddress AllocateData(int size)
    {
        var address = Kernel32.VirtualAllocEx(
            _handle, 0, size, AllocationType.Commit | AllocationType.Reserve, MemoryProtection.ReadWrite);

        return address == 0
            ? throw Failure($"VirtualAllocEx({size} bytes, data)")
            : new GameAddress((uint)address);
    }

    /// <summary>
    /// Allocates readable/writable memory that is freed on dispose. For data handed to
    /// a remote call and not needed afterwards, such as a DLL path.
    /// </summary>
    public RemoteBuffer AllocateScratch(int size)
    {
        var address = Kernel32.VirtualAllocEx(
            _handle, 0, size, AllocationType.Commit | AllocationType.Reserve, MemoryProtection.ReadWrite);

        return address == 0
            ? throw Failure($"VirtualAllocEx({size} bytes, scratch)")
            : new RemoteBuffer(this, new GameAddress((uint)address), size);
    }

    internal void Free(GameAddress address)
    {
        // MEM_RELEASE requires a size of zero.
        Kernel32.VirtualFreeEx(_handle, address.ToPointer(), 0, AllocationType.Release);
    }

    // ---- scanning ------------------------------------------------------------------

    /// <summary>
    /// Finds the first address in [<paramref name="start"/>, <paramref name="end"/>)
    /// matching <paramref name="pattern"/>, or null.
    /// </summary>
    /// <remarks>
    /// Unreadable regions are skipped rather than treated as failures: most of a
    /// process's address space is unmapped, and hitting a hole is the normal case.
    /// </remarks>
    public GameAddress? Scan(BytePattern pattern, GameAddress start, GameAddress end)
    {
        foreach (var hit in ScanCore(pattern, start, end))
        {
            return hit;
        }

        return null;
    }

    private IEnumerable<GameAddress> ScanCore(BytePattern pattern, GameAddress start, GameAddress end)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        // Chunks overlap by the pattern length so a match straddling a boundary is
        // still found.
        var overlap = pattern.Length - 1;
        var buffer = new byte[ScanChunkSize + overlap];

        for (var address = start; address < end;)
        {
            // Widened to long: a range near the top of the address space overflows a
            // signed 32-bit subtraction and would produce a negative length.
            var remaining = (long)end.Value - address.Value;
            var take = (int)Math.Min(buffer.Length, remaining);
            var window = buffer.AsMemory(0, take);

            if (TryReadBytes(address, window.Span))
            {
                var searched = 0;
                while (true)
                {
                    var hit = pattern.IndexIn(window.Span[searched..]);
                    if (hit < 0)
                    {
                        break;
                    }

                    yield return address + (searched + hit);
                    searched += hit + 1;
                    if (searched > take - pattern.Length)
                    {
                        break;
                    }
                }
            }

            var next = address + ScanChunkSize;
            if (next <= address)
            {
                // Wrapped past 0xFFFFFFFF; there is nothing above this.
                yield break;
            }

            address = next;
        }
    }

    // ---- layout --------------------------------------------------------------------

    /// <summary>
    /// Walks the committed, readable runs of pages between two addresses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For searching the game's heap, where most of a 32-bit address space is not mapped
    /// at all. Asking the kernel where the holes are steps over gigabytes in a handful of
    /// calls; reading blind would be tens of thousands of failing syscalls.
    /// </para>
    /// <para>
    /// Guard pages are left out. They read as ordinary memory but touching one is how a
    /// thread's stack grows, and the run they belong to is a stack rather than anything
    /// worth searching.
    /// </para>
    /// </remarks>
    public IEnumerable<MemoryRegion> Regions(GameAddress start, GameAddress end)
    {
        var size = (nint)Unsafe.SizeOf<MemoryBasicInformation>();

        for (var address = start; address < end;)
        {
            if (Kernel32.VirtualQueryEx(_handle, (nint)address.Value, out var info, size) == 0)
            {
                // Past the top of the address space, or a range this handle cannot see.
                yield break;
            }

            var regionBase = (uint)info.BaseAddress;
            var regionSize = (uint)info.RegionSize;

            // A zero-length region would leave the walk where it started. The kernel does
            // not return one, but the loop must not depend on that.
            if (regionSize == 0)
            {
                yield break;
            }

            if (info.State == MemoryState.Commit && IsReadable(info.Protect))
            {
                // The first query can land inside a region rather than on its start, and
                // the last can run past the caller's end.
                var from = GameAddress.Max(new GameAddress(regionBase), start);
                var to = GameAddress.Min(new GameAddress(regionBase) + regionSize, end);

                if (from < to)
                {
                    yield return new MemoryRegion(from, (uint)(to.Value - from.Value), KindOf(info.Type));
                }
            }

            var next = (ulong)regionBase + regionSize;
            if (next > uint.MaxValue)
            {
                yield break;
            }

            address = new GameAddress((uint)next);
        }
    }

    /// <summary>
    /// Whether a run of pages can be read at all.
    /// </summary>
    /// <remarks>
    /// The access values are distinct bits rather than a set of flags, so this is one
    /// mask rather than a list of comparisons. Only <c>Guard</c> and <c>NoCache</c> are
    /// modifiers layered on top, and a guard page disqualifies the run.
    /// </remarks>
    private static MemoryKind KindOf(uint type) => type switch
    {
        MemImage => MemoryKind.Image,
        MemMapped => MemoryKind.Mapped,
        _ => MemoryKind.Private,
    };

    private static bool IsReadable(MemoryProtection protect) =>
        (protect & (MemoryProtection.Guard | MemoryProtection.NoAccess)) == 0
        && (protect & AnyAccess) != 0;

    // ---- threads -------------------------------------------------------------------

    /// <summary>
    /// Suspends every thread in the process until the returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// Needed around any patch that rewrites code the game might be executing right
    /// now. Returning a disposable rather than a suspend/resume pair means the resume
    /// cannot be missed on an error path.
    /// </remarks>
    public ThreadSuspension SuspendThreads() => ThreadSuspension.Create(Id);

    // ---- modules -------------------------------------------------------------------

    /// <summary>Base address of a loaded module, or null if it is not loaded yet.</summary>
    public GameAddress? FindModule(string moduleName) => ModuleTable.FindBase(Id, moduleName);

    /// <summary>
    /// Waits for a module to appear, polling. DLLs the game loads lazily — winsock,
    /// most importantly — are not present when the process is first created.
    /// </summary>
    public async Task<GameAddress?> WaitForModuleAsync(
        string moduleName,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (true)
        {
            if (FindModule(moduleName) is { } found)
            {
                return found;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return null;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves an export by walking the module's PE headers in the target process.
    /// </summary>
    /// <remarks>
    /// Read from the target rather than resolved locally: under WOW64 a 64-bit caller's
    /// copy of a system DLL sits at a different base than the 32-bit target's. This
    /// build is x86 so the two usually agree, but relying on that is exactly the
    /// assumption that breaks silently.
    /// </remarks>
    public GameAddress? FindExport(GameAddress moduleBase, string exportName) =>
        PeExportTable.Find(this, moduleBase, exportName);

    // ---- plumbing ------------------------------------------------------------------

    internal static GameProcessException Failure(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return new GameProcessException($"{operation} failed.", new Win32Exception(error));
    }

    public void Dispose() => _handle.Dispose();
}
