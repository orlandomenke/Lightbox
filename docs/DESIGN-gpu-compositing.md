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
   **3b landed on the culled route only, and the reason is a constraint rather
   than caution.** Of the three compositors, only the culled one already built a
   fresh surface every publish and filled all of it — so nothing whatever is lost
   by building it on the render thread instead. The **ring** exists to reuse three
   buffers and repaint a dirty region, and B121 measured what losing that costs
   (a dab-sized repaint becoming a viewport-sized one, 1 232 px against
   134 400 px); moving it before moving its buffers *is* that regression, so it
   waits for stage 6. The **unbounded** path reads tile caches the view model
   owns.

   **The claim that the culled route "matters most" was wrong, and the first real
   reports (2026-08-10) say so.** A playing document takes the *unbounded* tiled
   compositor — `tileModeOn = UnboundedCanvasOn || IsPlaying` — so the culled
   route, and everything from stage 3b onward with it, applies to a route playback
   never takes. The reports show 1756 tiled layer passes against 68 layer draws
   through the texture cache. Reaching playback needs the tile route moved, which
   is a bigger piece because the tile caches live on the view model.

4. **GPU surface, CPU-uploaded passes.** Composite into a GPU surface from the
   lease, uploading pass bitmaps per frame. Almost certainly *slower* than today
   on some hardware — the point is to measure the upload, which is the number the
   whole design rests on.
5. **Resident layer textures.** `LayerTextureCache` keys uploaded textures by
   bitmap instance *plus* `BitmapVersion` — identity alone is the trap
   `LayerStackBake` documents, since a stroke commit stamps into a cached bitmap
   in place and an instance survives its pixels changing. LRU inside a byte
   budget, because VRAM is scarcer than RAM and on integrated graphics it is the
   same memory the CPU is competing for. A refused upload falls back to drawing
   the bitmap rather than throwing.

   **The hit rate is a number B165 already measured**: a texture is reused
   exactly when a layer shows the same drawing as the previous frame, which is
   26% of layer draws at two layers, 51% at six and 59% at ten. The two changes
   exploit the same property of animation from opposite ends, so one measurement
   sizes both. The render report prints the achieved rate against those figures,
   which is how a wrong invalidation shows up as a number rather than as a
   feeling.

6. **Retire the display-side `ComposeRing`** once nothing reads a composed image.

Stage 4 is a gate. If the upload dominates on an integrated GPU, stage 5's
invalidation design is what has to carry it, and the estimate for the whole
changes.

## One machine, every machine: what stage 4's measurement can and cannot settle

The owner's question, and it reshapes stage 4 rather than merely qualifying it:
*measuring on one graphics card is fine, but this has to run better on every
computer with one — right?*

Yes. And the measurement generalises for **the decision stage 4 gates** while
not generalising at all for **the decision to ship the GPU path**. Keeping those
apart is the whole of the answer.

**What generalises.** Stage 4 uploads every layer every frame, which is the
worst case by construction — nothing about it is tuned to a vendor. Its number
answers "how urgent is residency?", and residency (stage 5) is what fixes *both*
classes of hardware:

| | upload cost | once resident |
| --- | --- | --- |
| **Integrated** (the 5850U, the 2013 machine) | shares the system RAM bus — so an upload competes with the CPU work beside it | blending is cheap, VRAM is small and shared |
| **Discrete** | crosses PCIe, a hard ceiling no driver tunes away | blending is effectively free, VRAM is plentiful |

They fail differently and are cured by the same change. So a bad number on one
integrated GPU does not mean "GPU compositing is wrong here", it means "stage 5
is the feature and stage 4 was never the product" — which is exactly what a gate
is for.

**What does not generalise, and one of them is a trap.**

- **A software rasteriser reports as a GPU.** `llvmpipe`/`swiftshader` provide a
  real GL context that Skia accepts, so `GraphicsBackend` says "GPU" and every
  pixel is drawn on the CPU — slower than today's path, with the status bar
  claiming otherwise. This is not hypothetical: B125's own entry records the
  status bar already misleading the owner once, for a milder version of this.
- **Texture limits are hardware, not driver.** A 4K document needs a 4096-wide
  texture and an 8K one needs 8192; older parts cap at 4096 or below, and the
  2013 machine is genuinely the useful test here rather than a curiosity.
- **Driver quality is the largest unknown and the least measurable** — Mesa
  versus vendor blobs versus Windows versus Metal, on the same silicon.

### The consequence: the choice is measured at runtime, not decided here

This is the design change the question forces, and it is not defensive padding:

> **The compositor is chosen on the machine it is running on, and the CPU path
> stays a first-class fallback rather than a legacy branch.**

Three rules follow, all of them testable without a GPU:

1. **Probe, then choose.** `RenderReport.RunUploadProbe` already times a real
   upload against the real context and reports a speedup. That number — not a
   build flag and not a vendor list — decides whether a session composites on
   the GPU. A speedup near 1× means the transfer is not the cost and the CPU
   path is kept.
2. **Refuse rather than degrade.** No context, a document wider than
   `MaxTextureSize`, or a probe that comes back slower: fall back, and say so in
   the report. A wrong answer that is merely slow is the good outcome here; the
   bad one is a document that will not open on a laptop.
3. **The fallback is exercised, not assumed.** The CPU path is what export
   already uses (see *display-only*, above), so it cannot rot unnoticed — that
   is a second reason for the export decision beyond blend determinism.

**And it is why the render report exists.** It is the only way to collect
hardware this repository will never own: backend, whether the durable frame is
really GPU-backed, texture limits and a timed upload probe, written on the
artist's machine. Two machines is a thin sample; a report per artist is not.

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
