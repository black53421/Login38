<#
.SYNOPSIS
Narrows down which byte of a monster's record holds its health.

.DESCRIPTION
The hunt has no way to tell "my attacks are landing" from "the server is throwing every one
of them away". Both look identical from inside the client: it starts the blow, plays the
animation, pushes its own cooldown forward, and waits. The server drops what it will not
allow - no line of sight round a corner, a monster still burrowed - and says nothing.

Health is the signal that tells them apart, and the client must hold it because it draws the
bar. This finds where. Run it once with every monster untouched: an offset that reads the
same full-health value on all of them is a candidate. Run it again after hurting one and
pass -Compare with the first run's output file; the offset that moved is the answer.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.

.PARAMETER Radius
How far around the player to look, in the client's own coordinates.

.PARAMETER Save
Write every candidate's raw record to this file, for comparing against a later run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid,
    [int]$Radius = 25,
    [string]$Save
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class HpScan {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")]
    public static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, int len);

    [StructLayout(LayoutKind.Sequential)]
    public struct MBI {
        public IntPtr BaseAddress, AllocationBase;
        public int AllocationProtect;
        public IntPtr RegionSize;
        public int State, Protect, Type;
    }

    public static byte[] Read(IntPtr h, long addr, int size) {
        byte[] b = new byte[size]; IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), b, size, out got) || got.ToInt32() != size) return null;
        return b;
    }

    public static uint U32(IntPtr h, long addr) {
        byte[] b = Read(h, addr, 4);
        return b == null ? 0u : BitConverter.ToUInt32(b, 0);
    }

    // "addr,vtable" per entity standing near (px,py).
    public static List<string> Find(IntPtr h, uint px, uint py, int radius) {
        var hits = new List<string>();
        long p = 0x10000, limit = 0x7FFF0000;
        MBI mbi;
        uint xlo = (uint)(px - radius), xhi = (uint)(px + radius);
        uint ylo = (uint)(py - radius), yhi = (uint)(py + radius);
        while (p < limit) {
            if (VirtualQueryEx(h, new IntPtr(p), out mbi, Marshal.SizeOf(typeof(MBI))) == 0) break;
            long size = mbi.RegionSize.ToInt64();
            bool usable = mbi.State == 0x1000
                       && (mbi.Protect == 0x04 || mbi.Protect == 0x40)
                       && (mbi.Protect & 0x100) == 0
                       && size > 0 && size <= 64 * 1024 * 1024;
            if (usable) {
                byte[] buf = new byte[size]; IntPtr got;
                if (ReadProcessMemory(h, mbi.BaseAddress, buf, (int)size, out got) && got.ToInt64() == size) {
                    long b0 = mbi.BaseAddress.ToInt64();
                    for (int i = 0; i + 0x140 <= size; i += 4) {
                        uint vt = BitConverter.ToUInt32(buf, i);
                        if (vt < 0x400000 || vt > 0x0A00000) continue;
                        uint x = BitConverter.ToUInt32(buf, i + 0x34);
                        uint y = BitConverter.ToUInt32(buf, i + 0x38);
                        if (x < xlo || x > xhi || y < ylo || y > yhi) continue;
                        hits.Add(String.Format("{0},{1}", b0 + i, vt));
                    }
                }
            }
            p = mbi.BaseAddress.ToInt64() + size;
            if (size <= 0) break;
        }
        return hits;
    }
}
'@

if (-not ('HpScan' -as [type])) { Add-Type -TypeDefinition $sig }

$h = [HpScan]::OpenProcess(0x0410, $false, $TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($TargetPid) failed" }

try {
    $ply = [HpScan]::U32($h, 0x00C2D2B8)
    $px = [HpScan]::U32($h, $ply + 0x34)
    $py = [HpScan]::U32($h, $ply + 0x38)
    $tableA = [HpScan]::U32($h, 0x009A8F18)
    if ($tableA -lt 0x10000) { $tableA = 0x009A8F18 }

    Write-Host ("player 0x{0:X8} at ({1},{2})" -f $ply, $px, $py)

    $records = @()
    foreach ($hit in [HpScan]::Find($h, $px, $py, $Radius)) {
        $addr = [long]($hit -split ',')[0]
        if ($addr -eq $ply) { continue }
        $b = [HpScan]::Read($h, $addr, 0x140)
        if ($null -eq $b) { continue }

        $sprite = [BitConverter]::ToUInt16($b, 0x18)
        $classA = [HpScan]::Read($h, $tableA + $sprite, 1)
        if ($null -eq $classA -or $classA[0] -ne 0x0A) { continue }
        if ($b[0x27] -ne 0 -or $b[0x58] -ne 0 -or $b[0x14] -eq 8) { continue }

        $namePtr = [BitConverter]::ToUInt32($b, 0x60)
        $name = ''
        if ($namePtr -gt 0x10000 -and $namePtr -lt 0x7FFF0000) {
            $nb = [HpScan]::Read($h, $namePtr, 32)
            if ($nb) {
                $end = [Array]::IndexOf($nb, [byte]0)
                if ($end -lt 0) { $end = 32 }
                if ($end -gt 0) { $name = [Text.Encoding]::GetEncoding(950).GetString($nb, 0, $end) }
            }
        }
        if ($name -eq '') { continue }

        $records += [pscustomobject]@{
            Addr  = $addr
            Id    = [BitConverter]::ToUInt32($b, 0x0C)
            Name  = $name
            Bytes = $b
        }
    }

    Write-Host ("monsters found: {0}" -f $records.Count)
    if ($records.Count -eq 0) { return }

    foreach ($r in $records) {
        Write-Host ("  0x{0:X8}  id {1,-12} {2}" -f $r.Addr, $r.Id, $r.Name)
    }

    # An offset every monster agrees on is either a constant or a field they all share a
    # value for, and full health is exactly that. Anything already varying between monsters
    # standing untouched cannot be it.
    Write-Host ''
    Write-Host 'offsets every monster agrees on, with a value that could be full health:'
    for ($off = 0; $off -lt 0x140; $off++) {
        $values = $records | ForEach-Object { $_.Bytes[$off] } | Sort-Object -Unique
        if ($values.Count -ne 1) { continue }
        $v = $values[0]
        if ($v -ne 100 -and $v -ne 255 -and $v -ne 0x64) { continue }
        Write-Host ("  +0x{0:X3} = {1} (0x{1:X2})" -f $off, $v)
    }

    Write-Host ''
    Write-Host 'offsets that differ between monsters (already ruled out unless one is hurt):'
    $varying = @()
    for ($off = 0; $off -lt 0x140; $off++) {
        $values = $records | ForEach-Object { $_.Bytes[$off] } | Sort-Object -Unique
        if ($values.Count -gt 1) { $varying += $off }
    }
    Write-Host ("  {0}" -f (($varying | ForEach-Object { "0x{0:X3}" -f $_ }) -join ' '))

    if ($Save) {
        $records | ForEach-Object {
            "{0} {1} {2}" -f $_.Id, $_.Name, ([BitConverter]::ToString($_.Bytes) -replace '-', '')
        } | Set-Content -Path $Save -Encoding utf8
        Write-Host ''
        Write-Host ("saved {0} records to {1}" -f $records.Count, $Save)
    }
}
finally { [void][HpScan]::CloseHandle($h) }
