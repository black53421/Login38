<#
.SYNOPSIS
Finds which field of a monster's record says it is attacking the player.

.DESCRIPTION
Walks the client's heap for entity records, then reports every offset in each nearby
record that holds the local player's address or the local player's object id. A field
that only the monsters currently fighting the player carry is the one worth reading.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid,
    [int]$Radius = 40,
    [int]$Length = 0x140
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class Scan
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(
        IntPtr handle, IntPtr address, byte[] buffer, int size, out int read);

    public static IntPtr Open(int pid) { return OpenProcess(0x0410, false, pid); }

    public static byte[] Read(IntPtr handle, long address, int size)
    {
        byte[] buffer = new byte[size];
        int read;
        if (!ReadProcessMemory(handle, new IntPtr(address), buffer, size, out read)) return null;
        return buffer;
    }

    public static uint U32(IntPtr handle, long address)
    {
        byte[] b = Read(handle, address, 4);
        return b == null ? 0u : BitConverter.ToUInt32(b, 0);
    }

    // Every 16-byte-aligned slot in the client's heap whose first dword is the entity
    // vtable, within Radius of the player.
    public static string Find(IntPtr handle, uint vtable, int px, int py, int radius)
    {
        System.Text.StringBuilder found = new System.Text.StringBuilder();
        byte[] page = new byte[0x10000];
        for (long at = 0x02000000; at < 0x40000000; at += 0x10000)
        {
            int read;
            if (!ReadProcessMemory(handle, new IntPtr(at), page, page.Length, out read)) continue;
            for (int i = 0; i + 0x140 < page.Length; i += 4)
            {
                if (BitConverter.ToUInt32(page, i) != vtable) continue;
                int x = BitConverter.ToInt32(page, i + 0x34);
                int y = BitConverter.ToInt32(page, i + 0x38);
                if (Math.Abs(x - px) > radius || Math.Abs(y - py) > radius) continue;
                found.Append((at + i).ToString()).Append(',');
            }
        }
        return found.ToString();
    }
}
'@

Add-Type -TypeDefinition $sig

$h = [Scan]::Open($TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open $TargetPid" }

try {
    $vtable = 0x008DC08C
    $ply = [Scan]::U32($h, 0x00C2D2B8)
    if ($ply -eq 0) { throw "no local player" }

    $pb = [Scan]::Read($h, $ply, 0x80)
    $px = [BitConverter]::ToInt32($pb, 0x34)
    $py = [BitConverter]::ToInt32($pb, 0x38)
    $pid32 = [BitConverter]::ToUInt32($pb, 0x0C)

    "player 0x{0:X8}  id {1}  at ({2},{3})" -f $ply, $pid32, $px, $py
    "attack target [0xC2D2B4] = 0x{0:X8}   hover [0xABF440] = 0x{1:X8}" -f `
        [Scan]::U32($h, 0x00C2D2B4), [Scan]::U32($h, 0x00ABF440)
    ""

    $hits = ([Scan]::Find($h, $vtable, $px, $py, $Radius) -split ',') |
        Where-Object { $_ } | ForEach-Object { [long]$_ }

    "records within $Radius : $($hits.Count)"
    ""

    foreach ($addr in $hits) {
        if ($addr -eq $ply) { continue }
        $b = [Scan]::Read($h, $addr, $Length)
        if (-not $b) { continue }

        $namePtr = [BitConverter]::ToUInt32($b, 0x60)
        $name = ""
        if ($namePtr -gt 0x10000 -and $namePtr -lt 0x7FFF0000) {
            $nb = [Scan]::Read($h, $namePtr, 32)
            if ($nb) {
                $end = [Array]::IndexOf($nb, [byte]0)
                if ($end -lt 0) { $end = 32 }
                if ($end -gt 0) { $name = [Text.Encoding]::GetEncoding(950).GetString($nb, 0, $end) }
            }
        }
        if ($name -match '^(.*)#(\d+):(\d+)$') { $name = $Matches[1] }

        # Every offset holding the player's address or the player's id.
        $refs = @()
        for ($o = 0; $o + 4 -le $Length; $o += 4) {
            $v = [BitConverter]::ToUInt32($b, $o)
            if ($v -eq $ply)   { $refs += ("+0x{0:X2}=ptr" -f $o) }
            if ($v -eq $pid32) { $refs += ("+0x{0:X2}=id"  -f $o) }
        }

        $x = [BitConverter]::ToInt32($b, 0x34)
        $y = [BitConverter]::ToInt32($b, 0x38)
        $dist = [Math]::Max([Math]::Abs($x - $px), [Math]::Abs($y - $py))

        "0x{0:X8}  {1,-10}  d={2,-3}  act=0x{3:X2}  {4}" -f `
            $addr, $name, $dist, $b[0x14], ($refs -join " ")
    }
}
finally { [void][Scan]::CloseHandle($h) }
