# cellmap.ps1 -- cross-tabulate the client's collision cell word against the server's tile byte.
#
# The launcher's sight trace tests bit 0 of the ushort at cell+4 (walkability). The server
# decides a shot with `tile & 0x0C` (arrow passability), out of maps/{id}.txt. If the client's
# word carries the same attribute somewhere, the trace can test the same thing and the whole
# "no wall in the way and it still whiffs" class goes away.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\cellmap.ps1 -Pid 48052
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [string]$MapDir = 'D:\L1JGO-Whale\l1j_yiwei_java\maps',
    [string]$MapList = 'D:\L1JGO-Whale\server\data\yaml\map_list.yaml'
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mem {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);
    public static byte[] Read(IntPtr h, long addr, int size) {
        var buf = new byte[size];
        IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), buf, size, out got)) return null;
        return buf;
    }
}
'@

$PROCESS_VM_READ = 0x0010
$PROCESS_QUERY_INFORMATION = 0x0400
$h = [Mem]::OpenProcess($PROCESS_VM_READ -bor $PROCESS_QUERY_INFORMATION, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }

function Read-U32([long]$addr) { [BitConverter]::ToUInt32([Mem]::Read($h, $addr, 4), 0) }
function Read-I32([long]$addr) { [BitConverter]::ToInt32([Mem]::Read($h, $addr, 4), 0) }

$cells   = Read-U32 0x00ABF4C0
$originX = Read-I32 0x00ABF978
$originY = Read-I32 0x00ABF97C
$mapId   = Read-U32 0x00965B60

Write-Host "cells=0x$($cells.ToString('X8')) origin=($originX,$originY) map=$mapId"
if ($cells -eq 0) { throw 'no world loaded' }

# Map extent, so the comparison stops at the edge of what the server has.
$yaml = Get-Content $MapList -Raw
$block = ($yaml -split '  - map_id: ') | Where-Object { $_ -match "^$mapId\s" } | Select-Object -First 1
if (-not $block) { throw "map $mapId is not in $MapList" }
function Field($name) { if ($block -match "$name`: (-?\d+)") { [int]$Matches[1] } else { throw "no $name" } }
$startX = Field 'start_x'; $endX = Field 'end_x'
$startY = Field 'start_y'; $endY = Field 'end_y'
$width = $endX - $startX + 1
$height = $endY - $startY + 1
Write-Host "server extent: x $startX..$endX  y $startY..$endY  ($width x $height)"

# Server tiles: one line per Y, comma separated X.
$tiles = New-Object 'byte[,]' $width, $height
$y = 0
foreach ($line in [System.IO.File]::ReadLines((Join-Path $MapDir "$mapId.txt"))) {
    if ($y -ge $height) { break }
    $line = $line.Trim()
    if ($line.Length -eq 0 -or $line[0] -eq '#') { continue }
    $x = 0
    foreach ($tok in $line.Split(',')) {
        if ($x -ge $width) { break }
        $tiles[$x, $y] = [byte]([int]$tok -band 0xFF)
        $x++
    }
    $y++
}
Write-Host "read $y rows of server tiles"

# Client cells: 0x14 each, index (y-originY)*0x100 + (x-originX).
$stride = 0x100
$cellLen = 0x14
$pairs = @{}
$rows = [Math]::Min($height, 131)
for ($ry = 0; $ry -lt $rows; $ry++) {
    $rowAddr = [int64]$cells + ([int64]$ry * $stride * $cellLen)
    $raw = [Mem]::Read($h, $rowAddr, $stride * $cellLen)
    if ($null -eq $raw) { Write-Host "row $ry unreadable"; break }
    for ($rx = 0; $rx -lt $stride; $rx++) {
        $wx = $originX - $startX + $rx
        $wy = $originY - $startY + $ry
        if ($wx -lt 0 -or $wx -ge $width -or $wy -lt 0 -or $wy -ge $height) { continue }
        $word = [BitConverter]::ToUInt16($raw, ($rx * $cellLen) + 4)
        $key = '{0:X4}/{1:X2}' -f $word, $tiles[$wx, $wy]
        if ($pairs.ContainsKey($key)) { $pairs[$key]++ } else { $pairs[$key] = 1 }
    }
}

[Mem]::CloseHandle($h) | Out-Null

Write-Host ''
Write-Host 'client cell+4 word / server tile byte  ->  count'
$pairs.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 40 | ForEach-Object {
    $parts = $_.Key.Split('/')
    $w = [Convert]::ToUInt16($parts[0], 16)
    $t = [Convert]::ToByte($parts[1], 16)
    '{0}  word b0={1} b1={2} b2={3} b3={4}   tile b0={5} b1={6} b2={7} b3={8}   x{9}' -f `
        $_.Key, ($w -band 1), (($w -shr 1) -band 1), (($w -shr 2) -band 1), (($w -shr 3) -band 1),
        ($t -band 1), (($t -shr 1) -band 1), (($t -shr 2) -band 1), (($t -shr 3) -band 1), $_.Value
}
