# chatlog.ps1 -- read the client's own chat ring, and the message table it draws from.
#
# FUN_004378a0 writes every displayed line into an array of 0x126-byte records at 0x00996D00,
# 150 of them, advancing the cursor at 0x00980EA0 by one per line. The server's numbered
# messages are looked up in a pointer array at 0x00C2D0B4 -- index 280 is the cast-failed line.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [int]$Tail = 24,
    [int[]]$Message = @()
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Chat {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);
    public static byte[] Read(IntPtr h, long addr, int size) {
        var buf = new byte[size]; IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), buf, size, out got)) return null;
        return buf;
    }
}
'@

$LINES  = 0x00996D00
$CURSOR = 0x00980EA0
$STRIDE = 0x126
$SLOTS  = 0x96
$TABLE  = 0x00C2D0B4

$h = [Chat]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }
$big5 = [System.Text.Encoding]::GetEncoding(950)

function Text([byte[]]$raw, [int]$at, [int]$max) {
    $end = [Array]::IndexOf($raw, [byte]0, $at)
    if ($end -lt 0 -or $end -gt $at + $max) { $end = $at + $max }
    $big5.GetString($raw, $at, $end - $at)
}

foreach ($id in $Message) {
    $slot = [Chat]::Read($h, $TABLE, 4)
    $array = [BitConverter]::ToUInt32($slot, 0)
    $p = [Chat]::Read($h, [int64]$array + $id * 4, 4)
    $addr = [BitConverter]::ToUInt32($p, 0)
    $raw = [Chat]::Read($h, $addr, 128)
    '  msg {0,4} @0x{1:X8}  {2}' -f $id, $addr, (Text $raw 0 120)
}
if ($Message.Count -gt 0) { Write-Host '' }

$at = [BitConverter]::ToUInt32([Chat]::Read($h, $CURSOR, 4), 0)
Write-Host ("cursor {0}" -f $at)
$all = [Chat]::Read($h, $LINES, $STRIDE * $SLOTS)

for ($i = $Tail; $i -ge 1; $i--) {
    $slot = (($at - $i) % $SLOTS + $SLOTS) % $SLOTS
    $off = $slot * $STRIDE
    $line = Text $all $off 0x5F
    $colour = [BitConverter]::ToUInt16($all, $off + 0x60)
    if ($line.Length -gt 0) { '  {0,3}  {1,5}  {2}' -f $slot, $colour, $line }
}

[Chat]::CloseHandle($h) | Out-Null
