# Dump the client's collision grid around the player as ASCII.
#   .  passable (flags bit0 clear)      #  blocked (bit0 set)
#   P  the player                       o  cell carries an object list (+0x0C)
param(
    [Parameter(Mandatory=$true)][int]$TargetPid,
    [int]$Radius = 24,
    [switch]$Flags   # also print a histogram of distinct flag words
)

$sig = @'
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
        byte[] b = new byte[size];
        IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), b, size, out got) || got.ToInt32() != size)
            throw new Exception(String.Format("read 0x{0:X} +{1} failed, err {2}", addr, size, Marshal.GetLastWin32Error()));
        return b;
    }
    public static uint U32(IntPtr h, long addr) { return BitConverter.ToUInt32(Read(h, addr, 4), 0); }
}
'@
Add-Type -TypeDefinition $sig

$GRID_PTR = 0x00ABF4C0
$ORIGIN_X = 0x00ABF978
$ORIGIN_Y = 0x00ABF97C
$PLAYER   = 0x00C2D2B8
$CELL     = 0x14
$ROW      = 0x100
$CELLS    = 0x8000

$h = [Mem]::OpenProcess(0x0410, $false, $TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($TargetPid) failed: $([ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error()).Message)" }

try {
    $grid = [Mem]::U32($h, $GRID_PTR)
    $ox   = [Mem]::U32($h, $ORIGIN_X)
    $oy   = [Mem]::U32($h, $ORIGIN_Y)
    $ply  = [Mem]::U32($h, $PLAYER)
    $px   = [Mem]::U32($h, $ply + 0x34)
    $py   = [Mem]::U32($h, $ply + 0x38)

    $idx = ($py - $oy) * $ROW + ($px - $ox)
    "grid   0x{0:X8}   origin ({1},{2})" -f $grid, $ox, $oy
    "player 0x{0:X8}   pos ({1},{2})  index {3}  row {4} col {5}" -f $ply, $px, $py, $idx, ($idx -shr 8), ($idx -band 0xFF)
    if ($idx -lt 0 -or $idx -ge $CELLS) { "index out of range" ; return }

    $prow = [int]($idx -shr 8); $pcol = [int]($idx -band 0xFF)
    $r0 = [Math]::Max(0, $prow - $Radius); $r1 = [Math]::Min(127, $prow + $Radius)
    $c0 = [Math]::Max(0, $pcol - $Radius * 2); $c1 = [Math]::Min(255, $pcol + $Radius * 2)
    $hist = @{}
    ""
    "     " + (($c0..$c1) | ForEach-Object { if ($_ % 10 -eq 0) { "|" } else { " " } }) -join ""
    foreach ($r in $r0..$r1) {
        $bytes = [Mem]::Read($h, $grid + ($r * $ROW + $c0) * $CELL, ($c1 - $c0 + 1) * $CELL)
        $line = New-Object System.Text.StringBuilder
        for ($c = $c0; $c -le $c1; $c++) {
            $o = ($c - $c0) * $CELL
            $f = [BitConverter]::ToUInt16($bytes, $o + 4)
            $obj = [BitConverter]::ToUInt32($bytes, $o + 0x0C)
            if (-not $hist.ContainsKey($f)) { $hist[$f] = 0 }
            $hist[$f]++
            if ($r -eq $prow -and $c -eq $pcol) { [void]$line.Append('P') }
            elseif ($f -band 1)                 { [void]$line.Append('#') }
            elseif ($obj -ne 0)                 { [void]$line.Append('o') }
            else                                { [void]$line.Append('.') }
        }
        "{0,4} {1}" -f $r, $line.ToString()
    }
    if ($Flags) {
        ""
        "flag word histogram:"
        $hist.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
            "  0x{0:X4}  bit0={1}  {2} cells" -f $_.Key, ($_.Key -band 1), $_.Value
        }
    }
}
finally { [void][Mem]::CloseHandle($h) }
