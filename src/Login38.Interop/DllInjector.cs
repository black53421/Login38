using System.Text;
using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>
/// Loads a native DLL into the game process.
/// </summary>
/// <remarks>
/// <para>
/// Used for exactly one thing: <c>l38ddraw.dll</c>, the C++ DirectDraw present hook
/// that has to live inside the game's render loop. Everything else this launcher does
/// is out-of-process.
/// </para>
/// <para>
/// The classic <c>VirtualAllocEx</c> + <c>WriteProcessMemory</c> + <c>CreateRemoteThread</c>
/// sequence, with the DLL path as the thread parameter and <c>LoadLibraryW</c> as the
/// start address.
/// </para>
/// </remarks>
public static class DllInjector
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Injects <paramref name="dllPath"/> and waits for it to load.
    /// </summary>
    /// <returns>The module handle the target received.</returns>
    /// <exception cref="GameProcessException">Injection failed or timed out.</exception>
    public static GameAddress Inject(RemoteProcess process, string dllPath)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);

        if (!File.Exists(dllPath))
        {
            throw new GameProcessException($"DLL to inject does not exist: {dllPath}");
        }

        var loadLibrary = ResolveLoadLibraryW(process);

        // UTF-16 with an explicit NUL: LoadLibraryW reads until the terminator.
        var pathBytes = Encoding.Unicode.GetBytes(dllPath + '\0');

        using var remotePath = process.AllocateScratch(pathBytes.Length);
        remotePath.Write(pathBytes);

        using var thread = Kernel32.CreateRemoteThread(
            process.Handle, 0, 0, loadLibrary.ToPointer(), remotePath.Address.ToPointer(), 0, out _);

        if (thread.IsInvalid)
        {
            throw RemoteProcess.Failure($"CreateRemoteThread(LoadLibraryW, {dllPath})");
        }

        var wait = Kernel32.WaitForSingleObject(thread, (uint)LoadTimeout.TotalMilliseconds);
        if (wait != WaitResult.Object0)
        {
            throw new GameProcessException(
                $"LoadLibraryW did not return within {LoadTimeout.TotalSeconds:0}s for {dllPath} (wait result {wait}).");
        }

        if (!Kernel32.GetExitCodeThread(thread, out var moduleHandle))
        {
            throw RemoteProcess.Failure("GetExitCodeThread(LoadLibraryW)");
        }

        // LoadLibraryW returns the module handle, or null on failure. The thread exit
        // code is only 32 bits wide, which is exactly right for a 32-bit target.
        return moduleHandle == 0
            ? throw new GameProcessException(
                $"LoadLibraryW returned null for {dllPath}; the target refused to load it.")
            : new GameAddress(moduleHandle);
    }

    /// <summary>
    /// Finds <c>LoadLibraryW</c> inside the <em>target</em> process.
    /// </summary>
    /// <remarks>
    /// The reference resolved it in its own process with <c>GetProcAddress</c> and
    /// passed that address to the target. That only works because both processes are
    /// 32-bit and Windows maps kernel32 at the same base in every process of a given
    /// bitness for a given boot — true today, but an unstated assumption that fails
    /// silently by jumping to the wrong address. Reading the target's own export table
    /// costs one extra module lookup and removes the assumption.
    /// </remarks>
    private static GameAddress ResolveLoadLibraryW(RemoteProcess process)
    {
        var kernel32 = process.FindModule("kernel32.dll")
                       ?? throw new GameProcessException("kernel32.dll is not loaded in the target process.");

        return process.FindExport(kernel32, "LoadLibraryW")
               ?? throw new GameProcessException("kernel32.dll in the target exports no LoadLibraryW.");
    }
}
