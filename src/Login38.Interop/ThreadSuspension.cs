using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>
/// Holds every thread of a process suspended for the lifetime of the scope.
/// </summary>
/// <remarks>
/// <para>
/// Required around any patch that rewrites bytes the game might be executing. Writing
/// a five-byte jump over an instruction boundary while a thread is mid-instruction
/// crashes the client.
/// </para>
/// <para>
/// Keep the scope as short as possible and never do I/O inside it: the game's network
/// threads are stopped too, and a long pause drops the connection.
/// </para>
/// </remarks>
public sealed class ThreadSuspension : IDisposable
{
    private readonly List<SafeThreadHandle> _threads;
    private bool _resumed;

    private ThreadSuspension(List<SafeThreadHandle> threads) => _threads = threads;

    /// <summary>How many threads were successfully suspended.</summary>
    public int Count => _threads.Count;

    internal static ThreadSuspension Create(uint processId)
    {
        var threads = new List<SafeThreadHandle>();

        // TH32CS_SNAPTHREAD ignores the pid argument and always snapshots every thread
        // on the system, so entries have to be filtered by owner.
        using var snapshot = Kernel32.CreateToolhelp32Snapshot(SnapshotFlags.Thread, 0);
        if (snapshot.IsInvalid)
        {
            throw RemoteProcess.Failure("CreateToolhelp32Snapshot(threads)");
        }

        var entry = new ThreadEntry32 { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ThreadEntry32>() };
        if (Kernel32.Thread32First(snapshot, ref entry))
        {
            do
            {
                if (entry.OwnerProcessId != processId)
                {
                    continue;
                }

                var thread = Kernel32.OpenThread(ThreadAccess.SuspendResume, false, entry.ThreadId);
                if (thread.IsInvalid)
                {
                    // A thread that exited between the snapshot and here is normal.
                    thread.Dispose();
                    continue;
                }

                if (Kernel32.SuspendThread(thread) == uint.MaxValue)
                {
                    thread.Dispose();
                    continue;
                }

                threads.Add(thread);
            }
            while (Kernel32.Thread32Next(snapshot, ref entry));
        }

        return new ThreadSuspension(threads);
    }

    public void Dispose()
    {
        if (_resumed)
        {
            return;
        }

        _resumed = true;
        foreach (var thread in _threads)
        {
            Kernel32.ResumeThread(thread);
            thread.Dispose();
        }

        _threads.Clear();
    }
}
