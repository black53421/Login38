Login38 Companion Collision Probe v3.7
======================================

Base
----
Probe v3.6 + CS1503 fix.
This version keeps the environment-variable activation path and does not include the CLI switch experiment.

Goal
----
Trace the movement-specific dynamic blocker path around:

  0x004F5CD5  mov ecx,[eax]
  0x004F5CD7  call 0x005AF010
  0x004F5CDC  movzx edx,al

Probe v3.7 also traces the classification result inside 0x005AF010 at 0x005AF021.
It does not change the classifier result or the movement decision.

Trace sites
-----------
walk-sanity
  hook   0x005A51A0
  sanity counter only

movement-blocker
  hook   0x004F5CD5
  replays mov ecx,[eax] + call 0x005AF010 in the code cave
  captures:
    entity pointer
    gfx
    owner name pointer
    local-player name pointer
    entity x/y
    destination x/y from the 0x004F5A60 frame
    heading
    0x005AF010 return AL

blocker-classifier
  hook   0x005AF021
  captures:
    caller return address
    entity pointer from [ebp-8]
    EntityCombatTypeClass result in AL
    gfx / owner / local-player relation

Output
------
<GameDirectory>\Login38-Diagnostics\companion-collision-v3.7\<timestamp-pid>\

Important files:
  dynamic-blocker-trace-v3.7.csv
  probe-heartbeat-v3.7.csv
  manifest.txt
  client-unpacked-*.bin

Enable
------
Keep using the environment-variable method that is already confirmed working:

  setx LOGIN38_WRITE_LOGS 1
  setx LOGIN38_COMPANION_COLLISION_PROBE 1

Open a new shell before launching Login38.

Suggested test sequence
-----------------------
1. Walk on empty ground for a few seconds.
2. Repeatedly attempt to walk into Own Pet.
3. Repeatedly attempt to walk into Own Summon.
4. Repeatedly attempt to walk into a wild Monster.
5. Repeatedly attempt to walk into a static NPC.
6. Walk through a Magic Doll several times.
7. Close the client and zip the newest companion-collision-v3.7 directory.

Expected discriminator
----------------------
The useful rows are movement-blocker + blocker-classifier pairs.
Expected hypothesis to verify:

  Own Pet/Summon:
    owner_name == local_name
    class = 6
    movement-blocker result = 0

  Normal blocking Monster/NPC:
    result = 0

  Magic Doll / non-blocking entity:
    result = 1

Do not treat this table as confirmed until runtime samples show it.
