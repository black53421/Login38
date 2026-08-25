using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Login38.Interop.Win32;

/// <summary>
/// The kernel32 surface this launcher needs.
/// </summary>
/// <remarks>
/// <see cref="LibraryImportAttribute"/> rather than <c>DllImport</c>: marshalling code
/// is generated at compile time, so there is no runtime IL stub and the signatures are
/// checked by an analyzer.
/// </remarks>
internal static partial class Kernel32
{
    private const string Library = "kernel32.dll";

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess(
        ProcessAccess desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial SafeThreadHandle OpenThread(
        ThreadAccess desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint threadId);

    /// <summary>
    /// <paramref name="commandLine"/> is a raw pointer on purpose: CreateProcessW may
    /// write into the buffer it is given, so it has to be a pinned mutable array rather
    /// than a marshalled temporary.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "CreateProcessW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(
        string? applicationName,
        nint commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        ProcessCreationFlags creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfoW startupInfo,
        out ProcessInformation processInformation);

    /// <summary>Returns the previous suspend count, or <c>0xFFFFFFFF</c> on failure.</summary>
    [LibraryImport(Library, SetLastError = true)]
    internal static partial uint ResumeThread(SafeThreadHandle thread);

    /// <inheritdoc cref="ResumeThread"/>
    [LibraryImport(Library, SetLastError = true)]
    internal static partial uint SuspendThread(SafeThreadHandle thread);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial SafeSnapshotHandle CreateToolhelp32Snapshot(SnapshotFlags flags, uint processId);

    [LibraryImport(Library, EntryPoint = "Module32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Module32First(SafeSnapshotHandle snapshot, ref ModuleEntry32W entry);

    [LibraryImport(Library, EntryPoint = "Module32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Module32Next(SafeSnapshotHandle snapshot, ref ModuleEntry32W entry);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Thread32First(SafeSnapshotHandle snapshot, ref ThreadEntry32 entry);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Thread32Next(SafeSnapshotHandle snapshot, ref ThreadEntry32 entry);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool ReadProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        void* buffer,
        nint size,
        out nint bytesRead);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool WriteProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        void* buffer,
        nint size,
        out nint bytesWritten);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial nint VirtualAllocEx(
        SafeProcessHandle process,
        nint address,
        nint size,
        AllocationType allocationType,
        MemoryProtection protect);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualFreeEx(
        SafeProcessHandle process,
        nint address,
        nint size,
        AllocationType freeType);

    [LibraryImport(Library)]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport(Library, SetLastError = true)]
    internal static partial nint VirtualQueryEx(
        SafeProcessHandle process,
        nint address,
        out MemoryBasicInformation information,
        nint length);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualProtectEx(
        SafeProcessHandle process,
        nint address,
        nint size,
        MemoryProtection newProtect,
        out MemoryProtection oldProtect);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlushInstructionCache(SafeProcessHandle process, nint baseAddress, nint size);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial SafeThreadHandle CreateRemoteThread(
        SafeProcessHandle process,
        nint threadAttributes,
        nint stackSize,
        nint startAddress,
        nint parameter,
        uint creationFlags,
        out uint threadId);

    [LibraryImport(Library, SetLastError = true)]
    internal static partial WaitResult WaitForSingleObject(SafeThreadHandle handle, uint milliseconds);

    [LibraryImport(Library, EntryPoint = "WaitForSingleObject", SetLastError = true)]
    internal static partial WaitResult WaitForProcess(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeThread(SafeThreadHandle thread, out uint exitCode);

    [LibraryImport(Library, EntryPoint = "GetModuleHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string moduleName);

    [LibraryImport(Library, EntryPoint = "GetProcAddress", SetLastError = true,
        StringMarshalling = StringMarshalling.Custom,
        StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    internal static partial nint GetProcAddress(nint module, string procName);
}
