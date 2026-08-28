<#
.SYNOPSIS
Watches two bytes of every nearby monster's record and reports the ones that move.

.DESCRIPTION
The hunt needs to tell "my attacks are landing" from "the server is throwing every one of
them away". Both look the same from inside the client, which starts the blow, plays the
animation and pushes its own cooldown forward either way; the server drops what it will not
allow - no line of sight round a corner, a monster still burrowed - and says nothing at all.

Health is what separates them, and the server sends it: the NPC spawn packet writes a hidden
byte and then an HP percentage, 0xFF meaning full. +0x30 and +0x31 read like that pair on
every monster sampled, and this is what checks it. Run it while something is being fought:
the byte that falls is health, and a monster whose health never falls is one the server is
refusing.

Addresses are resolved once and then polled, so the scan cost is paid a single time.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.

.PARAMETER Seconds
How long to watch.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid,
    [int]$Radius = 25,
    [int]$Seconds = 25
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class HpWatch {
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
                        hits.Add(String.Format("{0}", b0 + i));
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

if (-not ('HpWatch' -as [type])) { Add-Type -TypeDefinition $sig }

$h = [HpWatch]::OpenProcess(0x0410, $false, $TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($TargetPid) failed" }

try {
    $ply = [HpWatch]::U32($h, 0x00C2D2B8)
    $px = [HpWatch]::U32($h, $ply + 0x34)
    $py = [HpWatch]::U32($h, $ply + 0x38)
    $tableA = [HpWatch]::U32($h, 0x009A8F18)
    if ($tableA -lt 0x10000) { $tableA = 0x009A8F18 }

    $watched = @{}
    foreach ($hit in [HpWatch]::Find($h, $px, $py, $Radius)) {
        $addr = [long]$hit
        if ($addr -eq $ply) { continue }
        $b = [HpWatch]::Read($h, $addr, 0x140)
        if ($null -eq $b) { continue }
        $sprite = [BitConverter]::ToUInt16($b, 0x18)
        $classA = [HpWatch]::Read($h, $tableA + $sprite, 1)
        if ($null -eq $classA -or $classA[0] -ne 0x0A) { continue }
        if ($b[0x27] -ne 0) { continue }

        $namePtr = [BitConverter]::ToUInt32($b, 0x60)
        $name = ''
        if ($namePtr -gt 0x10000 -and $namePtr -lt 0x7FFF0000) {
            $nb = [HpWatch]::Read($h, $namePtr, 32)
            if ($nb) {
                $end = [Array]::IndexOf($nb, [byte]0)
                if ($end -lt 0) { $end = 32 }
                if ($end -gt 0) { $name = [Text.Encoding]::GetEncoding(950).GetString($nb, 0, $end) }
            }
        }
        if ($name -eq '') { continue }

        $watched[$addr] = [pscustomobject]@{
            Id   = [BitConverter]::ToUInt32($b, 0x0C)
            Name = $name
            Seen = New-Object System.Collections.Generic.List[string]
        }
    }

    Write-Host ("watching {0} monsters for {1}s" -f $watched.Count, $Seconds)
    Write-Host ''

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($addr in @($watched.Keys)) {
            $b = [HpWatch]::Read($h, $addr, 0x60)
            if ($null -eq $b) { continue }
            if ([BitConverter]::ToUInt32($b, 0x0C) -ne $watched[$addr].Id) { continue }
            $state = "{0:X2}/{1:X2}/{2:X2}" -f $b[0x30], $b[0x31], $b[0x14]
            $seen = $watched[$addr].Seen
            if ($seen.Count -eq 0 -or $seen[$seen.Count - 1] -ne $state) { $seen.Add($state) | Out-Null }
        }
        Start-Sleep -Milliseconds 250
    }

    Write-Host 'id            +0x30/+0x31/+0x14 as they changed'
    foreach ($addr in @($watched.Keys)) {
        $w = $watched[$addr]
        $trail = ($w.Seen | Select-Object -First 24) -join '  '
        Write-Host ("{0,-12}  {1}" -f $w.Id, $trail)
    }
}
finally { [void][HpWatch]::CloseHandle($h) }
