# Playback's first loop

The owner: *initial playback stalls*. This note says why, what the owner decided
about it (Q214), and the order to build it in. It was written before any of it was
built, so the owner is agreeing to a shape rather than to a diff.

It sits **beside** `DESIGN-tiled-compositor.md` (B167), not on top of it. That note
is about *compositing* a playback frame from layers that are already tiled, and it
plans a cache of finished composites. This note is about the step before:
**turning each drawing into tiles at all**. On a cold scene that step is nearly all
of the wait.

## What was measured

The performance lab's `first-playback` scenario uses the owner-shaped fixture:
1920×1080, 10 drawing layers, 63 drawings × 20 strokes × 150 points, 21 frames on
2s. Each run plays for 3 s from a freshly opened document, stops, then plays again.
It is real input against the Release build on current `main`, 2026-10-07.

| | first play | second play |
|---|---|---|
| Space → first frame on screen | **3.3 s** | 2–4 ms |
| frames shown in 3 s (72 expected at 24 fps) | **2** | 6.5–74 |
| longest gap between two frames | **4.3 s** | 90 ms (warm) |

- **The first tick is one publish of 3.3 s.** It holds **11 cache misses, each a
  drawing rendered into tiles on the UI thread at ~300–360 ms** (`raster.miss.tiles`).
- **In the same 3 s the prewarm worker rendered nothing** (`prewarm`: 0).
- **Stopping takes 2.6 s:** the still canvas wants its own copies of the frame's
  drawings, which playback never made.
- **The whole scene is ~63 × 0.36 ≈ 23 s of rendering on one core.** The first
  loop pays it on the UI thread, a few drawings per tick.

### Why, read off the code

1. **Pressing play changes the cache, not just the compositor.**
   - Playback composes from `TileFrameCache`; the paused canvas composes through
     the ring from `FrameBitmapCache` (`tileModeOn = IsPlaying`, B167).
   - So nothing the paused canvas rendered helps playback. Opening the document
     rendered 23 drawings as bitmaps, and the first tick rendered them again as
     tiles.
   - The reverse is why stopping is slow.
2. **The prewarmer starts too late and is too small for a cold scene.**
   - `RequestPlaybackPrewarm` returns at once unless `IsPlaying`, and is called at
     the *end* of a publish. So the first tick has no warm frames by construction.
   - It looks `Lookahead = 3` frames ahead with **one** worker. At ~0.36 s a
     drawing that is ~3 drawings a second, while a cold loop at 24 fps asks for
     dozens.
   - One worker was right for its stated purpose: not competing with the tick for
     cores while playing (B29). It cannot warm a scene from cold.
3. **A miss blocks the tick.** A frame whose tiles aren't there is rendered inside
   `PublishSnapshot` on the UI thread, so the app is frozen while the first loop
   crawls.

## What the owner decided (Q214)

1. **Buffer, then play.**
   - If the range isn't ready, the timeline shows it filling, and playback starts
     at full speed once it can keep up.
   - Timing on screen is always true, which is the point of an animator's
     playback. The declined alternative (keep the clock, hold frames) starts at
     once and shows false timing on the first loop.
2. **Warm at idle, on all cores but one.**
   - After opening, or after an edit, idle time renders every drawing in the
     playback range as tiles.
   - It pauses the moment a stroke begins or play is pressed, and a Configure →
     Performance switch turns it off.
3. **Stopping stays on tiles until the stills are ready.** The stopped frame is
   drawn from the playback tiles while the still canvas's copies are made in the
   background, then it swaps.

**Merging the two caches is the end state, and it is deliberately later.**
- Once the paused canvas also composes from tiles there is one cache: opening
  warms playback, and stopping costs nothing.
- It changes the composite path that B125 (GPU compositing) and B167 are already
  reshaping. Doing it alongside them would mean three plans editing one method.
  So it comes after them, or folds into them, and is listed as phase 5 below so
  it is not lost.

## The shape

### One warmer, two speeds

`FramePrewarmer` grows, rather than a second worker system arriving beside it:

- **Idle mode:** a pool of `Environment.ProcessorCount - 1` workers. It walks the
  playback range (`EffectiveStartFrame..EffectiveEndFrame`) from the playhead
  outward and asks for every drawing `TileFrameCache` does not hold — one job per
  drawing, de-duplicated exactly as `RequestPlaybackPrewarm` already does for held
  cels and the paper.
- **Playing mode:** one worker, the lookahead it has today. B29's reason, not
  competing with the tick for cores, still holds while frames are being shown.
- **Off:** while a stroke is in flight, and when the Configure switch is off.

Why idle work is safe off the UI thread is already written down in the
prewarmer's own remarks:
- Invariant 2 makes a render a pure function of the record.
- A generation counter discards a result rendered against a document that has
  since changed (`Flush`, from the invalidation funnel).

Nothing here alters a pixel, only when it is computed. One thing is new: an edit
during idle warming must now *restart* the walk, not merely discard. The same
funnel that flushes can re-queue.

### Results land without a dispatcher, as today

Finished work is queued and taken by the UI thread at the top of the next
publish (`TakeWarmedFrames`). That is fine while playing, which publishes every
tick.

At idle nothing publishes, so finished tiles would sit in the queue. Idle mode
therefore needs a cheap *drain* posted at `Background` priority when a batch
finishes. It is a drain only, never a publish.

### Buffering is a state of the play button

Pressing play with the range not ready enters **buffering**:
- the playhead holds;
- the timeline shows ready frames filling;
- every core but one is on the range, since no frames are being shown yet;
- play starts when the remaining render time is shorter than the remaining loop.
  The simpler rule, "every drawing in the range is held", is the first cut.

Pressing play again cancels it. The progress lives on the timeline, the
indicator B167 also asks for. One indicator, two producers: tiles ready (this
note) and composites ready (B167).

**A miss during play still renders on the tick.** Buffering makes it rare, not
impossible: an evicted tile, or a range larger than the budget. This note does not
make playback drop or hold frames; Q214 chose true timing.

### Stopping

On stop, the publish keeps the tile route for the frame on screen when its tiles
are held. It warms that frame's bitmaps through the existing
`WarmWhatTheNextStrokeWillNeed` path, and swaps to the ring once they are in.
The swap must not change a pixel, so phase 3's gate is a test that the two routes
draw the same frame identically. If they are not identical today, that is a
finding to fix before the swap, not a tolerance to add.

## Phases, and how each is known to work

The lab measures every phase: `first-playback`, plus a new `play-after-edit`
(edit a cel, wait N seconds, press play). Each phase is A/B'd against the one
before it with `perf/lab.py ab`.

| Phase | What | Done when |
|---|---|---|
| 1 | **Measure memory**: tile bytes per drawing on the owner-shaped document; whether the whole range fits `MemoryBudget.TileCache()` at 1080p and 4K | Numbers in this note; decides whether idle mode warms the range or a window around the playhead |
| 2 | **Idle warming**, pool of N−1, restarts on edit, drains at Background | `first-playback`: first frame **< 100 ms** after a 10 s idle; `play-after-edit` reported against idle time; UI stall from the warmer **0** (it never runs on the UI thread) |
| 3 | **Stop stays on tiles**, bitmaps warmed behind | Stop → frame on screen **< 50 ms**; the two routes pixel-identical (test) |
| 4 | **Buffering** state and the timeline indicator | Pressing play cold: no frame shown out of time; UI responsive throughout (no stall over 50 ms while buffering) |
| 5 | **One cache**: the paused canvas composes from tiles | Folded into B125/B167; not started before they settle |

Two things are deliberately not here:
- **Parallel rasterization while playing:** B29 measured it as a loss, and
  nothing in this note changes that.
- **Dropping frames:** Q214 chose true timing.

## Risks

- **Memory.** A scene's tiles are sparse, only inked tiles exist, but 240 frames
  at 4K is not 21 at 1080p. Phase 1 exists so that idle mode is sized by
  measurement, not hope. If the range does not fit, warm a window around the
  playhead and let buffering cover the rest.
- **Fans and battery.** Idle mode is real CPU after every edit. That is why it
  stops on a stroke, has a switch, and walks outward from the playhead, so the
  frames most likely to be played are done first.
- **Wasted work.** A rapid run of edits restarts the walk each time. The
  generation counter already makes this wasteful, never wrong. If the lab shows
  churn, debounce the restart by a few hundred ms.
- **A second route that diverges.** Phase 3's identity test is the guard. It is
  also the first evidence for phase 5: if the routes are identical, merging them
  is a routing change and not a rendering one.
