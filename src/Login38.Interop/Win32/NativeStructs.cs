using System.Runtime.InteropServices;

namespace Login38.Interop.Win32;

/// <summary>Layout must match the Win32 headers exactly; sizes below are for x86.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoW
{
    public uint Size;
    public nint Reserved;
    public nint Desktop;
    public nint Title;
    public uint X;
    public uint Y;
    public uint XSize;
    public uint YSize;
    public uint XCountChars;
    public uint YCountChars;
    public uint FillAttribute;
    public uint Flags;
    public ushort ShowWindow;
    public ushort Reserved2Size;
    public nint Reserved2;
    public nint StdInput;
    public nint StdOutput;
    public nint StdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public nint Process;
    public nint Thread;
    public uint ProcessId;
    public uint ThreadId;
}

/// <summary>
/// The wide variant. The reference used the ANSI one, which mangles module paths
/// containing non-ASCII characters — and this game is routinely installed under a
/// Chinese directory name.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct ModuleEntry32W
{
    /// <summary>MAX_MODULE_NAME32 + 1.</summary>
    public const int ModuleNameLength = 256;

    public const int ExePathLength = 260;

    public uint Size;
    public uint ModuleId;
    public uint ProcessId;
    public uint GlobalUsageCount;
    public uint ProcessUsageCount;
    public nint BaseAddress;
    public uint BaseSize;
    public nint Module;
    public fixed char ModuleName[ModuleNameLength];
    public fixed char ExePath[ExePathLength];
}

[StructLayout(LayoutKind.Sequential)]
internal struct ThreadEntry32
{
    public uint Size;
    public uint UsageCount;
    public uint ThreadId;
    public uint OwnerProcessId;
    public int BasePriority;
    public int DeltaPriority;
    public uint Flags;
}

/// <summary>
/// What <c>VirtualQueryEx</c> says about the run of pages containing an address.
/// </summary>
/// <remarks>
/// 28 bytes on x86. The pointer-sized fields are <c>nint</c> rather than <c>uint</c> so
/// the layout is right whatever this process is built for, even though the game is
/// always 32-bit.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct MemoryBasicInformation
{
    public nint BaseAddress;
    public nint AllocationBase;
    public MemoryProtection AllocationProtect;
    public nint RegionSize;
    public MemoryState State;
    public MemoryProtection Protect;
    public uint Type;
}

/// <summary>What a low-level keyboard hook is handed for each keystroke.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardHookData
{
    public uint VirtualKey;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

/// <summary>A window message, as the pump sees it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Message
{
    public nint Window;
    public uint Id;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public int X;
    public int Y;
}

/// <summary>A Win32 <c>RECT</c>: two corners rather than a corner and a size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Rectangle
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

/// <summary>A Win32 <c>POINT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    public int X;
    public int Y;
}
