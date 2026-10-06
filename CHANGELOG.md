# 2026-10-05 - optional low-latency F1-F4 worker

- Add Encoder switch `function_key_high_speed_enabled` for a dedicated F1-F4 worker.
- High-speed mode consumes key-down/key-up edges, ignores Windows auto-repeat, and uses monotonic repeat deadlines with the existing `function_key_cooldown_ms`.
- The worker runs at `AboveNormal` priority and wakes from keyboard events instead of waiting for the 100 ms AuxHost cadence.
- Add a shared action arbiter: waiting potion actions take priority over F1-F4, which take priority over ordinary helper actions; client `RemoteCall` entry points remain serialized.
- Before every fast hotkey action, re-check potion rules on a fresh player/bag snapshot. If recovery acts, defer the hotkey for one normal AuxHost cadence rather than dropping it.
- Make shared spell/dispatch state safe for the additional reader thread.

# 2026-10-04 - potion priority over F1-F4

- Automatic potion rules now run on every AuxHost pass, including passes woken early by F1-F4.
- When a potion rule actually sends an action, F1-F4 yield for that pass and keep the pending key press for the next pass.
- Potion cooldown behavior is unchanged; a potion that is still cooling down does not block F1-F4.

## 2026-10-01

### F1-F4 input latency

- Wake the existing AuxHost action thread immediately for each watched F1-F4 key-down, instead of waiting up to the 100 ms polling cadence.
- Keep Windows keyboard auto-repeat as the held-key event source; `function_key_cooldown_ms` remains the minimum action interval.
- Do not create a separate repeat timer or action thread, avoiding the pacing regression found in the previous event-driven experiment.

# Changelog

- F1～F4 巨集重複觸發間隔改由 Encoder 的 `function_key_cooldown_ms` 設定；預設 500 ms，可調 100～5000 ms，並以每個 game session 的 server config 個別套用。

## 2026-09-30 — entity-targeted magic scroll packet fix

- Match the client PacketSpy format for world-targeted magic scrolls: `cddhhc`, not `cdd`.
- Send the scroll object id, target object id, target X/Y, and the observed trailing zero byte.
- Read entity X/Y from record offsets `+0x34/+0x38` and reject coordinates that do not fit the packet's 16-bit `h` fields.
- Apply the entity packet only to `/IT` and `/IT=name`; keep `/IME`, `/IA`, `/IW`, and `/I=name` on the existing item-target `cdd` packet.

## 2026-09-30 — F-key magic scrolls can use the current target

- Make bare `/IT` resolve the client's current combat target instead of only opening an item target cursor.
- Prefer the active attack target and fall back to the client's hover/re-lock target.
- Resolve the client `LEntity*` pointer to the server-facing object id at record offset `+0x0C` before sending `UseOn`.
- Reject recycled, gone, dying, zero-id, or out-of-heap target records instead of sending a bad target id.
- Keep `/IT=name` unchanged; it still resolves a named entity through the existing heap scan.

## 2026-09-29 — faster potion and buff reaction

- Poll potion thresholds and buff effect state every 100 ms.
- Keep potion actions independently paced at 500 ms and allow only one successful potion-rule action per window.
- Pace helper skill casts independently at 500 ms while preserving the existing per-effect 5 s retry/backoff.
- Keep item-based helper entries independent from the skill-cast gate.
- Support potion rules using `/IME` targeted items such as healing scrolls, while keeping `/I`, `/M`, and `/ME` behavior.
- Pace helper item actions independently at 500 ms so multiple scroll/item buffs cannot burst in one 100 ms poll.
- Add targeted self-buff scroll choices and the full-heal magic scroll to the default helper lists.

## 2026-09-15 - Diagnostic receive analyzer

- Added an optional Server-to-Client packet recorder in front of the client dispatcher.
- Added one-shot dispatcher/range-check dumps and `ddhhh` descriptor xref discovery for opcode reverse engineering.
- The receive recorder shares the existing long-item-status dispatcher router so two detours never compete for the same entry point.

## 2026-09-13 - RangeSkill capability marker fix

- Correct the 3.80C `C_ServerVersion` client-version stack slot from `[esp+0x20]` to `[esp+0x1C]`, based on startup PacketSpy output for `"chdcddc"`.
- Guard the marker write with the observed caller return address `0x004E0EE1` and opcode `0x0E`.
- Add a byte-level regression test for the marker cave.

## 1.1.0

Automatic hunting, and the reason skills used to miss.

### Hunting

The helper can now fight on its own: pick a target, close to it, keep a skill rotation
going, retaliate when something hits first, and stop when health runs low. Its page in the
helper window appears only when the server list has `internal_bot_enabled` set — the
operator decides whether the build offers it at all.

### Casting was aimed at an address the client does not use

Every skill went out aimed at `0x0097C90C`, which nothing in the client reads. The cast
landed on whatever target the client happened to be holding, or on nothing — in which case
it armed the "choose a target" cursor and waited for a click. That is why skills seemed to
fire at monsters nobody chose, and why they sometimes did not fire at all.

Casting is now assembled from the client's own routines rather than dispatched through its
spell book entry point: the cast delay, the packet, and both cooldown stamps, with none of
the words that decide what the player's next click means. Typing or browsing a bag while
the helper casts no longer arms the cursor, and where the mouse is pointing no longer
steers what the helper casts at. The rate is bounded by the client's own cooldown.

### The client says why a cast was refused, so the helper reads it

The server answers a refused cast with a numbered message and the client writes it into its
own chat window. The helper watches for the nine that mean a cast did not happen — too
heavy, out of mana or health, nothing in line, interrupted, and the rest — and switches to
the weapon until the condition clears. Each reason is held against the thing that would end
it: weight against the load the client shows, mana and health against real recovery, line
of sight against the character having moved. The rest are answered by letting one cast
through every so often, waiting twice as long each time it draws the same answer.

Being overweight used to cost a whole hunt: every cast was refused, silently, several times
a second, and nothing was reading the reason.

### Also

- An "ATS" badge drawn inside the game's own frame while the hunt is running.
- Hidden monsters — burrowed, sunk, invisible, flying — are no longer chosen as targets.
- Skills cast from five tiles, which is as far as every ranged skill a character can learn
  still reaches.

## 1.0.1

Scaled present, overlay lifetime, client-owned toggles and exit latency.

## 1.0.0

First public release.
