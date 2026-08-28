# spellbook.ps1 -- dump the client's learned-skill book, name and packed id.
#
# The hunt takes a skill's range out of the bracketed numbers on the end of its name. If the
# names in a real book do not carry them, every skill reads as range 0 and the rotation treats
# them all as arm's length. Look before assuming.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Book {
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

$h = [Book]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }
function RU32([long]$a) { $b = [Book]::Read($h, $a, 4); if ($null -eq $b) { 0 } else { [BitConverter]::ToUInt32($b, 0) } }

$book = RU32 0x00C31324
Write-Host "book = 0x$($book.ToString('X8'))"
if ($book -eq 0) { throw 'no spell book (not in the world?)' }

$count = RU32 ([int64]$book + 0x2C)
$array = RU32 ([int64]$book + 0x58)
Write-Host "count=$count array=0x$($array.ToString('X8'))"
if ($count -eq 0 -or $count -gt 1024 -or $array -eq 0) { throw 'book looks wrong' }

$big5 = [System.Text.Encoding]::GetEncoding(950)
$ptrs = [Book]::Read($h, $array, [int]$count * 4)

for ($i = 0; $i -lt $count; $i++) {
    $rec = [BitConverter]::ToUInt32($ptrs, $i * 4)
    if ($rec -eq 0) { continue }
    $packed = RU32 ([int64]$rec + 0x04)
    $textPtr = RU32 ([int64]$rec + 0x0C)
    if ($textPtr -eq 0) { continue }
    $raw = [Book]::Read($h, $textPtr, 96)
    if ($null -eq $raw) { continue }
    $end = [Array]::IndexOf($raw, [byte]0)
    if ($end -lt 0) { $end = $raw.Length }
    $name = $big5.GetString($raw, 0, $end)
    '{0,4}  id={1,-5}  {2}' -f $i, $packed, $name
}

[Book]::CloseHandle($h) | Out-Null
