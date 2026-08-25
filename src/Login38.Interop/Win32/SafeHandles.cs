using Microsoft.Win32.SafeHandles;

namespace Login38.Interop.Win32;

/// <summary>
/// A handle closed with <c>CloseHandle</c>.
/// </summary>
/// <remarks>
/// The reference implementation paired every <c>OpenProcess</c> / <c>OpenThread</c> /
/// <c>CreateToolhelp32Snapshot</c> with a hand-placed <c>CloseHandle</c>, including on
/// error paths. Deriving from <see cref="SafeHandle"/> makes that structural instead of
/// a discipline, and removes the whole class of leaks that comes from an early return.
/// </remarks>
internal abstract class SafeWin32Handle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected SafeWin32Handle()
        : base(ownsHandle: true)
    {
    }

    protected SafeWin32Handle(nint existingHandle)
        : base(ownsHandle: true) => SetHandle(existingHandle);

    protected override bool ReleaseHandle() => Kernel32.CloseHandle(handle);
}

/// <summary>An open handle to a thread.</summary>
internal sealed class SafeThreadHandle : SafeWin32Handle
{
    public SafeThreadHandle()
    {
    }

    public SafeThreadHandle(nint existingHandle)
        : base(existingHandle)
    {
    }
}

/// <summary>A Toolhelp snapshot.</summary>
internal sealed class SafeSnapshotHandle : SafeWin32Handle
{
    public SafeSnapshotHandle()
    {
    }
}
