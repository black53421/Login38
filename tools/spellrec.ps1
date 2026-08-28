# spellrec.ps1 -- dump whole skill records from the client's book, so a field holding the
# skill's range can be found by looking rather than by guessing.
#
# The launcher only reads two fields out of a record today (packed id at +4, name pointer at
# +0xC). Everything else in it is unexamined, and the range has to be somewhere -- the client
# draws skill tooltips.
param(
    [Parameter(Mandatory = $true)][int]$ClientPid,
    [int]$Length = 0x60,
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Rec {
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

$h = [Rec]::OpenProcess(0x0010 -bor 0x0400, $false, $ClientPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open pid $ClientPid" }
function RU32([long]$a) { $b = [Rec]::Read($h, $a, 4); if ($null -eq $b) { 0 } else { [BitConverter]::ToUInt32($b, 0) } }

$book = RU32 0x00C31324
$count = RU32 ([int64]$book + 0x2C)
$array = RU32 ([int64]$book + 0x58)
if ($book -eq 0 -or $array -eq 0) { throw 'no spell book' }

$big5 = [System.Text.Encoding]::GetEncoding(950)
$ptrs = [Rec]::Read($h, $array, [int]$count * 4)

for ($i = 0; $i -lt $count; $i++) {
    $rec = [BitConverter]::ToUInt32($ptrs, $i * 4)
    if ($rec -eq 0) { continue }
    $raw = [Rec]::Read($h, $rec, $Length)
    if ($null -eq $raw) { continue }

    $textPtr = [BitConverter]::ToUInt32($raw, 0x0C)
    $name = ''
    if ($textPtr -ne 0) {
        $txt = [Rec]::Read($h, $textPtr, 96)
        if ($null -ne $txt) {
            $end = [Array]::IndexOf($txt, [byte]0)
            if ($end -lt 0) { $end = $txt.Length }
            $name = $big5.GetString($txt, 0, $end)
        }
    }
    $bare = $name
    $open = $name.LastIndexOf('(')
    if ($open -gt 0) { $bare = $name.Substring(0, $open).Trim() }

    if ($Only.Count -gt 0 -and -not ($Only -contains $bare)) { continue }

    $words = @()
    for ($o = 0; $o -lt $Length; $o += 4) {
        $words += ('{0:X2}:{1}' -f $o, [BitConverter]::ToUInt32($raw, $o))
    }
    Write-Host ("{0}  @0x{1:X8}" -f $name, $rec)
    Write-Host ('   ' + ($words -join '  '))
}

[Rec]::CloseHandle($h) | Out-Null
