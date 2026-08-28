<#
.SYNOPSIS
Dumps the client's own attack-reach table and the entry the equipped weapon selects.

.DESCRIPTION
ComputeStepHeading at 0x5A4D60, in interaction mode 1 — the mode a monster is chased in —
picks its arrival radius like this:

    local_38 = 1;
    if (weaponClass < 0x58) local_38 = DAT_008D2CB8[weaponClass];
    if (DAT_00C2D2CA != 0)  local_38 = DAT_00C2D2CA;
    InRange_check(player, dest, local_38 + targetSize)

So the table is what decides how close the character walks before it starts swinging, and
0xC2D2CA is an override the server is meant to send and this one never does. Where the
table disagrees with the server's own range check the client stops out of reach, fires, and
the server drops the packet without a word — which from outside is a character that has
locked on to something and will not walk to it.

.PARAMETER TargetPid
The client process. Not $Pid, which PowerShell owns.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetPid
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class ReachPeek
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
        byte[] buffer = new byte[n];
        int read;
        if (!ReadProcessMemory(h, new IntPtr(a), buffer, n, out read) || read != n) { return null; }
        return buffer;
    }
}
'@

if (-not ('ReachPeek' -as [type])) { Add-Type -TypeDefinition $sig }

$handle = [ReachPeek]::Open($TargetPid)
if ($handle -eq [IntPtr]::Zero) { throw "could not open pid $TargetPid" }

function Read-U32([long]$address) {
    $bytes = [ReachPeek]::Read($handle, $address, 4)
    if ($null -eq $bytes) { return $null }
    return [BitConverter]::ToUInt32($bytes, 0)
}

# The client keeps these three behind a key array: value = keys[[addr] ^ 0xC0017921] ^ salt,
# with keys at [addr+4] and the salt at [addr+8].
function Read-Obfuscated([long]$address) {
    $index = Read-U32 $address
    $keys = Read-U32 ($address + 4)
    $salt = Read-U32 ($address + 8)
    if ($null -eq $index -or $null -eq $keys -or $null -eq $salt) { return $null }
    $slot = $keys + (($index -bxor 0xC0017921) * 4)
    $stored = Read-U32 $slot
    if ($null -eq $stored) { return $null }
    return $stored -bxor $salt
}

try {
    $weapon = Read-Obfuscated 0x00BDC7D4
    $sprite = Read-Obfuscated 0x00BDC7C8
    $override = [ReachPeek]::Read($handle, 0x00C2D2CA, 1)

    Write-Host ''
    Write-Host "weapon class (0xBDC7D4) = $weapon"
    Write-Host "sprite       (0xBDC7C8) = 0x$('{0:X}' -f $sprite)"
    Write-Host "override     (0xC2D2CA) = $($override[0])   # 0 = table wins"

    if ($null -ne $weapon -and $weapon -lt 0x58) {
        $selected = Read-U32 (0x008D2CB8 + ($weapon * 4))
        Write-Host "table[$weapon]  (0x$('{0:X}' -f (0x008D2CB8 + $weapon * 4))) = $selected tiles   <-- in use"
    }

    Write-Host ''
    Write-Host 'DAT_008D2CB8, every entry that is not 1:'
    for ($i = 0; $i -lt 0x58; $i++) {
        $value = Read-U32 (0x008D2CB8 + ($i * 4))
        if ($null -ne $value -and $value -ne 1) {
            Write-Host ("  [{0,2}] = {1}" -f $i, $value)
        }
    }
}
finally {
    [void][ReachPeek]::CloseHandle($handle)
}
