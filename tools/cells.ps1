<#
.SYNOPSIS
Histograms the client's collision grid, one entry per distinct cell value.

.DESCRIPTION
TileBlocked at 0x4F5910 reads a ushort at cell+4 and tests bit 0 — "can I walk here". That
is not the bit the server checks before it lets an arrow through. L1JGO's IsArrowPassable
reads the map's original tile byte and tests 0x0C (bit 2, arrows east; bit 3, arrows north),
and refuses 0x00 and 0x03 outright. A corner is exactly where those two answers differ: a
character can walk round it and an arrow cannot pass it, so the launcher's own sight trace
lets a target through and the server drops every shot at it without a word.

The question this answers is whether the client's cell holds the same tile attribute the
server does. If it does, the values below will be the server's vocabulary — 0, 1, 3, 15 and
friends — and the launcher can test 0x0C on the client's own grid instead of guessing. If
they are something else, the encoding is the client's own and has to be worked out.

Cells are 20 bytes and there are 33,540 of them, allocated at 0x4E6C20.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class CellPeek
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(
        IntPtr handle, IntPtr address, byte[] buffer, int size, out int read);

    public static IntPtr Open(int pid) { return OpenProcess(0x0410, false, pid); }

    public static byte[] Read(IntPtr h, long a, int n)
    {
        byte[] buffer = new byte[n];
        int read;
        if (!ReadProcessMemory(h, new IntPtr(a), buffer, n, out read) || read != n) { return null; }
        return buffer;
    }
}
'@

if (-not ('CellPeek' -as [type])) { Add-Type -TypeDefinition $sig }

$handle = [CellPeek]::Open($TargetPid)
if ($handle -eq [IntPtr]::Zero) { throw "could not open pid $TargetPid" }

try {
    $cellsPtr = [CellPeek]::Read($handle, 0x00ABF4C0, 4)
    if ($null -eq $cellsPtr) { throw 'could not read the cell array pointer at 0xABF4C0' }
    $cells = [BitConverter]::ToUInt32($cellsPtr, 0)
    if ($cells -eq 0) { throw 'the client has not allocated its collision grid yet' }

    $originX = [BitConverter]::ToInt32([CellPeek]::Read($handle, 0x00ABF978, 4), 0)
    $originY = [BitConverter]::ToInt32([CellPeek]::Read($handle, 0x00ABF97C, 4), 0)

    Write-Host ''
    Write-Host ("cells at 0x{0:X8}, origin ({1},{2})" -f $cells, $originX, $originY)

    $count = 0x8304
    $stride = 0x14
    $bytes = [CellPeek]::Read($handle, $cells, $count * $stride)
    if ($null -eq $bytes) { throw 'could not read the cell array' }

    $histogram = @{}
    for ($i = 0; $i -lt $count; $i++) {
        $value = [BitConverter]::ToUInt16($bytes, ($i * $stride) + 4)
        if ($histogram.ContainsKey($value)) { $histogram[$value]++ } else { $histogram[$value] = 1 }
    }

    Write-Host ''
    Write-Host 'cell+4 (ushort), most common first:'
    Write-Host '   value    hex   walk(&1)  arrows(&0x0C)      count'
    $histogram.GetEnumerator() |
        Sort-Object -Property Value -Descending |
        Select-Object -First 25 |
        ForEach-Object {
            Write-Host ("  {0,6}  0x{1:X4}   {2,-8}  {3,-13}  {4,9}" -f
                $_.Key,
                $_.Key,
                $(if ($_.Key -band 1) { 'yes' } else { 'no' }),
                $(if ($_.Key -band 0x0C) { 'yes' } else { 'no' }),
                $_.Value)
        }

    Write-Host ''
    Write-Host ("distinct values: {0}" -f $histogram.Count)
}
finally {
    [void][CellPeek]::CloseHandle($handle)
}
