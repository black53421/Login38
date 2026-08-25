namespace Login38.Interop.Win32;

[Flags]
internal enum ProcessAccess : uint
{
    Terminate = 0x0001,
    CreateThread = 0x0002,
    VmOperation = 0x0008,
    VmRead = 0x0010,
    VmWrite = 0x0020,
    DuplicateHandle = 0x0040,
    CreateProcess = 0x0080,
    SetQuota = 0x0100,
    SetInformation = 0x0200,
    QueryInformation = 0x0400,
    SuspendResume = 0x0800,
    QueryLimitedInformation = 0x1000,
    Synchronize = 0x0010_0000,

    /// <summary>
    /// What the patching pipeline needs. Requires the elevated token declared in the
    /// application manifest.
    /// </summary>
    AllAccess = 0x001F_0FFF,
}

[Flags]
internal enum ThreadAccess : uint
{
    SuspendResume = 0x0002,
    QueryInformation = 0x0040,
}

[Flags]
internal enum AllocationType : uint
{
    Commit = 0x1000,
    Reserve = 0x2000,
    Release = 0x8000,
}

[Flags]
internal enum MemoryProtection : uint
{
    NoAccess = 0x01,
    ReadOnly = 0x02,
    ReadWrite = 0x04,
    WriteCopy = 0x08,
    Execute = 0x10,
    ExecuteRead = 0x20,
    ExecuteReadWrite = 0x40,
    ExecuteWriteCopy = 0x80,
    Guard = 0x100,
    NoCache = 0x200,
}

[Flags]
internal enum SnapshotFlags : uint
{
    Thread = 0x0000_0004,
    Module = 0x0000_0008,

    /// <summary>
    /// Required to see a 32-bit process's modules. Without it, enumerating a WOW64
    /// target from a 64-bit caller returns nothing — not a concern for this x86 build,
    /// but harmless and correct to pass.
    /// </summary>
    Module32 = 0x0000_0010,
}

[Flags]
internal enum ProcessCreationFlags : uint
{
    None = 0,
    Suspended = 0x0000_0004,
}

internal enum WaitResult : uint
{
    Object0 = 0x0000_0000,
    Timeout = 0x0000_0102,
    Abandoned = 0x0000_0080,
    Failed = 0xFFFF_FFFF,
}

internal enum MemoryState : uint
{
    Commit = 0x1000,
    Reserve = 0x2000,
    Free = 0x10000,
}
