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

            return RunAt(process, page, timeout);
        }
        catch (RemoteCodeStillRunningException e)
        {
            // The thread is still somewhere in this page. Freeing it now would unmap the
            // code out from under it, which is a crash of the game rather than a failure of
            // the launcher. A leaked page is the cheaper mistake.
            release = false;

            throw new RemoteCodeStillRunningException(
                $"{e.Message} Its page has been left mapped.");
        }
        finally
        {
            if (release)
            {
                process.Free(page);
            }
        }
    }

    /// <summary>
    /// Runs code that is already in the game on a thread of its own and waits for it.
    /// </summary>
    /// <returns>The thread's exit code, which is whatever the code left in <c>eax</c>.</returns>
    /// <remarks>
    /// For a page the caller keeps, which is what anything asked more than once wants: the
    /// allocation and the writing of the code are most of the cost of <see cref="Run"/>, and
    /// repeating them per question would swamp the question.
    /// </remarks>
    /// <exception cref="RemoteCodeStillRunningException">It did not return in time.</exception>
    /// <exception cref="GameProcessException">The thread could not be started or waited on.</exception>
    public static uint RunAt(RemoteProcess process, GameAddress entry, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(process);

        using var thread = Kernel32.CreateRemoteThread(
            process.Handle, 0, 0, entry.ToPointer(), 0, 0, out var threadId);

        if (thread.IsInvalid)
        {
            throw RemoteProcess.Failure($"CreateRemoteThread({entry})");
        }

        var wait = Kernel32.WaitForSingleObject(thread, (uint)timeout.TotalMilliseconds);

        if (wait == WaitResult.Timeout)
        {
            throw new RemoteCodeStillRunningException(
                $"Code at {entry} did not return within {timeout.TotalSeconds:0}s (thread {threadId}).");
        }

        if (wait != WaitResult.Object0)
        {
            throw new GameProcessException($"Waiting on thread {threadId} at {entry} gave {wait}.");
        }

        return Kernel32.GetExitCodeThread(thread, out var exitCode)
            ? exitCode
            : throw RemoteProcess.Failure($"GetExitCodeThread({threadId})");
    }
}

/// <summary>
/// Code run inside the game had not returned when the wait ran out.
/// </summary>
/// <remarks>
/// Its own type because the only safe response is different from every other failure: the
/// thread is still inside that page, so the page cannot be freed, and whatever the page
/// holds cannot be written to again. Callers that keep a page have to retire it.
/// </remarks>
public sealed class RemoteCodeStillRunningException(string message) : GameProcessException(message);
