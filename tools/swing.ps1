<#
.SYNOPSIS
Samples nearby monsters' action bytes and the player's hit points together.

.DESCRIPTION
The client keeps no record of who is attacking the player, so retaliation has to be
inferred. This samples every close monster's action code alongside the player's hit
points, then reports which action codes were on screen at the moments the hit points
dropped. Whatever shows up there is what "swinging at me" looks like.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid,
    [int]$Seconds = 20,
    [int]$Close = 4
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class Swing
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

    // addr,action,dist,x,y per record within radius, semicolon separated.
    public static string Near(IntPtr handle, uint vtable, int px, int py, int radius)
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
                int d = Math.Max(Math.Abs(x - px), Math.Abs(y - py));
                if (d > radius) continue;
                found.Append(at + i).Append(',')
                     .Append(page[i + 0x14]).Append(',')
                     .Append(d).Append(',')
                     .Append(BitConverter.ToUInt32(page, i + 0x60)).Append(',')
                     .Append(page[i + 0x58]).Append(';');
            }
        }
        return found.ToString();
    }
}
'@

Add-Type -TypeDefinition $sig

$h = [Swing]::Open($TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open $TargetPid" }

try {
    $ply = [Swing]::U32($h, 0x00C2D2B8)

    # Hit points are not stored anywhere a scanner would find them: an index xored with a
    # constant, into an array of sixteen slots, each xored with a salt that is rewritten on
    # every change. Reading the twelve bytes any other way gives a plausible wrong number.
    function Get-HitPoints {
        $encodedIndex = [Swing]::U32($h, 0x00BDC828)
        $keys = [Swing]::U32($h, 0x00BDC82C)
        $salt = [Swing]::U32($h, 0x00BDC830)
        $index = $encodedIndex -bxor 0xC0017921
        if ($index -ge 16) { return -1 }
        return [int]([Swing]::U32($h, $keys + $index * 4) -bxor $salt)
    }

    "player 0x{0:X8}   hp {1}" -f $ply, (Get-HitPoints)

    $names = @{}
    $whenHit = @{}
    $always = @{}
    $lives = @{}
    $goneBy = @{}
    $lastHp = -1
    $drops = 0
    $samples = 0
    $stop = (Get-Date).AddSeconds($Seconds)

    while ((Get-Date) -lt $stop) {
        $pb = [Swing]::Read($h, $ply, 0x80)
        if (-not $pb) { break }
        $px = [BitConverter]::ToInt32($pb, 0x34)
        $py = [BitConverter]::ToInt32($pb, 0x38)

        $hp = Get-HitPoints

        $rows = ([Swing]::Near($h, 0x008DC08C, $px, $py, $Close) -split ';') | Where-Object { $_ }
        $samples++
        $hurt = ($lastHp -ge 0 -and $hp -lt $lastHp)
        if ($hurt) { $drops++ }

        foreach ($row in $rows) {
            $f = $row -split ','
            $addr = [long]$f[0]
            if ($addr -eq $ply) { continue }
            $act = [int]$f[1]
            $d = [int]$f[2]
            $namePtr = [uint32]$f[3]
            $gone = [int]$f[4]

            if (-not $names.ContainsKey($addr)) {
                $name = ""
                if ($namePtr -gt 0x10000 -and $namePtr -lt 0x7FFF0000) {
                    $nb = [Swing]::Read($h, $namePtr, 32)
                    if ($nb) {
                        $end = [Array]::IndexOf($nb, [byte]0)
                        if ($end -lt 0) { $end = 32 }
                        if ($end -gt 0) {
                            $name = [Text.Encoding]::GetEncoding(950).GetString($nb, 0, $end)
                        }
                    }
                }
                if ($name -match '^(.*)#(\d+):(\d+)$') { $name = $Matches[1] }
                $names[$addr] = $name
            }

            $key = "{0:X2}" -f $act
            if (-not $always.ContainsKey($key)) { $always[$key] = 0 }
            $always[$key]++
            if ($hurt) {
                if (-not $whenHit.ContainsKey($key)) { $whenHit[$key] = 0 }
                $whenHit[$key]++
                "  hp {0} -> {1}   {2,-10} d={3} act=0x{4} gone={5}" -f `
                    $lastHp, $hp, $names[$addr], $d, $key, $gone
            }

            # How long each record spends in each action, to tell a state that means
            # "dying" from one that is simply common.
            $seen = "{0:X8}/{1:X2}" -f $addr, $act
            if (-not $lives.ContainsKey($seen)) { $lives[$seen] = 0 }
            $lives[$seen]++
            $goneBy[("{0:X8}" -f $addr)] = $gone
        }

        $lastHp = $hp
        Start-Sleep -Milliseconds 150
    }

    ""
    "samples $samples, hit-point drops $drops"
    ""
    "action codes seen on close monsters (all samples / on the samples that hurt):"
    foreach ($key in ($always.Keys | Sort-Object)) {
        $hit = 0
        if ($whenHit.ContainsKey($key)) { $hit = $whenHit[$key] }
        "  0x{0}  {1,6}  {2,6}" -f $key, $always[$key], $hit
    }

    ""
    "how long each record held each action (a death should be brief, and end gone):"
    foreach ($k in ($lives.Keys | Sort-Object)) {
        $parts = $k -split '/'
        $addr = $parts[0]
        $name = ""
        $key = [Convert]::ToInt64($addr, 16)
        if ($names.ContainsKey($key)) { $name = $names[$key] }
        "  0x{0}  {1,-10}  act=0x{2}  {3,4} samples  gone={4}" -f `
            $addr, $name, $parts[1], $lives[$k], $goneBy[$addr]
    }
}
finally { [void][Swing]::CloseHandle($h) }
