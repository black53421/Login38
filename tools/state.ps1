<#
.SYNOPSIS
Dumps every global the hunt depends on, plus the client's scheduler queue.

.DESCRIPTION
When the character stops and stays stopped, the question is which of the client's two
self-driving chains died and why nothing restarted it. That is eight globals and a queue,
and guessing at them from the outside has cost more time than reading them ever would.

Run it while the character is stuck, not afterwards.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid,
    [int]$Samples = 1,
    [int]$IntervalMs = 500
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class Peek
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(
        IntPtr handle, IntPtr address, byte[] buffer, int size, out int read);

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

    public static int U8(IntPtr h, long a)
    {
        byte[] b = Read(h, a, 1);
        return b == null ? -1 : b[0];
    }
}
'@

Add-Type -TypeDefinition $sig

$h = [Peek]::Open($TargetPid)
if ($h -eq [IntPtr]::Zero) { throw "cannot open $TargetPid" }

# What each one means, so a dump can be read without the decompiler open.
$flags = [ordered]@{
    'g_auto_attack       0xAC450C' = @(0x00AC450C, 'byte', 'master switch; both chains re-post only while set')
    'g_walk_target_valid 0x9AB3DF' = @(0x009AB3DF, 'byte', 'walk engine returns at once when clear')
    'g_attack_target     0xC2D2B4' = @(0x00C2D2B4, 'dword', 'who; only ReTarget_fromHover sets it')
    'g_hover_target      0xABF440' = @(0x00ABF440, 'dword', 'what the re-lock reads; our chase hook pins it')
    'g_interaction_mode  0x9AB31C' = @(0x009AB31C, 'dword', '3 = chase and attack; consumed on use')
    'attack_in_flight    0xABF33C' = @(0x00ABF33C, 'dword', 'non-zero makes ReTarget_fromHover a no-op')
    'autoattack_running  0x9AB320' = @(0x009AB320, 'byte', 'Attack_dispatch refuses while set; funcA forgets to lower it on two paths')
    'tick_pending        0xC2D29D' = @(0x00C2D29D, 'byte', 'a walk tick was posted by the tail')
    'stepped             0xC2D29E' = @(0x00C2D29E, 'byte', 'set after a step block ran')
    'suppress_relock     0xC2D29F' = @(0x00C2D29F, 'byte', 'set by the cancel routine')
    'kick_blocker_a      0xC2D2C8' = @(0x00C2D2C8, 'byte', 'ClickHandler refuses while set')
    'kick_blocker_b      0xC2D2C9' = @(0x00C2D2C9, 'byte', 'ClickHandler refuses while set')
    'next_attack_tick    0xC2D27C' = @(0x00C2D27C, 'dword', 'rewritten on every swing by both chains')
    'attack_reach        0xC2D2CA' = @(0x00C2D2CA, 'byte', 'how close the client thinks it must be; 0 = its default of 1')
    'dest_x              0x9ABCA4' = @(0x009ABCA4, 'dword', 'masked to even before the walk')
    'dest_y              0x9ABCA8' = @(0x009ABCA8, 'dword', '')
}

# Read through the client's own obfuscation: an index xored with a constant into a key
# array, then xored with a salt. Both gates below are stored that way.
$obfuscated = [ordered]@{
    'movement_blocked  0xBDC744' = @(0x00BDC744, 'walk engine refuses a step while non-zero')
    'attack_blocked    0xBDC984' = @(0x00BDC984, 'both halves of the attack chain give up while above zero')
    'form_or_state     0xBDC7D4' = @(0x00BDC7D4, 'indexes the per-form reach table at 0x8D2CB8 when below 0x58')
    'other_state       0xBDC7C8' = @(0x00BDC7C8, 'checked for 0x20/0x448/0x465 in both arrival tests')
}

try {
    for ($sample = 0; $sample -lt $Samples; $sample++) {
        if ($sample -gt 0) { Start-Sleep -Milliseconds $IntervalMs; "" }

        "=== sample $($sample + 1) ==="

        foreach ($name in $flags.Keys) {
            $spec = $flags[$name]
            $value = if ($spec[1] -eq 'byte') { [Peek]::U8($h, $spec[0]) } else { [Peek]::U32($h, $spec[0]) }
            "{0,-30} 0x{1:X8}  {2}" -f $name, $value, $spec[2]
        }

        ""
        foreach ($name in $obfuscated.Keys) {
            $spec = $obfuscated[$name]
            $index = [Peek]::U32($h, $spec[0]) -bxor 0xC0017921
            $keys  = [Peek]::U32($h, $spec[0] + 4)
            $salt  = [Peek]::U32($h, $spec[0] + 8)
            $value = if ($index -lt 16 -and $keys -ne 0) {
                [Peek]::U32($h, $keys + $index * 4) -bxor $salt
            } else { 'unreadable' }
            "{0,-30} {1,-10}  {2}" -f $name, $value, $spec[1]
        }
        "  either one non-zero is the client saying the character cannot act"

        $ply = [Peek]::U32($h, 0x00C2D2B8)
        if ($ply -ne 0) {
            $pb = [Peek]::Read($h, $ply, 0x140)
            if ($pb) {
                ""
                "player 0x{0:X8} at ({1},{2})  action=0x{3:X2}  heading={4}  anim_frame={5}  busy(+0x7c)={6}" -f `
                    $ply,
                    [BitConverter]::ToInt32($pb, 0x34), [BitConverter]::ToInt32($pb, 0x38),
                    $pb[0x14], $pb[0x15], $pb[0x17], [BitConverter]::ToInt32($pb, 0x7C)
                "  anim_frame or busy non-zero stops the walk engine deciding anything"
            }
        }

        # The queue the launcher reads to decide whether a step is already coming. A
        # WalkEngine_tick (0x5A51A0) or an AutoAttack_funcA (0x5A3010) in here is a chain
        # that is alive; neither, with g_auto_attack set, is the state that needs a kick.
        $count = [Peek]::U32($h, 0x00C2D218)
        $entries = [Peek]::U32($h, 0x00C2D218 + 8)

        ""
        "scheduler: count={0} entries=0x{1:X8}" -f $count, $entries

        if ($count -gt 0 -and $count -le 1024 -and $entries -ne 0) {
            $queue = [Peek]::Read($h, $entries, [int]$count * 12)
            if ($queue) {
                for ($i = 0; $i -lt $count; $i++) {
                    $due = [BitConverter]::ToUInt32($queue, $i * 12)
                    $callback = [BitConverter]::ToUInt32($queue, ($i * 12) + 4)
                    $note = switch ($callback) {
                        0x005A51A0 { 'WalkEngine_tick' }
                        0x005A3010 { 'AutoAttack_funcA' }
                        0x005A5130 { 'cancel everything' }
                        default    { '' }
                    }
                    "  [{0,2}] due={1,-12} callback=0x{2:X8}  {3}" -f $i, $due, $callback, $note
                }
            }
        }
        elseif ($count -gt 1024) {
            "  count is out of range, so the launcher reads this as 'busy' and never kicks"
        }
    }
}
finally { [void][Peek]::CloseHandle($h) }
