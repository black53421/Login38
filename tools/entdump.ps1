# Print the exact fields Entity_combatTypeClass (0x5AEBE0) reads, for named entities.
# Field offsets are lifted from the disassembly, not from a struct guess.
param(
    [Parameter(Mandatory=$true)][int]$TargetPid,
    [Parameter(Mandatory=$true)][long[]]$Address
)

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class D {
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool i, int p);
  [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out IntPtr r);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  public static byte[] R(IntPtr h, long a, int s){
    byte[] b = new byte[s]; IntPtr g;
    if (!ReadProcessMemory(h, new IntPtr(a), b, s, out g) || g.ToInt32() != s) return null;
    return b;
  }
  public static uint U(IntPtr h, long a){ var b = R(h, a, 4); return b == null ? 0u : BitConverter.ToUInt32(b, 0); }
  public static string S(IntPtr h, long p){
    if (p <= 0x10000 || p >= 0x7FFF0000) return "<null>";
    var b = R(h, p, 40); if (b == null) return "<unreadable>";
    int e = Array.IndexOf(b, (byte)0); if (e < 0) e = 40;
    if (e == 0) return "<empty>";
    return System.Text.Encoding.GetEncoding(950).GetString(b, 0, e);
  }
}
'@

$h = [D]::OpenProcess(0x0410, $false, $TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess($TargetPid) failed" }
try {
    $tA  = [D]::U($h, 0x009A8F18)
    $tB  = [D]::U($h, 0x009A8F08)
    $ply = [D]::U($h, 0x00C2D2B8)
    "tables A=0x{0:X8} B=0x{1:X8}   player=0x{2:X8} name='{3}'" -f $tA, $tB, $ply, [D]::S($h, [D]::U($h, $ply + 0x60))
    ""
    foreach ($a in $Address) {
        $addr = $a
        $b = [D]::R($h, $addr, 0x130)
        if ($null -eq $b) { "0x{0:X8}  <unreadable>" -f $addr; continue }

        $sp   = [BitConverter]::ToInt16($b, 0x18)
        $bA   = [D]::R($h, $tA + $sp, 1)
        $bB   = [D]::R($h, $tB + $sp, 1)
        $nm6C = [D]::S($h, [BitConverter]::ToUInt32($b, 0x6C))
        $nm60 = [D]::S($h, [BitConverter]::ToUInt32($b, 0x60))

        "0x{0:X8} sprite {1,6}" -f $addr, $sp
        "    A[sprite]=0x{0:X2}  B[sprite]=0x{1:X2}" -f $(if ($bA) { $bA[0] } else { 255 }), $(if ($bB) { $bB[0] } else { 255 })
        "    +0x14={0:X2}  +0x27={1:X2}  +0x58={2:X2}  +0x2B={3:X2}  +0x12D={4:X2}" -f $b[0x14], $b[0x27], $b[0x58], $b[0x2B], $b[0x12D]
        "    +0x60='{0}'   +0x6C='{1}'" -f $nm60, $nm6C
        "    pos ({0},{1})" -f [BitConverter]::ToUInt32($b, 0x34), [BitConverter]::ToUInt32($b, 0x38)
    }
}
finally { [void][D]::CloseHandle($h) }
