# Q214 · Playback's first loop: buffer, warm at idle, and what stopping draws from — **answered 2026-10-08**

**Answered, all three as recommended:**

1. **A frame that is not ready:** buffer, then play. When the range isn't
   rendered, the timeline shows it filling, and playback starts at full speed
   once it can keep up. Timing is always true.
2. **Background rendering at idle:** yes, on all cores but one. It pauses the
   moment the artist draws or plays, and Configure → Performance can switch it
   off.
3. **What stopping draws from:** the frame stays drawn from the playback tiles
   while the still canvas's own copies are made in the background.
   Merging the two caches into one was the alternative. The owner chose this
   *"for now"* and asked whether merging should be done down the line. **Yes.**
   Merging is the right end state and is recorded as a later phase in
   `docs/DESIGN-playback-first-loop.md`. It waits for the compositor work it
   would collide with (B125 and B167), and is not a separate question.

Raised by the owner's report that initial playback stalls. The performance lab
(`first-playback`, owner-shaped document, 2026-10-07) measured it. Everything
below is in the design note.

## What it costs

- **Buffering:** a visible wait before the first loop after an edit,
  roughly 3–4 s on the test document with idle warming in place, against today's
  1–4 s *per frame*. The alternative that was declined, keeping the clock and
  holding frames, starts at once and shows false timing on the first loop.
- **Warming on all cores but one:** CPU, so fan noise and battery, for a few
  seconds after each edit. One core would have been about 10× slower (~23 s
  after opening on the test document).
- **Staying on tiles at stop:** none visible. Both routes render the same
  record with the same seeded dynamics. It keeps two caches alive until the
  merge phase.

## Correction, 2026-10-08 (phase 1)

The cost of answer 2 was understated. "All cores but one" was offered on the
assumption that renders scale with cores ("3–4 s" for the test document; one core
"about 10× slower"). Phase 1 measured **1.6×** on 15 workers: the renders spend
their time allocating and collecting garbage (24 MB per drawing) and re-stamping
each stroke once per tile (3×). The answer stands; what it costs is now known. It
needs phase 2a (cheaper renders) before it can deliver, and the worker count will
be set from measurement rather than from the core count.
