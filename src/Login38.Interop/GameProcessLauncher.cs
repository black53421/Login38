using System.Runtime.InteropServices;
using Login38.Interop.Win32;
using Microsoft.Win32.SafeHandles;

namespace Login38.Interop;

/// <summary>A game process this launcher started, plus its main thread.</summary>
public sealed class LaunchedGameProcess : IDisposable
{
    private readonly SafeThreadHandle _mainThread;
    private bool _resumed;

    internal LaunchedGameProcess(RemoteProcess process, SafeThreadHandle mainThread, bool startedSuspended)
    {
        Process = process;
        _mainThread = mainThread;
        _resumed = !startedSuspended;
    }

    public RemoteProcess Process { get; }

    public uint Id => Process.Id;

    /// <summary>Whether the process is still waiting to be resumed.</summary>
    public bool IsSuspended => !_resumed;

    /// <summary>
    /// Lets the game start executing. Idempotent, so a caller that resumes explicitly
    /// and then disposes does not double-resume.
    /// </summary>
    public void ResumeMainThread()
    {
        if (_resumed)
        {
            return;
        }

        _resumed = true;
        if (Kernel32.ResumeThread(_mainThread) == uint.MaxValue)
        {
            throw RemoteProcess.Failure("ResumeThread(main)");
        }
    }

    public void Dispose()
    {
        // A process created suspended and never resumed would hang forever holding its
        // window handle; release it rather than leaking a stuck process.
        if (!_resumed)
        {
            Kernel32.ResumeThread(_mainThread);
            _resumed = true;
        }

        _mainThread.Dispose();
        Process.Dispose();
    }
}

/// <summary>Starts the game client.</summary>
public static class GameProcessLauncher
{
    /// <summary>
    /// Creates the game process.
    /// </summary>
    /// <param name="executablePath">Full path to the client executable.</param>
    /// <param name="workingDirectory">Directory the client resolves its data files against.</param>
    /// <param name="arguments">Arguments appended after the executable path, or null.</param>
    /// <param name="suspended">
    /// Start suspended so patches can be applied before a single instruction runs.
    /// The caller must then call <see cref="LaunchedGameProcess.ResumeMainThread"/>.
    /// </param>
    public static unsafe LaunchedGameProcess Launch(
        string executablePath,
        string workingDirectory,
        string? arguments = null,
        bool suspended = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // The executable path is always quoted. The reference only quoted it when
        // arguments were present, which breaks for an install directory containing a
        // space — and these are routinely installed under a path with one.
        var commandLine = string.IsNullOrWhiteSpace(arguments)
            ? $"\"{executablePath}\""
            : $"\"{executablePath}\" {arguments}";

        // CreateProcessW may write into the command line buffer, so it needs a mutable
        // NUL-terminated array rather than a marshalled string temporary.
        var buffer = new char[commandLine.Length + 1];
        commandLine.CopyTo(buffer);
        buffer[^1] = '\0';

        var startupInfo = new StartupInfoW { Size = (uint)Marshal.SizeOf<StartupInfoW>() };

        bool created;
        ProcessInformation info;
        fixed (char* commandLinePointer = buffer)
        {
            created = Kernel32.CreateProcess(
                applicationName: null,
                commandLine: (nint)commandLinePointer,
                processAttributes: 0,
                threadAttributes: 0,
                inheritHandles: false,
                creationFlags: suspended ? ProcessCreationFlags.Suspended : ProcessCreationFlags.None,
                environment: 0,
                currentDirectory: workingDirectory,
                startupInfo: ref startupInfo,
                processInformation: out info);
        }

        if (!created)
        {
            throw RemoteProcess.Failure($"CreateProcess({executablePath})");
        }

        var process = new RemoteProcess(new SafeProcessHandle(info.Process, ownsHandle: true), info.ProcessId);
        return new LaunchedGameProcess(process, new SafeThreadHandle(info.Thread), suspended);
    }
}
