# Compositing on the GPU

The design note B125 asks for. It records what the change actually is, the two
things in the current code that decide its shape, and what stays on the CPU on
purpose.

Written before any of it is built, so the owner is agreeing to a shape rather
than to a diff.

## Why, in one paragraph

Compositing is CPU raster everywhere: nothing in the solution creates a
`GRContext`. Measured with every cache access a hit, so it prices blending with
no rasterization in it, one 3-layer frame costs **55.7 ms at 1080p, 215 ms at
4K, 1040 ms at 8K** against an 83 ms playback budget — `n^1.03`, linear in area,
which is what full-canvas CPU blits are and is why it cannot be tuned out. The
roadmap already commits to infinite canvas and to documents this does not serve.

## The first crux: the context is not where the composite is

This is the part that makes B125 an inversion rather than a substitution.

- `MainViewModel.PublishSnapshot` composites **on the UI thread** and produces a
  finished `SKImage`, which it hands to the canvas through `SnapshotChanged`.
- The `GRContext` exists **only inside the draw op, on the render thread** —
  `DrawOp.Render` takes an `ISkiaSharpApiLeaseFeature` and reads `lease.GrContext`.

`CanvasControl.RunWithGpuContext` already bridges those, and it is deliberately
not the answer: it is a one-shot queue built for the upload probe, drained once
in the next render and cleared. Per-frame compositing cannot ride on it.

So the change is: **the view model stops producing an image and starts producing
a pass list; the canvas composites inside the draw op.**

That is a bigger win than moving the blend, and worth being explicit about
because it is easy to lose in the plumbing: **the intermediate full-canvas
`SKImage` disappears entirely.** No allocation, no upload of the composed frame,
and `ComposeRing`'s three-buffer rotation exists to make that allocation
survivable — so it goes too, on the display path.

## The second crux: a pass is a CPU bitmap

`RenderPass` carries an `SKBitmap`. Passes are produced by rasterization on the
CPU, which is where they have to stay — the brush engine is bit-identical by
invariant and `RuntimeDeterminismTests` pins it.

**So "composite on the GPU" is really "get the layer rasters onto the GPU and
keep them there".** Blending is cheap once the pixels are resident; uploading two
megapixels per layer per frame would just move the cost from blend to bus.

That makes texture residency the actual design problem:

- A layer's raster changes when its **drawing** changes, not when the playhead
  moves. During playback nothing changes at all — the playhead exposes different
  cels, each of which has its own raster.
- So layer rasters want to live as GPU textures keyed by the same identity the
  frame cache already uses, and be re-uploaded only when that identity's pixels
  change.
- The tile path (B144) already decomposes a frame into 256² tiles and only
  touches the ones a viewport reaches. Tiles are the natural upload unit: a
  stroke dirties a handful of them, not a canvas.

**This is where the estimate is softest and it should not be presented
otherwise.** Upload bandwidth on an integrated GPU is the number that decides
whether this is a 20× win or a 3× one, and there is no way to measure it in this
repository — the container has no GPU context, which is the same reason B122
shipped as an inference and the render report exists at all. The first
measurement on real hardware is a gate, not a formality.

## What stays on the CPU, and why that is the safety property

**GPU compositing is display-only. Export stays on the CPU.**

This is what dissolves B125's third constraint rather than hedging it. The stroke
record is the document (invariant 1) and export runs through `FrameRasterizer`,
so GPU blend rounding differing from Skia's CPU blending cannot reach saved art,
an export or a contact sheet. Two blend implementations is the shape of B54 and
B69 — the difference here is that only one of them can ever produce a file.

**The two paths staying separate is the constraint.** The hazard returns the day
somebody unifies them for tidiness, so `RuntimeDeterminismTests` going red means
that happened; it is not a reason to relax the test.

### The readback list

Anything that reads composited pixels back becomes a pipeline stall against a
GPU-resident surface. These are the call sites, and each one is a decision:

| Reads back | Where | Disposition |
| --- | --- | --- |
| Smudge and blur sampling | `BrushEngine` `PeekPixels` | Stays CPU — it samples the *layer*, not the composite |
| Flood fill | `FloodFill` | Stays CPU, same reason |
| Tile rasterization | `TiledRasterizer` | Produces the textures; upstream of the GPU |
| Thumbnails, symbols, textures | `SymbolRasterizer`, `TextureRegistry` | Stays CPU |
| Export, contact sheets | `FrameRasterizer` | Stays CPU **by decision**, see above |

The encouraging part: none of these sample the *composited display surface*.
They sample layers and frames, which remain CPU-side. If that turns out to be
wrong for any one of them, that call site is the thing to redesign — not the
compositor.

## The third crux, found while scoping stage 1: pass bitmaps are cache-owned

Stage 1 sends a pass list to the render thread. Every pass bitmap comes from
`_cache.Get(...)` — `FrameBitmapCache` — and **eviction disposes it**
(`FrameBitmapCache.cs:331`, `node.Value.Bmp.Dispose()`).

Today that is safe by construction: the composite is synchronous on the UI
thread while the cache still holds the bitmap, and only the *finished* image
crosses to the render thread, whose lifetime `CanvasControl._retired` manages.

Send the passes instead and that guarantee is gone. A frame-cache eviction
between publish and render frees pixels Skia is about to read — a
**use-after-free in native code**, which is B130's exact signature: no managed
stack, an empty crash log, and "Lightbox dies as soon as I touch anything".

So stage 1 is not plumbing. It needs a lifetime protocol, and there are three
candidates:

- **Pin what is published.** The cache refuses to evict entries a live pass list
  references. Cheapest, and it lets a stalled render pin the cache.
- **Extend retirement to passes.** `_retired` already solves this exact hazard
  for the composed image; passes would follow the same rule — freed only once a
  newer snapshot has been drawn.
- **Publish immutable snapshots.** Passes carry their own references rather than
  borrowing the cache's. Safest, and costs a copy — which is the thing the whole
  change exists to avoid.

The second reuses machinery that already exists for this failure and is the one
to cost first. Whichever wins, **it lands before any pass crosses a thread**,
not after a crash report.

## Staging

Each stage is independently landable and independently revertable, which matters
because the bandwidth question above is unanswered until stage 2 runs on real
hardware.

1. **A lifetime protocol for published passes**, per the crux above. Nothing
   crosses a thread until this exists.
2. **The pixel-identity harness.** `ComposeIdentityTests.Bytes` composes a pass
   list and reads the pixels out; the inversion is measured against it. Built
   before the plumbing on purpose — "looks the same" is not a claim this project
   accepts, and nothing could assert on a composite while B156–B164 were being
   diagnosed from field captures. It already carries three properties: the
   composite is deterministic, a clipped compose matches an unclipped one inside
   the clip, and **a full compose into a dirty surface matches a fresh one** —
   the stale-buffer hazard `PublishSnapshot` describes and admits it never
   tested.
3. **Pass list to the render thread.** The pass builder is now a unit
   (`ScenePassBuilder`, B166's first seam) rather than 230 lines inside
   `PublishSnapshot`, which is what makes this stage's diff readable against the
   concern.

   **Split in two, because the lifetime has to be real before the compositing
   moves.** Stage 1 built the pin protocol and *nothing called it* — a tested
   mechanism nobody invokes is indistinguishable from a working one right up
   until the crash it exists to prevent. So **3a** publishes the pass list on
   the snapshot and wires the borrow: `PublishSnapshot` pins every cached
   bitmap a pass carries, `RenderSnapshot.Dispose` releases them, and the three
   places the retirement queue frees a snapshot all route through it. No pixels
   change; the image is still composed on the UI thread. **3b** is then the
   inversion itself: `RenderSnapshot` grows a pass-list form
   beside its image; the canvas composites CPU-side from passes inside the draw
   op. No GPU yet, no behaviour change, and the render must stay pixel-identical
   — `PresentLatency`, retired-image disposal and the durable frame all assume an
   `SKImage` today.
4. **GPU surface, CPU-uploaded passes.** Composite into a GPU surface from the
   lease, uploading pass bitmaps per frame. Almost certainly *slower* than today
   on some hardware — the point is to measure the upload, which is the number the
   whole design rests on.
5. **Resident layer textures.** Cache uploaded tiles, invalidated by the drawing
   rather than the playhead. This is where the win actually arrives.
6. **Retire the display-side `ComposeRing`** once nothing reads a composed image.

Stage 4 is a gate. If the upload dominates on an integrated GPU, stage 5's
invalidation design is what has to carry it, and the estimate for the whole
changes.

## How we will know it worked

Not by feel, after the six rounds B156–B164 took to learn that. The render report
already prints `Compose` per tick, `tick + draw` against the frame period, and
the upload probe against the real graphics context. The claim to test is narrow:
**`Compose` falls by an order of magnitude at 1080p and stays sub-budget at 4K
with ten layers.** If it falls at 1080p and not at 4K, the upload is dominating
and stage 3 is the whole feature.

## What this does not fix

The layer axis. Compositing costs area × layers; this divides the area term.
Ten layers at 8K is still over budget after a 20× win, which is B165 — a separate
entry on purpose, because it is a different axis and needs a different answer.
