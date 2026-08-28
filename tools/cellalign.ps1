# cellalign.ps1 -- find how the client's collision window lines up with the server's tile file.
#
# Both describe the same walls, so if the mapping is right the client's "blocked" bit and the
# server's "impassable" test agree almost everywhere. Scans candidate offsets and X scales and
# reports the agreement rate, so the mapping is measured rather than assumed.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [int]$Span = 8,
    [string]$MapDir = 'D:\L1JGO-Whale\l1j_yiwei_java\maps',
    [string]$MapList = 'D:\L1JGO-Whale\server\data\yaml\map_list.yaml'
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mem3 {
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

$h = [Mem3]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }
function RU32([long]$a) { [BitConverter]::ToUInt32([Mem3]::Read($h, $a, 4), 0) }
function RI32([long]$a) { [BitConverter]::ToInt32([Mem3]::Read($h, $a, 4), 0) }

$cells = RU32 0x00ABF4C0
$originX = RI32 0x00ABF978
$originY = RI32 0x00ABF97C
$mapId = RU32 0x00965B60
$player = RU32 0x00C2D2B8
$px = RI32 ([int64]$player + 0x34)
$py = RI32 ([int64]$player + 0x38)
Write-Host "map=$mapId origin=($originX,$originY) player=($px,$py)"

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

# The client window, whole, as a blocked/clear bitmap.
$stride = 0x100; $rows = 131; $cellLen = 0x14
$blocked = New-Object 'byte[,]' $stride, $rows
for ($gy = 0; $gy -lt $rows; $gy++) {
    $raw = [Mem3]::Read($h, ([int64]$cells + ([int64]$gy * $stride * $cellLen)), $stride * $cellLen)
    if ($null -eq $raw) { $rows = $gy; break }
    for ($gx = 0; $gx -lt $stride; $gx++) {
        $blocked[$gx, $gy] = [byte]([BitConverter]::ToUInt16($raw, ($gx * $cellLen) + 4) -band 1)
    }
}
[Mem3]::CloseHandle($h) | Out-Null
Write-Host "read $rows client rows"

# Only near the character: the far corners of the window can hold blocks that were never filled.
$gx0 = [Math]::Max(0, $px - $originX - 60); $gx1 = [Math]::Min($stride - 1, $px - $originX + 60)
$gy0 = [Math]::Max(0, $py - $originY - 40); $gy1 = [Math]::Min($rows - 1, $py - $originY + 40)

$best = $null
foreach ($scale in 1, 2) {
    for ($dy = -$Span; $dy -le $Span; $dy++) {
        for ($dx = -$Span; $dx -le $Span; $dx++) {
            $both = 0; $either = 0
            for ($gy = $gy0; $gy -le $gy1; $gy++) {
                $sy = $gy + ($originY - $startY) + $dy
                if ($sy -lt 0 -or $sy -ge $height) { continue }
                for ($gx = $gx0; $gx -le $gx1; $gx++) {
                    $sx = [int][Math]::Floor($gx / $scale) + ($originX - $startX) + $dx
                    if ($sx -lt 0 -or $sx -ge $width) { continue }
                    # Jaccard over the open cells. Plain agreement is meaningless here:
                    # this map is 90% wall, so any offset scores 80-something per cent.
                    $openServer = (($tiles[$sx, $sy] -band 0x03) -ne 0)
                    $openClient = ($blocked[$gx, $gy] -eq 0)
                    if ($openServer -and $openClient) { $both++ }
                    if ($openServer -or $openClient) { $either++ }
                }
            }
            if ($either -gt 200) {
                $rate = $both / $either
                if ($null -eq $best -or $rate -gt $best.Rate) {
                    $best = [pscustomobject]@{ Scale = $scale; Dx = $dx; Dy = $dy; Rate = $rate; Seen = $either }
                }
            }
        }
    }
}

'best: scale={0} dx={1} dy={2} open-cell overlap={3:P1} over {4} cells' -f $best.Scale, $best.Dx, $best.Dy, $best.Rate, $best.Seen
