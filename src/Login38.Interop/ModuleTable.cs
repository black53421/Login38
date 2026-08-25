using System.Runtime.InteropServices;
using Login38.Interop.Win32;

namespace Login38.Interop;

/// <summary>
/// Enumerates the modules loaded in another process.
/// </summary>
internal static class ModuleTable
{
    /// <summary>
    /// <c>ERROR_BAD_LENGTH</c>. Toolhelp returns this while the target's module list is
    /// mid-update, which happens constantly during process start-up.
    /// </summary>
    private const int ErrorBadLength = 24;

    private const int SnapshotRetries = 50;
    private const int SnapshotRetryDelayMs = 20;

    /// <summary>Base address of the named module, or null if it is not loaded.</summary>
    internal static GameAddress? FindBase(uint processId, string moduleName)
    {
        using var snapshot = CreateSnapshotWithRetry(processId);

        var entry = new ModuleEntry32W { Size = (uint)Marshal.SizeOf<ModuleEntry32W>() };
        if (!Kernel32.Module32First(snapshot, ref entry))
        {
            return null;
        }

        do
        {
            if (ReadModuleName(ref entry).Equals(moduleName, StringComparison.OrdinalIgnoreCase))
            {
                return new GameAddress((uint)entry.BaseAddress);
            }
        }
        while (Kernel32.Module32Next(snapshot, ref entry));

        return null;
    }

    /// <summary>
    /// Retries past <c>ERROR_BAD_LENGTH</c>, which is transient rather than fatal:
    /// the module list is being modified while it is being read. Anything else fails
    /// immediately.
    /// </summary>
    private static SafeSnapshotHandle CreateSnapshotWithRetry(uint processId)
    {
        for (var attempt = 0; ; attempt++)
        {
            var snapshot = Kernel32.CreateToolhelp32Snapshot(
                SnapshotFlags.Module | SnapshotFlags.Module32, processId);

            if (!snapshot.IsInvalid)
            {
                return snapshot;
            }

            var error = Marshal.GetLastWin32Error();
            snapshot.Dispose();

            if (error != ErrorBadLength || attempt + 1 >= SnapshotRetries)
            {
                throw new GameProcessException(
                    $"CreateToolhelp32Repeat(modules, pid {processId}) failed.",
                    new System.ComponentModel.Win32Exception(error));
            }

            Thread.Sleep(SnapshotRetryDelayMs);
        }
    }

    private static unsafe string ReadModuleName(ref ModuleEntry32W entry)
    {
        fixed (char* name = entry.ModuleName)
        {
            var span = new ReadOnlySpan<char>(name, ModuleEntry32W.ModuleNameLength);
            var end = span.IndexOf('\0');
            return new string(end < 0 ? span : span[..end]);
        }
    }
}
