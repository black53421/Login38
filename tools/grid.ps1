<#
.SYNOPSIS
Prints the client's own collision grid around the character.

.DESCRIPTION
The hunt asks the client whether its walk would reach a monster, by replaying the client's
own hill climb inside the client. When that answer is "none of eleven", two very different
things look identical from the log: the character really is walled in, or the replay is
wrong. Nothing in the launcher can tell them apart, because the launcher is the thing being
doubted.

This reads the grid straight out of the process and draws it, so the question is answered by
looking. `FUN_004F4BD0` is the client's own index arithmetic:

    cell = (y - originY) * 0x100 + (x - originX)

with the cell array at `DAT_00ABF4C0`, twenty bytes a cell, and bit 0 of the attribute at
`+4` meaning BLOCKED.

That polarity is settled by the client's own walk engine and by nothing else. In
`ComputeStepHeading` at `0x5A4D60`:

    uVar3 = FUN_004f5910(px, py, heading);
    if ((uVar3 & 0xff) == 0) { score = GridDistance(step, dest); }
    else                     { score = 99999999; }

Zero is usable, non-zero is wall. Do not try to settle it from the picture instead: the bit
belongs to a heading out of a cell, not to standing in one, so a character occupying a cell
that carries the bit is ordinary and proves nothing. Reading it the other way round produced
a search that pathed through walls.

Two columns make a tile across and one row makes one down, so the output is drawn two
characters wide per tile to keep it the shape the map actually is.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.

.PARAMETER Across
How many columns either side of the character to draw.

.PARAMETER Down
How many rows either side of the character to draw.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid,
    [int]$Across = 40,
    [int]$Down = 16
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class Grid {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);

    public static byte[] Read(IntPtr h, long addr, int size) {
        byte[] b = new byte[size]; IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), b, size, out got) || got.ToInt32() != size) return null;
        return b;
    }

    public static uint U32(IntPtr h, long addr) {
        byte[] b = Read(h, addr, 4);
        return b == null ? 0u : BitConverter.ToUInt32(b, 0);
    }

    public static int I32(IntPtr h, long addr) {
        byte[] b = Read(h, addr, 4);
        return b == null ? 0 : BitConverter.ToInt32(b, 0);
    }
}
'@

if (-not ('Grid' -as [type])) { Add-Type -TypeDefinition $sig }

$h = [Grid]::OpenProcess(0x0410, $false, $TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($TargetPid) failed" }

try {
    $cells   = [Grid]::U32($h, 0x00ABF4C0)
    $originX = [Grid]::I32($h, 0x00ABF978)
    $originY = [Grid]::I32($h, 0x00ABF97C)
    $player  = [Grid]::U32($h, 0x00C2D2B8)

    if ($cells -eq 0 -or $player -eq 0) { throw "client is not in the world (cells=$cells player=$player)" }

    $px = [Grid]::I32($h, $player + 0x34)
    $py = [Grid]::I32($h, $player + 0x38)

    Write-Host ("cells 0x{0:X8}  origin ({1},{2})  player ({3},{4})  index {5}" -f `
        $cells, $originX, $originY, $px, $py, (($py - $originY) * 0x100 + $px - $originX))
    Write-Host ''

    # One read for the whole window rather than one per cell: the rows wanted are contiguous
    # in the array, so this is a single span even though only part of each row is drawn.
    $firstRow = $py - $Down
    $rows = ($Down * 2) + 1
    $start = $cells + ((($firstRow - $originY) * 0x100) * 0x14)
    $span = [Grid]::Read($h, $start, $rows * 0x100 * 0x14)

    if ($null -eq $span) { throw "the grid window could not be read at 0x$('{0:X8}' -f $start)" }

    $blocked = 0
    $open = 0

    for ($r = 0; $r -lt $rows; $r++) {
        $y = $firstRow + $r
        $line = New-Object System.Text.StringBuilder

        for ($x = $px - $Across; $x -le $px + $Across; $x++) {
            $at = (($r * 0x100) + ($x - $originX)) * 0x14

            if ($at -lt 0 -or $at + 6 -gt $span.Length) { [void]$line.Append('?'); continue }

            $attr = [BitConverter]::ToUInt16($span, $at + 4)
            $shut = ($attr -band 1) -ne 0

            if ($shut) { $blocked++ } else { $open++ }

            if ($x -eq $px -and $y -eq $py) { [void]$line.Append('@') }
            elseif ($shut)                 { [void]$line.Append('#') }
            else                           { [void]$line.Append('.') }
        }

        Write-Host ("{0,6}  {1}" -f $y, $line.ToString())
    }

    Write-Host ''
    Write-Host ("# impassable = {0}, . walkable = {1}" -f $blocked, $open)
    Write-Host '(# is a heading refused out of that cell, not a square nobody may stand on)'
}
finally { [void][Grid]::CloseHandle($h) }
