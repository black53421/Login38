Login38 Companion Collision Probe v3.6
======================================

Base
----
Apply this increment on top of Probe v3.5 Walk Control Flow.
The environment-variable activation path is retained; the CLI switch experiment is not included.

Purpose
-------
Probe v3.6 traces the movement-specific passability test at 0x004F5910 and its
ComputeStepHeading caller without changing the client's movement decision.

Static review of the captured 3.80c runtime image shows 0x004F5910 reading the
map/tile table at 0x00ABF4C0 and returning AL=1/0 from bit tests. No dynamic
entity lookup is visible inside this function. v3.6 is intended to verify this
at runtime before moving to the next collision stage.

Trace sites
-----------
- 0x005A51A0 walk-sanity
- 0x005A4DCE step-heading-result
  Captures the result of the call from ComputeStepHeading to 0x004F5910 and the
  direction being evaluated.
- 0x004F5A58 tileblocked-return
  Captures function caller, x, y, heading, and AL immediately before the
  original function epilogue/ret.

Outputs
-------
<GameDirectory>\Login38-Diagnostics\companion-collision-v3.6\<timestamp-pid>\

Important files:
- manifest.txt
- tile-passability-trace-v3.6.csv
- probe-heartbeat-v3.6.csv
- client-unpacked-*.bin

CSV columns
-----------
utc,site,seq,seq_delta,caller,x,y,heading,result,local_x,local_y

Interpretation
--------------
result=0: 0x004F5910 considered that terrain/direction passable.
result=1: 0x004F5910 considered that terrain/direction blocked.

The key comparison is to attempt movement toward an Own Pet while standing on
otherwise open terrain. If the direction toward the Pet still returns result=0
while the Client refuses to send C_MoveChar, then Pet collision is downstream
of 0x004F5910 rather than inside this terrain test.

Suggested test
--------------
1. Walk on open ground for 5-10 seconds.
2. Repeatedly attempt to walk into Own Pet on open terrain.
3. Repeatedly attempt to walk into a Wild Monster on open terrain.
4. Repeatedly attempt to walk into a Static NPC.
5. Walk into a real wall/blocked terrain for comparison.
6. Pass through a Magic Doll.
7. Close the Client and provide the newest v3.6 output folder as ZIP.

Activation
----------
Use the environment-variable method already confirmed on the test machine:

  setx LOGIN38_WRITE_LOGS 1
  setx LOGIN38_COMPANION_COLLISION_PROBE 1

Open a new shell before launching Login38.
