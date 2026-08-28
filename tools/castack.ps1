# castack.ps1 -- watch what the client does to the character's own record around a cast.
#
# Mana is a slow and noisy receipt for "the server took that cast". The action byte at +0x14 is
# where every action id the server broadcasts lands, and a cast the server accepted comes back
# as one. Sample both fast and see what actually shows up.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [int]$Seconds = 20,
    [int]$EveryMs = 40
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Ack {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    public static byte[] Read(IntPtr h, long addr, int size) {
        var buf = new byte[size]; IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), buf, size, out got)) return null;
        return buf;
    }
}
'@

$h = [Ack]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }
function RU32([long]$a) { $b = [Ack]::Read($h, $a, 4); if ($null -eq $b) { 0 } else { [BitConverter]::ToUInt32($b, 0) } }

$seen = @{}
$last = -1
$stop = (Get-Date).AddSeconds($Seconds)
$transitions = 0

while ((Get-Date) -lt $stop) {
    $player = RU32 0x00C2D2B8
    if ($player -ne 0) {
        $rec = [Ack]::Read($h, $player, 0x40)
        if ($null -ne $rec) {
            $action = $rec[0x14]
            if ($seen.ContainsKey($action)) { $seen[$action]++ } else { $seen[$action] = 1 }
            if ($action -ne $last) {
                $transitions++
                if ($transitions -le 60) { Write-Host ("  +0x14 -> {0}" -f $action) }
                $last = $action
            }
        }
    }
    Start-Sleep -Milliseconds $EveryMs
}

Write-Host ''
Write-Host 'action byte on the character, by how often it was seen:'
$seen.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    '  {0,3}  x{1}' -f $_.Key, $_.Value
}
