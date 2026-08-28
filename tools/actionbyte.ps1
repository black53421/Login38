# actionbyte.ps1 -- watch entity+0x14 on every live creature.
#
# That byte is what the hunt refuses a burrowed monster on. It is also whatever the creature is
# doing this instant, and the two share one byte — so before refusing a value, look at what
# ordinary monsters in a fight actually carry.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [int]$Samples = 12,
    [int]$DelayMs = 400
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Scan {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")]
    public static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, int len);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION {
        public IntPtr BaseAddress; public IntPtr AllocationBase; public uint AllocationProtect;
        public IntPtr RegionSize; public uint State; public uint Protect; public uint Type;
    }

    public static byte[] Read(IntPtr h, long addr, int size) {
        var buf = new byte[size]; IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), buf, size, out got)) return null;
        return buf;
    }

    // Every dword in committed readable memory equal to the entity vtable.
    public static List<long> FindEntities(IntPtr h, uint vtable, long from, long to) {
        var hits = new List<long>();
        long p = from;
        MEMORY_BASIC_INFORMATION mbi;
        while (p < to && VirtualQueryEx(h, new IntPtr(p), out mbi, Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) != 0) {
            long baseAddr = mbi.BaseAddress.ToInt64();
            long size = mbi.RegionSize.ToInt64();
            bool readable = mbi.State == 0x1000 &&
                (mbi.Protect == 0x04 || mbi.Protect == 0x02 || mbi.Protect == 0x20 || mbi.Protect == 0x40);
            if (readable && size > 0 && size < 64 * 1024 * 1024) {
                var buf = Read(h, baseAddr, (int)size);
                if (buf != null) {
                    for (int i = 0; i + 4 <= buf.Length; i += 4) {
                        if (BitConverter.ToUInt32(buf, i) == vtable) hits.Add(baseAddr + i);
                    }
                }
            }
            p = baseAddr + size;
            if (size == 0) break;
        }
        return hits;
    }
}
'@

$h = [Scan]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }

$entities = [Scan]::FindEntities($h, 0x008DC08C, 0x01000000, 0x7FFF0000)
Write-Host "entity records: $($entities.Count)"

$seen = @{}
for ($s = 0; $s -lt $Samples; $s++) {
    foreach ($addr in $entities) {
        $rec = [Scan]::Read($h, $addr, 0x70)
        if ($null -eq $rec) { continue }
        if ($rec[0x27] -ne 0 -or $rec[0x58] -ne 0) { continue }   # players and corpses out
        $action = $rec[0x14]
        $name = ''
        for ($i = 0x60; $i -lt 0x70 -and $rec[$i] -ne 0; $i++) { $name += [char]$rec[$i] }
        $key = $action
        if ($seen.ContainsKey($key)) { $seen[$key]++ } else { $seen[$key] = 1 }
    }
    Start-Sleep -Milliseconds $DelayMs
}
[Scan]::CloseHandle($h) | Out-Null

Write-Host ''
Write-Host 'entity+0x14 seen on living non-player records:'
$seen.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    '  {0,3} (0x{1:X2})  x{2}' -f $_.Key, $_.Key, $_.Value
}
