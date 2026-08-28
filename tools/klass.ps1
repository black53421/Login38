<#
.SYNOPSIS
Shows, for every nearby monster, whether the client will actually swing at it.

.DESCRIPTION
Walking up to something and not hitting it is not one condition, it is two. The client has
two attack paths and they do not agree:

  ReTarget_fromHover, once in range, needs  spriteTypeTable[sprite] == 9.
  WalkEngine_tick's gate needs             Entity_combatTypeClass(entity) in {6,0xC,0xF,0x12},
                                           or == 0xE, or (+0x08 == 1 and +0x27 == 1).

A monster that satisfies neither is one the character walks to and stands in front of. This
computes the classifier the same way 0x5AE9xx does and prints every field that feeds it, so
which of the two is failing can be read rather than guessed.

Run it while the character is standing in front of something it will not hit.

The Hittable column is wrong, and the Class column is right. Measured against a live
client on 2026-08-28: eighteen monsters, every one classified 0x0A and every one
reported Hittable=NO, while the character was killing them in melee at that moment.

0x0A is the correct class for a monster, and the mistake was in what was done with it.
ReTarget_fromHover at 0x4F5DE0 sends a class-10 entity down its FIRST branch, which
never reaches the in-range test quoted above:

    if ((hover == DAT_00ABF34C || class == 10 || ...) && FUN_005AE780(hover))
        g_interaction_mode = DAT_009AB627 ? 0 : (DAT_009AB321 ? 2 : 1);

So a monster is chased in interaction mode 1, whose arrival distance comes from
DAT_008D2CB8[weaponClass] - see tools\reach.ps1 - and not in mode 3 at range 1. The
description above describes the branch that things which are NOT monsters take.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid,
    [int]$Radius = 30
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class Klass
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(
        IntPtr h, IntPtr a, byte[] b, int n, out int got);

    public static IntPtr Open(int pid) { return OpenProcess(0x0410, false, pid); }

    public static byte[] Read(IntPtr h, long a, int n)
    {
        byte[] b = new byte[n];
        int got;
        if (!ReadProcessMemory(h, new IntPtr(a), b, n, out got)) return null;
        return b;
    }

    public static uint U32(IntPtr h, long a)
    {
        byte[] b = Read(h, a, 4);
        return b == null ? 0u : BitConverter.ToUInt32(b, 0);
    }

    // Records within Radius, as address,x,y per entry.
    public static string Find(IntPtr h, uint vtable, int px, int py, int radius)
    {
        System.Text.StringBuilder found = new System.Text.StringBuilder();
        byte[] page = new byte[0x10000];
        for (long at = 0x02000000; at < 0x40000000; at += 0x10000)
        {
            int got;
            if (!ReadProcessMemory(h, new IntPtr(at), page, page.Length, out got)) continue;
            for (int i = 0; i + 0x200 < page.Length; i += 4)
            {
                if (BitConverter.ToUInt32(page, i) != vtable) continue;
                int x = BitConverter.ToInt32(page, i + 0x34);
                int y = BitConverter.ToInt32(page, i + 0x38);
                if (Math.Abs(x - px) > radius || Math.Abs(y - py) > radius) continue;
                found.Append(at + i).Append(';');
            }
        }
        return found.ToString();
    }
}
'@

Add-Type -TypeDefinition $sig

$h = [Klass]::Open($TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open $TargetPid" }

function Get-Text([long]$pointer) {
    if ($pointer -le 0x10000 -or $pointer -ge 0x7FFF0000) { return "" }
    $b = [Klass]::Read($h, $pointer, 48)
    if (-not $b) { return "" }
    $end = [Array]::IndexOf($b, [byte]0)
    if ($end -lt 0) { $end = 48 }
    if ($end -eq 0) { return "" }
    return [Text.Encoding]::GetEncoding(950).GetString($b, 0, $end)
}

try {
    $types  = [Klass]::U32($h, 0x009A8F18)   # per-sprite type, the classifier's starting point
    $second = [Klass]::U32($h, 0x009A8F08)   # the other per-sprite table it consults
    $ply    = [Klass]::U32($h, 0x00C2D2B8)
    if ($ply -eq 0) { throw "no local player" }

    $pb = [Klass]::Read($h, $ply, 0x140)
    $px = [BitConverter]::ToInt32($pb, 0x34)
    $py = [BitConverter]::ToInt32($pb, 0x38)
    $playerName = Get-Text ([BitConverter]::ToUInt32($pb, 0x60))

    "player 0x{0:X8} `"{1}`" at ({2},{3})" -f $ply, $playerName, $px, $py
    "sprite type table 0x{0:X8}   second table 0x{1:X8}" -f $types, $second
    ""

    $rows = ([Klass]::Find($h, 0x008DC08C, $px, $py, $Radius) -split ';') | Where-Object { $_ }

    $out = foreach ($row in $rows) {
        $addr = [long]$row
        if ($addr -eq $ply) { continue }
        $b = [Klass]::Read($h, $addr, 0x140)
        if (-not $b) { continue }

        $sprite   = [BitConverter]::ToUInt16($b, 0x18)
        $state14  = $b[0x14]
        $flag27   = $b[0x27]
        $state2b  = $b[0x2B]
        $flag58   = $b[0x58]
        $flag12d  = $b[0x12D]
        $flag08   = $b[0x08]
        $remote   = Get-Text ([BitConverter]::ToUInt32($b, 0x6C))
        $label    = Get-Text ([BitConverter]::ToUInt32($b, 0x60))
        if ($label -match '^(.*)#(\d+):(\d+)$') { $label = $Matches[1] }

        $tb = [Klass]::Read($h, $types + $sprite, 1)
        if (-not $tb) { continue }
        $spriteType = $tb[0]

        $sb = [Klass]::Read($h, $second + $sprite, 1)
        $secondType = if ($sb) { $sb[0] } else { 0 }

        # Only entities, and only the ones the launcher would consider at all.
        if ($spriteType -eq 0 -or $label -eq "") { continue }

        # Entity_combatTypeClass, transcribed. FUN_004f94d0 is taken as false: it gates a
        # branch that only fires for sprite type 0x0B, which is not what a monster is.
        $k = $spriteType
        if ($k -eq 0x0B) {
            $k = if ($state14 -lt 0x25 -and $state14 -ne 0x1C) { 0x0B } else { 1 }
        }
        else {
            if ($flag27 -eq 0) {
                if ($flag12d -ne 0) { $k = 0x11 }
            }
            else { $k = 5 }

            if ($k -eq 0x0A) {
                if ($flag58 -eq 0) {
                    if ($state14 -eq 8) { $k = 2 }
                    elseif ($remote -eq "") {
                        if ($secondType -eq 2 -and $state2b -eq 2) { $k = 1 }
                    }
                    else { $k = if ($remote -eq $playerName) { 5 } else { 6 } }
                }
                else { $k = 3 }
            }
            elseif ($k -notin @(4, 0x0E, 0x0F, 8, 7, 9, 0, 0x0D, 0x10)) {
                if ($k -in @(5, 6, 0x0C, 0x12)) {
                    if ($flag58 -ne 0 -or $state14 -eq 8) { $k = 3 }
                }
                elseif ($k -ne 0x13) { $k = 1 }
            }
        }

        $byRelock = ($spriteType -eq 9)
        $byEngine = ($k -in @(6, 0x0C, 0x0F, 0x12)) -or ($k -eq 0x0E) -or ($flag08 -eq 1 -and $flag27 -eq 1)

        [pscustomobject]@{
            Name    = $label
            Dist    = [Math]::Max([Math]::Abs([BitConverter]::ToInt32($b, 0x34) - $px),
                                  [Math]::Abs([BitConverter]::ToInt32($b, 0x38) - $py))
            Sprite  = "0x{0:X4}" -f $sprite
            Type    = "0x{0:X2}" -f $spriteType
            Class   = "0x{0:X2}" -f $k
            Relock  = if ($byRelock) { "yes" } else { "-" }
            Engine  = if ($byEngine) { "yes" } else { "-" }
            Hittable = if ($byRelock -or $byEngine) { "YES" } else { "NO" }
            S14     = "0x{0:X2}" -f $state14
            F27     = $flag27
            F58     = $flag58
            F12D    = $flag12d
            F08     = $flag08
            S2B     = $state2b
            Owner   = $remote
        }
    }

    $out | Sort-Object Dist | Format-Table -AutoSize | Out-String -Width 220

    ""
    "Hittable=NO means the client refuses both paths: the character will walk to it and stand there."
}
finally { [void][Klass]::CloseHandle($h) }
