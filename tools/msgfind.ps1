# msgfind.ps1 -- find a server message id by its text in the client's own message table.
#
# The table is a pointer array at 0x00C2D0B4, one entry per message id, each pointing into a
# loaded blob of NUL-terminated Big5 strings.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [Parameter(Mandatory = $true)][string]$Text,
    [int]$Ids = 2200
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Msg {
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

$h = [Msg]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }
$big5 = [System.Text.Encoding]::GetEncoding(950)

$array = [BitConverter]::ToUInt32([Msg]::Read($h, 0x00C2D0B4, 4), 0)
Write-Host ("table @0x{0:X8}" -f $array)
$ptrs = [Msg]::Read($h, $array, $Ids * 4)

for ($i = 0; $i -lt $Ids; $i++) {
    $p = [BitConverter]::ToUInt32($ptrs, $i * 4)
    if ($p -lt 0x10000) { continue }
    $raw = [Msg]::Read($h, $p, 160)
    if ($null -eq $raw) { continue }
    $end = [Array]::IndexOf($raw, [byte]0)
    if ($end -lt 0) { $end = 160 }
    $s = $big5.GetString($raw, 0, $end)
    if ($s.Contains($Text)) { '  {0,4}  {1}' -f $i, $s }
}
