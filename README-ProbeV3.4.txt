Login38 Companion Collision Probe v3.4
======================================

Base
----
Probe v3.3 runtime-callers build. CLI switch experiment is not included.
Activation remains LOGIN38_COMPANION_COLLISION_PROBE=1.

Purpose
-------
Trace the exact 0x0040E790 dispatcher branch flow identified by Probe v3.3.
The probe does not alter classification, the 0x0040EC00 result, or branch decisions.

Trace sites
-----------
0x005A51A0  walk-sanity
0x0040E790  dispatcher entry
0x0040E7B1  classifier result (AL before store to [ebp-1])
0x0040E860  0x0040EC00 result (AL)
0x0040E88F  class 06/0C/0D/0F special branch
0x0040E8A0  normal-class branch
0x0040EB03  relative-test-false branch
0x004F5F72  [0x00ABF440] caller's 0x0040EC00 result

Output
------
Login38-Diagnostics\companion-collision-v3.4\<timestamp-pid>\

Important files:
  manifest.txt
  branch-trace-v3.4.csv
  probe-heartbeat-v3.4.csv
  collision-state.txt

branch-trace-v3.4.csv includes:
  entity pointer
  class
  0x0040EC00 result
  gfx / coordinates / owner name pointer
  local-player coordinates/name pointer
  [0x00ABF440] and [0x00ABF444]
  best-effort current-object gfx/x/y snapshot

Recommended test
----------------
1. Walk normally for a few seconds.
2. Repeatedly collide with own Pet.
3. Repeatedly collide with own Summon.
4. Repeatedly collide with a wild Monster.
5. Repeatedly collide with a static NPC.
6. Walk through a Magic Doll.
7. Close the client and provide the newest v3.4 directory as ZIP.

Notes
-----
The special-branch detour at 0x0040E88F contains a relative CALL in the displaced
instructions. Probe v3.4 re-emits that CALL with a recalculated rel32 rather than
copying the original bytes into the remote cave.
