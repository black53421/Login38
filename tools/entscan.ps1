# Find world entities near the player by walking the client's committed heap.
#
# An entity record starts with a vtable pointer into the client image and carries its
# tile position at +0x34/+0x38. Those two facts together are enough to find them without
# knowing any class: scan for a module-range first dword, then keep the ones standing
# near the player. Grouping the survivors by vtable is what tells the classes apart.
param(
    [Parameter(Mandatory=$true)][int]$TargetPid,
    [int]$Radius = 40,
    [int]$Dump = 0          # print this many raw header bytes per entity
)

$sig = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Scan {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")]
    public static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, int len);

    [StructLayout(LayoutKind.Sequential)]
    public struct MBI {
        public IntPtr BaseAddress, AllocationBase;
        public int AllocationProtect;
        public IntPtr RegionSize;
        public int State, Protect, Type;
    }

    public static byte[] Read(IntPtr h, long addr, int size) {
        byte[] b = new byte[size]; IntPtr got;
        if (!ReadProcessMemory(h, new IntPtr(addr), b, size, out got) || got.ToInt32() != size)
            throw new Exception(String.Format("read 0x{0:X}+{1} failed err {2}", addr, size, Marshal.GetLastWin32Error()));
        return b;
    }
    public static uint U32(IntPtr h, long addr) { return BitConverter.ToUInt32(Read(h, addr, 4), 0); }

    // Returns "addr,vtable,x,y" per hit.
    public static List<string> Find(IntPtr h, uint px, uint py, int radius) {
        var hits = new List<string>();
        long p = 0x10000, limit = 0x7FFF0000;
        MBI mbi;
        uint xlo = (uint)(px - radius), xhi = (uint)(px + radius);
        uint ylo = (uint)(py - radius), yhi = (uint)(py + radius);
        while (p < limit) {
            if (VirtualQueryEx(h, new IntPtr(p), out mbi, Marshal.SizeOf(typeof(MBI))) == 0) break;
            long size = mbi.RegionSize.ToInt64();
            bool usable = mbi.State == 0x1000                                  // MEM_COMMIT
                       && (mbi.Protect == 0x04 || mbi.Protect == 0x40)         // READWRITE
                       && (mbi.Protect & 0x100) == 0                           // not PAGE_GUARD
                       && size > 0 && size <= 64 * 1024 * 1024;
            if (usable) {
                byte[] buf = new byte[size]; IntPtr got;
                if (ReadProcessMemory(h, mbi.BaseAddress, buf, (int)size, out got) && got.ToInt64() == size) {
                    long baseAddr = mbi.BaseAddress.ToInt64();
                    for (int o = 0; o + 0x40 <= size; o += 4) {
                        uint vt = BitConverter.ToUInt32(buf, o);
                        if (vt < 0x00400000 || vt >= 0x00A00000) continue;
                        uint x = BitConverter.ToUInt32(buf, o + 0x34);
                        if (x < xlo || x > xhi) continue;
                        uint y = BitConverter.ToUInt32(buf, o + 0x38);
                        if (y < ylo || y > yhi) continue;
                        hits.Add(String.Format("{0},{1},{2},{3}", baseAddr + o, vt, x, y));
                    }
                }
            }
            p = mbi.BaseAddress.ToInt64() + (size > 0 ? size : 0x1000);
        }
        return hits;
    }
}
'@
Add-Type -TypeDefinition $sig

$h = [Scan]::OpenProcess(0x0410, $false, $TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($TargetPid) failed" }
try {
    $ply = [Scan]::U32($h, 0x00C2D2B8)
    $px  = [Scan]::U32($h, $ply + 0x34)
    $py  = [Scan]::U32($h, $ply + 0x38)
    "player 0x{0:X8} at ({1},{2}), radius {3}" -f $ply, $px, $py, $Radius
    ""

    # The client's own per-sprite type tables, behind Entity_combatTypeClass (0x5AEBE0).
    # Read as pointers first; if that lands nowhere, the globals are the arrays themselves.
    $tableA = [Scan]::U32($h, 0x009A8F18)
    $tableB = [Scan]::U32($h, 0x009A8F08)
    if ($tableA -lt 0x10000) { $tableA = 0x009A8F18 }
    if ($tableB -lt 0x10000) { $tableB = 0x009A8F08 }
    "type tables  A=0x{0:X8}  B=0x{1:X8}" -f $tableA, $tableB

    # The collision grid, so each entity can be shown with the cell it stands on.
    $grid = [Scan]::U32($h, 0x00ABF4C0)
    $ox   = [Scan]::U32($h, 0x00ABF978)
    $oy   = [Scan]::U32($h, 0x00ABF97C)

    $hits = [Scan]::Find($h, $px, $py, $Radius)
    $rows = foreach ($hit in $hits) {
        $f = $hit -split ','
        $addr = [long]$f[0]
        $b = [Scan]::Read($h, $addr, 0x130)
        $sprite  = [BitConverter]::ToUInt16($b, 0x18)      # high half is flags, not the id
        $state14 = $b[0x14]
        $flag27  = $b[0x27]
        $flag58  = $b[0x58]
        $state2b = $b[0x2B]
        $flag12d = $b[0x12D]
        $baseA   = [Scan]::Read($h, $tableA + $sprite, 1)[0]

        # +0x60 is the label: "name#objectid:sprite" on anything the world names.
        # (+0x6C is a different field, the one the classifier compares against the local
        # player; it is empty on monsters.)
        $namePtr = [BitConverter]::ToUInt32($b, 0x60)
        $name = ""
        if ($namePtr -gt 0x10000 -and $namePtr -lt 0x7FFF0000) {
            try {
                $nb = [Scan]::Read($h, $namePtr, 32)
                $end = [Array]::IndexOf($nb, [byte]0)
                if ($end -lt 0) { $end = 32 }
                if ($end -gt 0) { $name = [Text.Encoding]::GetEncoding(950).GetString($nb, 0, $end) }
            } catch { $name = "?" }
        }

        # What the classifier reads, read directly. Replicating 0x5AEBE0 does not pay:
        # a live monster comes out 0x0A, which the engine attacks through a later branch,
        # so the class alone never says "attackable". These five fields do.
        # The id a packet aims at is +0x0C. The number inside the +0x60 label is a
        # template id shared by every monster of that kind, which is not the same thing.
        $objId = [BitConverter]::ToUInt32($b, 0x0C)
        $tmpl  = ""
        if ($name -match '^(.*)#(\d+):(\d+)$') { $tmpl = $Matches[2]; $name = $Matches[1] }
        $isMonster = $baseA -eq 0x0A -and $flag27 -eq 0 -and $flag58 -eq 0 `
                     -and $state14 -ne 8 -and $name -ne "" -and $addr -ne $ply

        [pscustomobject]@{
            Addr   = "0x{0:X8}" -f $addr
            VTable = "0x{0:X8}" -f ([uint32]$f[1])
            Sprite = $sprite
            Name   = $name
            Id     = $objId
            Tmpl   = $tmpl
            BaseA  = "0x{0:X2}" -f $baseA
            F27    = "0x{0:X2}" -f $flag27
            F58    = "0x{0:X2}" -f $flag58
            Hunt   = $(if ($isMonster) { "YES" } else { "" })
            B14    = "0x{0:X2}" -f $b[0x14]
            B15    = "0x{0:X2}" -f $b[0x15]
            B17    = "0x{0:X2}" -f $b[0x17]
            B28    = "0x{0:X2}" -f $b[0x28]
            B2A    = "0x{0:X2}" -f $b[0x2A]
            B2B    = "0x{0:X2}" -f $b[0x2B]
            X      = [int]$f[2]
            Y      = [int]$f[3]
            Cell   = $(
                $i = ([int]$f[3] - $oy) * 0x100 + ([int]$f[2] - $ox)
                if ($i -ge 0 -and $i -lt 0x8000) {
                    $c = [Scan]::Read($h, $grid + $i * 0x14, 0x14)
                    $fl = [BitConverter]::ToUInt16($c, 4)
                    "0x{0:X4}{1}" -f $fl, $(if ($fl -band 1) { " BLOCKED" } else { "" })
                } else { "out-of-range" }
            )
            Dist   = [Math]::Max([Math]::Abs([int]$f[2] - $px), [Math]::Abs([int]$f[3] - $py))
            Self   = if ($addr -eq $ply) { "*" } else { "" }
        }
    }
    $rows | Sort-Object Dist |
        Select-Object Self, Sprite, Name, Id, Tmpl, F27, F58, B14, Hunt, Dist |
        Format-Table -AutoSize | Out-String -Width 220

    "by vtable:"
    $rows | Group-Object VTable | Sort-Object Count -Descending |
        ForEach-Object { "  {0}  x{1}   sprites: {2}" -f $_.Name, $_.Count, (($_.Group.Sprite | Sort-Object -Unique) -join " ") }

    if ($Dump -gt 0) {
        foreach ($r in ($rows | Sort-Object Dist | Select-Object -First $Dump)) {
            ""
            "{0}  vtable {1}  sprite {2}  ({3},{4})" -f $r.Addr, $r.VTable, $r.Sprite, $r.X, $r.Y
            $b = [Scan]::Read($h, [Convert]::ToInt64($r.Addr.Substring(2), 16), 0x60)
            for ($i = 0; $i -lt 0x60; $i += 16) {
                "  +{0:X2}  {1}" -f $i, (($b[$i..($i+15)] | ForEach-Object { "{0:x2}" -f $_ }) -join " ")
            }
        }
    }
}
finally { [void][Scan]::CloseHandle($h) }
