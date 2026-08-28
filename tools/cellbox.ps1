# cellbox.ps1 -- print the client's collision word and the server's tile byte side by side,
# in a small box around the character, so the two can be eyeballed for alignment.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [int]$Radius = 16,
    [int]$XScale = 1,
    [string]$MapDir = 'D:\L1JGO-Whale\l1j_yiwei_java\maps',
    [string]$MapList = 'D:\L1JGO-Whale\server\data\yaml\map_list.yaml'
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mem2 {
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

$h = [Mem2]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }
function RU32([long]$a) { [BitConverter]::ToUInt32([Mem2]::Read($h, $a, 4), 0) }
function RI32([long]$a) { [BitConverter]::ToInt32([Mem2]::Read($h, $a, 4), 0) }

$cells = RU32 0x00ABF4C0
$originX = RI32 0x00ABF978
$originY = RI32 0x00ABF97C
$mapId = RU32 0x00965B60
$player = RU32 0x00C2D2B8
if ($player -eq 0) { throw 'not in the world' }
$px = RI32 ([int64]$player + 0x34)
$py = RI32 ([int64]$player + 0x38)
Write-Host "map=$mapId origin=($originX,$originY) player=($px,$py) cells=0x$($cells.ToString('X8'))"

$yaml = Get-Content $MapList -Raw
$block = ($yaml -split '  - map_id: ') | Where-Object { $_ -match "^$mapId\s" } | Select-Object -First 1
function Field($n) { if ($block -match "$n`: (-?\d+)") { [int]$Matches[1] } else { throw "no $n" } }
$startX = Field 'start_x'; $endX = Field 'end_x'
$startY = Field 'start_y'; $endY = Field 'end_y'
$width = $endX - $startX + 1; $height = $endY - $startY + 1

$tiles = New-Object 'byte[,]' $width, $height
$y = 0
foreach ($line in [System.IO.File]::ReadLines((Join-Path $MapDir "$mapId.txt"))) {
    if ($y -ge $height) { break }
    $line = $line.Trim()
    if ($line.Length -eq 0 -or $line[0] -eq '#') { continue }
    $x = 0
    foreach ($tok in $line.Split(',')) { if ($x -ge $width) { break }; $tiles[$x, $y] = [byte]([int]$tok -band 0xFF); $x++ }
    $y++
}

$stride = 0x100; $cellLen = 0x14
Write-Host ''
Write-Host "left: client cell+4 bit0 (# = blocked)   right: server tile (# = tile 0 or 32 = impassable)"
for ($wy = $py - $Radius; $wy -le $py + $Radius; $wy++) {
    $a = ''; $b = ''
    for ($wx = $px - ($Radius * 2); $wx -le $px + ($Radius * 2); $wx++) {
        $gx = $wx - $originX; $gy = $wy - $originY
        if ($gx -lt 0 -or $gx -ge $stride -or $gy -lt 0 -or $gy -ge 131) { $a += ' ' }
        else {
            $raw = [Mem2]::Read($h, ([int64]$cells + (([int64]$gy * $stride + $gx) * $cellLen) + 4), 2)
            $w = [BitConverter]::ToUInt16($raw, 0)
            $a += $(if ($w -band 1) { '#' } else { '.' })
        }
        $sx = [int][Math]::Floor(($wx - $originX) / $XScale) + ($originX - $startX); $sy = $wy - $startY
        if ($sx -lt 0 -or $sx -ge $width -or $sy -lt 0 -or $sy -ge $height) { $b += ' ' }
        else {
            $t = $tiles[$sx, $sy]
            $b += $(if (($t -band 0x03) -eq 0) { '#' } elseif (($t -band 0x0C) -eq 0) { 'o' } else { '.' })
        }
    }
    $mark = $(if ($wy -eq $py) { '<' } else { ' ' })
    Write-Host "$a  |  $b $mark"
}
[Mem2]::CloseHandle($h) | Out-Null
