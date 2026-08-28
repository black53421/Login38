# Watch whether the collision bit follows a moving entity.
#
# If bit 0 belongs to the map, an entity walking off a cell leaves it exactly as it was.
# If it belongs to the entity, the bit travels. Sampling the same entity's cell across
# several ticks is the only way to tell the two apart from outside.
param(
    [Parameter(Mandatory=$true)][int]$TargetPid,
    [int]$Samples = 12,
    [int]$IntervalMs = 500,
    [int]$Radius = 25
)

$sig = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class W {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(int a, bool i, int p);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out IntPtr r);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] public static extern int VirtualQueryEx(IntPtr h, IntPtr a, out MBI m, int l);
    [StructLayout(LayoutKind.Sequential)] public struct MBI {
        public IntPtr BaseAddress, AllocationBase; public int AllocationProtect;
        public IntPtr RegionSize; public int State, Protect, Type;
    }
    public static byte[] Read(IntPtr h, long a, int s) {
        byte[] b = new byte[s]; IntPtr g;
        if (!ReadProcessMemory(h, new IntPtr(a), b, s, out g) || g.ToInt32() != s) return null;
        return b;
    }
    public static uint U32(IntPtr h, long a) { var b = Read(h, a, 4); return b == null ? 0 : BitConverter.ToUInt32(b, 0); }
    public static List<long> Find(IntPtr h, uint px, uint py, int radius) {
        var hits = new List<long>(); long p = 0x10000; MBI m;
        uint xlo = (uint)(px-radius), xhi = (uint)(px+radius), ylo = (uint)(py-radius), yhi = (uint)(py+radius);
        while (p < 0x7FFF0000) {
            if (VirtualQueryEx(h, new IntPtr(p), out m, Marshal.SizeOf(typeof(MBI))) == 0) break;
            long size = m.RegionSize.ToInt64();
            if (m.State == 0x1000 && (m.Protect == 0x04 || m.Protect == 0x40) && size > 0 && size <= 64*1024*1024) {
                byte[] buf = new byte[size]; IntPtr g;
                if (ReadProcessMemory(h, m.BaseAddress, buf, (int)size, out g) && g.ToInt64() == size) {
                    long b0 = m.BaseAddress.ToInt64();
                    for (int o = 0; o + 0x130 <= size; o += 4) {
                        uint vt = BitConverter.ToUInt32(buf, o);
                        if (vt != 0x008DC08C) continue;
                        uint x = BitConverter.ToUInt32(buf, o+0x34); if (x < xlo || x > xhi) continue;
                        uint y = BitConverter.ToUInt32(buf, o+0x38); if (y < ylo || y > yhi) continue;
                        hits.Add(b0 + o);
                    }
                }
            }
            p = m.BaseAddress.ToInt64() + (size > 0 ? size : 0x1000);
        }
        return hits;
    }
}
'@
Add-Type -TypeDefinition $sig

$h = [W]::OpenProcess(0x0410, $false, $TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($TargetPid) failed" }
try {
    $ply = [W]::U32($h, 0x00C2D2B8)
    $px  = [W]::U32($h, $ply + 0x34); $py = [W]::U32($h, $ply + 0x38)
    # The player counts: if occupancy were a thing, walking off a cell would clear it.
    $watch = [W]::Find($h, $px, $py, $Radius)
    "watching $($watch.Count) entities around ($px,$py), player included"
    "walk around now - the test needs something to change cell"

    # cell index -> the flag words this cell has shown, and which entity was on it
    $seen = @{}
    $moved = @{}
    foreach ($n in 1..$Samples) {
        $grid = [W]::U32($h, 0x00ABF4C0)
        $ox   = [W]::U32($h, 0x00ABF978); $oy = [W]::U32($h, 0x00ABF97C)
        foreach ($e in $watch) {
            $b = [W]::Read($h, $e, 0x40); if (-not $b) { continue }
            $x = [BitConverter]::ToUInt32($b, 0x34); $y = [BitConverter]::ToUInt32($b, 0x38)
            $i = ($y - $oy) * 0x100 + ($x - $ox)
            if ($i -lt 0 -or $i -ge 0x8000) { continue }
            $c = [W]::Read($h, $grid + $i * 0x14, 8); if (-not $c) { continue }
            $fl = [BitConverter]::ToUInt16($c, 4)
            $key = "$i"
            if (-not $seen.ContainsKey($key)) { $seen[$key] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$seen[$key].Add(("0x{0:X4}" -f $fl))
            if (-not $moved.ContainsKey($e)) { $moved[$e] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$moved[$e].Add("$i")
        }
        if ($n -lt $Samples) { Start-Sleep -Milliseconds $IntervalMs }
    }

    $movers = ($moved.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 })
    "entities that changed cell during the watch: $($movers.Count)"
    ""

    # Which classes actually move. Scenery never does; an actor eventually will.
    $tableA = [W]::U32($h, 0x009A8F18)
    "per-entity motion and class:"
    $rows = foreach ($e in $watch) {
        $b = [W]::Read($h, $e, 0x130); if (-not $b) { continue }
        $sprite = [BitConverter]::ToUInt16($b, 0x18)
        $baseA  = [W]::Read($h, $tableA + $sprite, 1)[0]
        [pscustomobject]@{
            Addr   = "0x{0:X8}" -f $e
            Sprite = $sprite
            BaseA  = "0x{0:X2}" -f $baseA
            B14    = "0x{0:X2}" -f $b[0x14]
            Cells  = $(if ($moved.ContainsKey($e)) { $moved[$e].Count } else { 0 })
            Moved  = $(if ($moved.ContainsKey($e) -and $moved[$e].Count -gt 1) { "YES" } else { "" })
            Self   = $(if ($e -eq $ply) { "*" } else { "" })
        }
    }
    $rows | Sort-Object BaseA, Sprite | Format-Table -AutoSize | Out-String -Width 160
    ""
    "cells visited, and every flag word seen on them:"
    $flip = 0
    foreach ($k in ($seen.Keys | Sort-Object { [int]$_ })) {
        $vals = ($seen[$k] | Sort-Object) -join " "
        $note = ""
        if ($seen[$k].Count -gt 1) { $note = "   <-- CHANGED"; $flip++ }
        "  cell {0,-6} {1}{2}" -f $k, $vals, $note
    }
    ""
    if ($flip -eq 0) {
        "No cell changed its flag word while entities walked over it."
        "=> bit 0 is map geometry, not occupancy."
    } else {
        "$flip cell(s) changed => the bit tracks something dynamic."
    }
}
finally { [void][W]::CloseHandle($h) }
