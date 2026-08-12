# The tiled compositor, and getting it off the CPU

B167's plan. Written to be picked up cold, by somebody who was not in the
session that produced it.

## Why this and not something else

Playback takes the **tiled/unbounded** compositor. Not the culled one, not the
ring. The route is chosen like this, and there is no resolution term anywhere in
it:

```
tileModeOn    = UnboundedCanvasOn || IsPlaying          (ScenePassBuilder)
tileNativeDoc = tileModeOn && no camera && a viewport
unbounded     = tileNativeDoc && no camera && a viewport (ComposePlan)
```

So a 4K document **paused** composites through the ring; the same document
**playing** composites through tiles. Pressing play changes the compositor.

Measured on the owner's machine, 4K at 300 dpi, 2 layers, not zoomed in:

| canvas quality | compose scale | Compose | tick + draw | budget @ 12 fps | dropped |
| --- | --- | --- | --- | --- | --- |
| Display | 0.375 | **49.49 ms** | 55.0 ms | 66% | 3 |
| Full | 1.0 | **60.42 ms** | 70.1 ms | 84% | 47 |

100% of layer passes tiled in both. **Zero publishes reached the GPU path.**

**B125 stages 1–5 targeted the culled route, which playback never takes.** That
was a real mis-aim, corrected in `DESIGN-gpu-compositing.md`; the machinery
built there is sound and reusable, it was simply pointed at the wrong compositor.
This note points the same machinery at the right one.

The ring stays on the CPU deliberately. It exists to reuse three buffers and
patch a dirty region, and B121 measured what breaking that costs: a dab-sized
repaint became viewport-sized, 1 232 px against 134 400 px, 0.26 ms against
76 ms at 4K. Drawing is already cheap; playback is not.

## What the tiled path does today

`MainViewModel.ComposeUnboundedSnapshot`, on the UI thread. Per pass:

1. `_tileFrames.Get(frame, w, h)` → a tile pyramid for that frame.
2. `TilePyramid.LevelFor(renderScale)` → the level nearest the screen resolution.
3. `TileCompositor.CompositeToBitmap(pyramid.Level(level), levelViewport)` →
   **one flat bitmap** of the visible tiles.
4. Draw that bitmap in document space.

Two costs hide in there and the report cannot currently tell them apart:
**flattening** the tiles into a bitmap (step 3) and **blending** the result
(step 4). Every phase below depends on knowing the split, which is why measuring
it is first.

## The blocker, stated plainly

`_tileFrames` is a `TileFrameCache` owned by the view model. The draw op runs on
the render thread and has no route to it. That is the same shape as B125's first
crux, and the phases below dissolve it rather than confronting it — **after
flattening, a tile pass is just a bitmap pass**, and the existing
`DeferredCompose` already carries bitmap passes across the thread with a pin
protocol that works.

So the tile cache never has to move.

## Where this stands — read this first

Phases 1, 2, 3a, 3b and 4 have landed. **Phases 5 and 6 have not, and neither is
ready to start for the same reason: the number that decides them does not exist
yet.**

| Phase | State | What unblocks it |
| --- | --- | --- |
| 1 · flatten/blend split in the report | landed | — |
| 2 · cache the flattened bitmap | landed | — |
| 3a · flatten before the composite | landed | — |
| 3b · composite in the draw op | landed | — |
| 4 · GPU surface for the tiled route | landed, **measured 2026-08-12: 310 publishes on the card, 0 fell back** | — |
| 5 · resident tiles | not started | a **re-**measurement, now that residency is actually wired (see below) |
| 6 · retire the redundant compositors | not started | phases 4–5 running long enough to trust |
| 7 · cache the composite (added 2026-08-11) | not started | nothing — it is independent of all of the above |

**That capture happened on 2026-08-12, and it changed two things.**

**Phase 4 is confirmed executing** — `310 did, 0 fell back`, after reading zero
on every earlier capture because every earlier capture had the toggle off.

**And the same file read `no layer textures were asked for` three sections
later.** Both were true, and the contradiction was a wiring gap: every flattened
tile pass carries a placement matrix, so on this route every pass took
`ComposeUnbounded`'s matrix arm into `DrawWhole`, which uploaded
unconditionally. Only the ordinary-layer arm consulted residency, and nothing on
the route playback takes ever reached it. Fixed by passing residency into both
whole-pass arms — a fraction of phase 5's work, and the honest way to find out
whether the upload was ever the cost.

**So phase 5 needs a *re*-measurement rather than a first one.** The
pre-registered table below said "blend dominates, upload small → refuse", and
applying it to the 2026-08-12 numbers would have been wrong: residency was never
exercised, so that 38.47 ms draw contained the very uploads phase 5 exists to
remove. The table stands; the input to it does not exist yet.

**The bigger finding in that capture is not about compositing at all.** `tick +
draw` came to **39.24 ms against an 83.3 ms budget — 47%** — while the clock ran
late on **100% of ticks** and published frames waited a mean of **176 ms** to be
drawn. That is **B178**, it is P1, and every remaining phase here optimises the
47% while the 100% sits untouched. Read B178 before deciding this is the most
valuable place to spend a session.

## Phases

Each is independently landable, independently revertable, and leaves the
application working. None of them changes what an artist sees until the last
one changes how fast they see it.

### Phase 1 — Split `Compose` into flatten and blend in the report

Pure instrumentation. Add two `TickProfile` phases inside the tiled path so the
report says how much of the 49–60 ms is `CompositeToBitmap` and how much is the
draw.

**Why first:** B125 spent five stages optimising a route playback never took,
and the report is what eventually said so. Optimising the wrong half of the
tiled path is the same mistake one level down. If flattening is 90% of it,
phases 4–5 are the feature and phase 3 is plumbing; if blending is, the reverse.

*Risk: none. Nothing changes but the report.*

### Phase 2 — Cache the flattened bitmap — **landed**

`CompositeToBitmap` runs on every publish for every pass. Its inputs are the
frame, the pyramid level and the level-space viewport rectangle — all of which
are **identical from one playback frame to the next for any layer that holds**.
`TileFlattenCache` keys on exactly those three plus a version.

This is a CPU-only win and needs no threading change. It is also the same shape
as B165's held-run fold, so the expected hit rate is B165's measured share of
layer draws that repeat a drawing: 26% at two layers, 51% at six, 59% at ten.

**Budget it.** These are viewport-sized bitmaps; an unbounded cache here is the
memory problem B144 built tiles to avoid. LRU inside a byte budget, reported.

*Risk: stale pixels, mitigated by the version key and by a test that changes a
layer under a cached flatten and asserts the composite followed — the same test
B165's entry demanded, one level down.*

#### What the plan got wrong, and it was the version key

The plan said `BitmapVersion`, by analogy with `LayerStackBake`. **There is no
bitmap here to carry one.** A tile-native pass has a `TileStore` and a
`TilePyramid`, and the two obvious substitutes both fail:

- **The frame id** is what `Append` leaves unchanged while stamping a committed
  stroke into the tiles in place — the exact trap `BitmapVersion` exists to
  close, so using it alone reintroduces the bug the plan was guarding against.
- **The `TilePyramid` instance**, which `Append` *does* replace, is disposed at
  the moment it is replaced. Keying on it means holding a reference to a
  disposed object to compare against.

So `TileFrameCache.StampOf` is the version: a single ever-increasing counter,
issued fresh whenever a frame's tiles are built or mutated. **A per-frame
counter restarting at zero is the version of this that is subtly wrong and worth
writing down** — invalidate a changed frame, let `Get` rebuild it, and the
counter is back at zero, matching a flatten cached before the change whose
pixels are now wrong. A counter that only goes up cannot collide with anything
it has already issued.

`AStrokeCommittedUnderACachedFlattenStillReachesTheScreen` is the test the plan
demanded, and it bites: replacing the stamp with a constant makes the second
stroke never reach the screen.

#### The lifetime change nobody asked for, which is where B130 lives

Before this phase a flattened bitmap was allocated per publish and owned by
nobody — disposed with the snapshot, safe by construction. A **cached** one is
borrowed by every snapshot that got it, and evicting it while the render thread
is mid-draw frees pixels Skia is about to read: an access violation in native
code, no managed stack, an empty crash log. So `TileFlattenCache` runs the same
counted pin protocol as `FrameBitmapCache`, and `PinPasses` pins against both
caches because a pass list now mixes their bitmaps.

The one case that stays owned is a flatten the cache **refused** — a viewport
larger than the whole budget. Refusing is a real outcome rather than a failure:
caching it would evict everything on every publish, which is worse than not
caching at all.

### Phase 3 — Move the tiled composite into the draw op

With phase 2 in place, the publisher can flatten and hand over **bitmaps**. At
that point the tiled route is a list of bitmap passes and `DeferredCompose`
already knows what to do with those, including the pin protocol that keeps them
alive across the thread (B125 stage 3a).

Concretely: build the flattened passes in `PublishSnapshot`, put them in a
`DeferredCompose` with the viewport as its clip, and let `ComposePlan` route
`Unbounded` through the deferred path instead of `ComposeUnboundedSnapshot`.

**Measure it against `ComposeIdentityTests`.** The pixels must not change. That
harness exists for exactly this and was built before B125 stage 3 for the same
reason.

*Risk: the tiled path has its own transform handling (`FloorDiv`, level
stepping, the `lvp` translate). Getting that wrong shifts the whole image. The
identity harness catches it; a screenshot would not.*

### Phase 4 — GPU surface for the tiled route

Free, once phase 3 lands: the deferred path already asks `GpuComposite` for a
surface and already consults `LayerTextureCache` when the surface is GPU-backed.
Turning the toggle on should now show a non-zero count in the report's
compositing line, which currently reads zero for every playback capture.

**This is the gate.** If uploading the flattened bitmaps dominates, phase 5 is
the whole feature and the estimate changes. On integrated graphics the upload
competes with the CPU for the same memory bus, which is why it has to be
measured on hardware rather than reasoned about — the container has no GPU.

*Risk: none new. The CPU fallback is the same one that runs today, and it is
what every test in this repository exercises.*

### Phase 5 — Resident tiles instead of flattened bitmaps — **blocked, not merely unstarted**

Upload the **tiles** and let the GPU composite them, skipping
`CompositeToBitmap` entirely. A stroke dirties a handful of 256² tiles rather
than a viewport, so the upload becomes proportional to what changed rather than
to what is visible. This is where B125's original second crux finally applies —
"get the layer rasters onto the GPU and keep them there" — and
`LayerTextureCache`'s key and budget machinery carries over unchanged.

#### Do not start this without the number, and the reason is B125 itself

**B125 spent five stages optimising a route playback never takes.** Nothing was
red, every stage worked, and the mis-aim only surfaced when a render report was
finally read. Phase 5 is the single most expensive phase here and rests entirely
on a premise nobody has checked: *that the upload dominates.* Building it on the
assumption would be the same mistake with a larger bill.

**The blocking measurement, concretely:**

1. Switch GPU compositing on — Edit ▸ Configure ▸ Performance ▸ *Composite
   layers on the GPU* (or `LIGHTBOX_GPU_COMPOSITE=1`).
2. Open a document at the size that hurts — 4K is what the ledger's numbers
   came from — and **play a range for several seconds**, pointer still and off
   the canvas.
3. Help ▸ *Write a render report*.

**Read three lines, in this order:**

- **`of the publishes that could use the card`** — if this is still zero, stop.
  Phase 4 is not executing at all and *that* is the bug to fix, not phase 5.
- **`Compose` ms/tick, and the nested `of it, TileFlatten`** — phase 1 put
  flattening at 30–35% of Compose *before* phase 2 cached it. If flatten is now
  small, the upload is not the cost and phase 5's premise is refuted.
- **`layer textures` residency hits/misses** — the direct read on whether
  keeping pixels on the card is paying.

**What each outcome means, decided in advance so the result cannot be read
to taste:**

| The report says | Then |
| --- | --- |
| GPU publishes still 0 | Fix phase 4's wiring. Phase 5 stays blocked. |
| Blend dominates, upload small | **Phase 5 is refused.** Record it and go to phase 7. |
| Upload dominates | Phase 5 is justified. Build it. |

**And the generalisation warning that applies specifically here (Q64).** The
owner's machine is a Ryzen 7 PRO 5850U with *shared* memory between processor
and card, so an upload competes with the compositing beside it. A discrete card
crosses PCIe once and then blends from dedicated memory — **phase 5 helps that
case more.** So a bad number on the development machine is not a verdict on
phase 5 in general, and killing it on one integrated GPU's measurement would
repeat B125's mis-aim in the opposite direction. If the number is bad here, the
honest outcome is *"unproven on discrete hardware"*, not *"dead"*.

### Phase 6 — Retire what is now redundant — **deletion, and the last thing to do**

`SceneRenderer.ComposeUnbounded` and, if nothing else reads a composed image on
the display path, `ComposeRing` (B125 stage 6).

**Why it is last rather than tidy-up-as-you-go.** These are the fallbacks. While
phases 4–5 are unmeasured, they are what the application still works *through*
if the new path is wrong — and deleting a fallback before its replacement has
run on real hardware is how a bad frame becomes an unusable application.

**`ComposeRing` is the one to be careful with, and the reason is measured.** It
reuses three buffers and repaints only a dirty region; B121 measured what losing
that costs — a dab-sized repaint becoming a viewport-sized one, **1 232 px
against 134 400 px, 0.26 ms against 76 ms at 4K.** Drawing is already cheap
*because of the ring*. So the condition is not "playback is fast now" but
"nothing on the **drawing** path needs it":

- `ComposePlan.For` must no longer be able to return `ComposeRoute.Ring` — check
  the incremental-publish arm, which is the one that exists for drawing.
- No caller may read `RenderSnapshot.Image` expecting non-null on the display
  path. Ten tile pixel tests already went red on exactly this in 3b.
- The export and thumbnail paths compose separately; confirm they do not route
  through either before removing it.

**Do it as its own branch with no other change in it**, so a revert is one
command. A deletion mixed with an optimisation cannot be backed out cleanly, and
this is the change most likely to need backing out.

### Phase 7 — Cache the composite itself (added 2026-08-11, independent)

**Not part of the GPU migration, and that is the point of adding it here:** it
is the one remaining phase that pays with the GPU toggle *off*, which is what
almost every machine runs today.

**What is missing.** B165 already skips a composite that would be identical to
the one on screen — but `_lastPublished` (`MainViewModel.cs:10887`) is a
**single slot**, so it only catches *consecutive* repeats. That is the hold case
on 2s. Loop back to frame 0 and every frame recomposites from scratch, though
the blend is byte-identical to the one it did a second ago. Turning that slot
into a small LRU keyed by the same `FrameFingerprint` makes lap two of every
loop essentially free.

**Why it is worth more than phases 5 and 6 together.** It is
**route-independent**, exactly as B165 is: it applies wherever the composite
happens, so unlike the GPU work it *cannot* be aimed at a path playback never
takes. Compose measured 49.5 ms (Display) and 60.4 ms (Full) per tick at 4K
against an 83 ms budget.

**The memory is smaller than it sounds, and bigger than one number suggests.**
The compose surface is **view-sized, not document-sized** (`ComposePlan.cs:143`:
`viewWidth * renderScale`), so a 4K document in a 1080p window composites to
about the window — ~8 MB a frame, ~200 MB for a 24-frame loop. But a 240-frame
scene is ~2 GB, so this must cache **a window around the playhead inside a
budget**, never "the animation". `MemoryBudget` already gives the per-machine
number.

**Where it has to live, which is the one real complication.** Since phase 3b the
composite happens *inside the draw op*, and `CanvasControl` owns the resulting
image via `_retired`. So the cache belongs on the canvas side, keyed by a
fingerprint the view model computes and passes across with the snapshot — not in
`PublishSnapshot`.

**Pair it with a cache indicator on the timeline.** Every tool that solved this
— After Effects' RAM Preview, Krita's animation cache — solved it by making the
state *visible*, because the honest answer to "can you guarantee smooth 4K
playback" is *no, and here is exactly which frames are ready*. A cache with no
indicator gets blamed for the stutter it is reducing.

**A second half, deliberately separated:** teaching `FramePrewarmer` to produce
composites ahead of the playhead, so lap *one* is partly free too. Keep it a
distinct piece of work — the cache alone cannot make anything worse, whereas a
worker competing with the tick for memory bandwidth can, and *that* half needs
measuring rather than reasoning. Note that the `GRContext` is single-threaded
and lives in the render thread's lease, so background compositing is necessarily
CPU-side.

## The rule that makes this safe

Every phase keeps the CPU path working and reachable. GPU compositing stays
behind **Configure → Performance → Use the graphics card to blend layers**,
off by default, with `LIGHTBOX_GPU_COMPOSITE=1` as a headless override. Export
never touches any of this — it runs through `FrameRasterizer` on the CPU, which
is what keeps GPU blend rounding out of saved art (invariant 1).

## How to know each phase worked

`Help ▸ Write a render report`, during playback, on a large document. The lines
that matter:

- `Compose ms/tick` — the number every phase is trying to move.
- the new flatten/blend split — phase 1's whole product.
- `compositing: of the publishes that could use the card` — zero today; phase 4
  should make it non-zero.
- `resident layer textures` — phases 4–5.
- `frames not composited` — B165's reuse, which is worth nothing on a scene
  animated on 1s and should not be mistaken for a regression when it reads zero.
