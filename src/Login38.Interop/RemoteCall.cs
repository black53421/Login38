using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>
/// Runs a short piece of code inside the game and waits for it to finish.
/// </summary>
/// <remarks>
/// <para>
/// For the cases where the launcher needs the client to do something to itself — recompute
/// a cached palette, rebuild a table — that only the client's own routines know how to do.
/// Everything else this launcher does is reads and writes from outside.
/// </para>
/// <para>
/// The game's other threads keep running. Suspending them first is the obvious instinct
/// and the wrong one: the code being called is the client's, and a client routine that
/// takes a lock a suspended thread is holding deadlocks the whole process. So what is run
/// this way has to be something the client itself calls from more than one thread.
/// </para>
/// </remarks>
public static class RemoteCall
{
    /// <summary>
    /// Writes <paramref name="code"/> into the game, runs it on a thread of its own and
    /// waits for it to return.
    /// </summary>
    /// <returns>The thread's exit code, which is whatever the code left in <c>eax</c>.</returns>
    /// <exception cref="GameProcessException">
    /// The code could not be written or started, or did not return within
    /// <paramref name="timeout"/>.
    /// </exception>
    public static uint Run(RemoteProcess process, ReadOnlySpan<byte> code, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (code.IsEmpty)
        {
            throw new ArgumentException("There is no code to run.", nameof(code));
        }

        var page = process.AllocateExecutable(code.Length);
        var release = true;

        try
        {
            process.WriteBytes(page, code);

            using var thread = Kernel32.CreateRemoteThread(
                process.Handle, 0, 0, page.ToPointer(), 0, 0, out var threadId);

            if (thread.IsInvalid)
            {
                throw RemoteProcess.Failure($"CreateRemoteThread({page})");
            }

            var wait = Kernel32.WaitForSingleObject(thread, (uint)timeout.TotalMilliseconds);

            if (wait == WaitResult.Timeout)
            {
                // The thread is still somewhere in this page. Freeing it now would unmap
                // the code out from under it, which is a crash of the game rather than a
                // failure of the launcher. A leaked page is the cheaper mistake.
                release = false;

                throw new GameProcessException(
                    $"Code at {page} did not return within {timeout.TotalSeconds:0}s (thread {threadId}); " +
                    "its page has been left mapped.");
            }

            if (wait != WaitResult.Object0)
            {
                throw new GameProcessException($"Waiting on thread {threadId} at {page} gave {wait}.");
            }

            return Kernel32.GetExitCodeThread(thread, out var exitCode)
                ? exitCode
                : throw RemoteProcess.Failure($"GetExitCodeThread({threadId})");
        }
        finally
        {
            if (release)
            {
                process.Free(page);
            }
        }
    }
}
