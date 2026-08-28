# Changelog

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
