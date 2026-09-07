Login38 Companion Collision Probe v3.5
======================================

Baseline
--------
Probe v3.4 branch-flow source. Command-line activation experiment is NOT included.
Continue enabling with LOGIN38_COMPANION_COLLISION_PROBE=1 (and optionally LOGIN38_WRITE_LOGS=1).

Purpose
-------
Probe v3.5 stops guessing collision helper functions and maps the live control flow through
WalkEngineTick (0x005A51A0) using low-cost hit counters at instruction-aligned basic-block
checkpoints.

It also captures bytes BEFORE the v3.5 hooks are installed:
- walk-engine-prehook.bin       : 64 bytes starting at 0x005A51A0
- smooth-run-prehook.bin        : 16 bytes starting at 0x00449776

manifest.txt records both byte sequences. If SmoothRunPatch has installed its normal E9
at 0x00449776, the manifest also records the decoded jump target. This verifies whether
SmoothRun correlates with the movement path without changing SmoothRun itself.

Trace output
------------
Login38-Diagnostics\companion-collision-v3.5\<timestamp-pid>\

Important files:
- manifest.txt
- walk-control-flow-v3.5.csv
- probe-heartbeat-v3.5.csv
- walk-engine-prehook.bin
- smooth-run-prehook.bin
- client-unpacked-*.bin

Suggested test
--------------
1. Enter the game and walk normally for 10-20 seconds.
2. Repeatedly walk into your own Pet.
3. Repeatedly walk into a wild monster and a static NPC.
4. Walk through a Magic Doll.
5. Exit the client and provide the full v3.5 diagnostic directory.

Interpretation
--------------
The per-site counters show the deepest WalkEngineTick checkpoint actually reached.
For example, entry increasing while initial-guards-passed remains zero means execution
leaves before 0x005A522C. candidate-classify-pre increasing confirms the live path reaches
the candidate classifier at 0x005A5363.

The probe changes no collision decision; every checkpoint replays the displaced original
instructions before returning to the client.
