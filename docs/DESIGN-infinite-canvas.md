# The infinite canvas: what it costs today, and what has to change first

Status: **measured and specified, nothing built.** The roadmap carried this as
`- [?] Infinite canvas` — one line, no description, no evidence anchors — and
`[?]` in that file means *"no evidence declared, unverifiable, add anchors or
admit it is a wish"*. This document is the item being specified rather than
implemented, and the numbers below are the reason the shape of it is not a
matter of taste.

## The measurement, and the one it corrects

There was already a canvas-size sweep. It measures **committing a stroke**,
which is the one path in the application that is *immune* to canvas size by
design — Charter O3, a stroke costs what the stroke touched. It sits at
`n^0.22` and 18% of budget, and it has sat there for months. So the performance
map read as "canvas size is covered" while the two paths that genuinely scale
with area had never been swept along that dimension at all.

Swept now, three layers, one frame, cache warm and surface reused:

| canvas | resolution | recomposite p95 | cache for one frame |
| ---: | --- | ---: | ---: |
| 5 | 960×540 | 13.7 ms | 6 MB |
| 21 | 1920×1080 | 54.8 ms | 24 MB |
| **37** | 2560×1440 | **99.6 ms** | 42 MB |
| 83 | 3840×2160 | 218.9 ms | 95 MB |
| 332 | 7680×4320 | **1120.1 ms** | 380 MB |

`n^1.05`, cliff at **37** — 1440p — against an 83 ms playback budget, reaching
**1344% of budget** at 8K.

**The two exponents together are the finding.** `0.22` for drawing and `1.05`
for repainting, on the same axis, in the same application:

- **Putting ink down does not care how big the canvas is.** The drawing floor
  is already built for an infinite canvas and nothing here threatens it.
- **Showing the canvas cares about nothing else.** A repaint is proportional to
  the document, not to the window, because every layer bitmap *is* the
  document.

An artist on a 4K canvas is therefore paying 219 ms to see a frame change that
their strokes cost 9 ms to make. The cost is not in the art; it is in the
model.

## Why this is not an optimisation problem

The line that decides the whole design:

```csharp
_cache.Get(frame, scene.Width, scene.Height)   // one bitmap per layer, document-sized
```

On an unbounded canvas there is no `scene.Width` to pass. This is not a slow
call to make faster — it is a call that **cannot be made at all**, and no amount
of culling changes that, because the allocation happens before anything knows
what is on screen.

The arithmetic, extrapolated from the measured column above at 4 bytes a pixel:

| canvas | bytes per layer | one 3-layer frame | against the 512 MB budget |
| --- | ---: | ---: | --- |
| 1080p | 8 MB | 24 MB | 21 frames cached |
| 4K | 33 MB | 95 MB | 5 frames |
| 8K | 132 MB | 380 MB | **1 frame, 74% of the budget** |
| 16K | 530 MB | 1.5 GB | **cannot hold one frame** |
| unbounded | — | — | **cannot allocate one layer** |

The failure is not gradual. It is a wall, and 8K is already standing against it.

**So tiling is the precondition and culling is the consequence**, in that order.
Culling asks "which of these do I need?", and it can only be asked once pixels
live in units smaller than the document. Reversing the order — culling strokes
while still materialising document-sized bitmaps — saves the rasterisation and
leaves both the allocation and the composite exactly where they are.

## What "infinite" has to mean here, and what it must not break

Two rules from `CLAUDE.md` constrain this, and both are load-bearing.

**"Optional means absent, not disabled."** A document that never asks for an
infinite canvas must serialise, render and export *exactly* as it does today —
no new keys, no new UI, no cost. The camera is the precedent and it is a good
one: authored, absent from the file until it exists, askable for anywhere. An
infinite canvas is the same kind of thing, and the same test applies —
serialise a fixed-size document and assert the JSON is unchanged.

**"Assets — the canvas *is* the output. There is no camera, frame bounds must
stay consistent, and every frame is a deliverable."** This is the one that does
not resolve on its own. A sprite sheet is defined by having consistent frame
bounds; an infinite canvas is defined by not having bounds. Both are first-class
here, so the answer cannot be "the asset workflow loses". It is **Q20**.

The third rule the design has to satisfy is invariant 1: **the stroke record is
the document**. Tiles are *derived pixels*, exactly like the frame cache is
today — a tile is a cache entry, never a source of truth, and dropping every
tile must lose nothing but time. That makes the change less frightening than it
sounds: it is a change to a cache, not to a document format.

## The shape, in one paragraph each

**Tiles behind the cache, not beside it.** `FrameBitmapCache.Get` hands back a
document-sized `SKBitmap` today and every caller treats it as pixels the cache
owns. The smallest honest change keeps that contract for fixed-size documents
and adds a tiled path for unbounded ones, so nothing that works today takes a
new code path. A tile is a fixed square — 256² or 512², to be measured, not
guessed — addressed by integer grid coordinates that can go negative, because
an infinite canvas has no origin corner.

**Culling is then a rectangle intersection.** Given the view transform, the
visible document rectangle is already computable — `CameraTransform.DeviceBounds`
does the equivalent for dirty regions — so compositing walks the tiles the
viewport touches and nothing else. The exponent that should fall out is the one
the drawing path already has: cost proportional to what is *seen*, not to what
*exists*.

**Strokes need a spatial index, or rasterising a tile is O(all strokes).** A
tile has to know which strokes reach it. Every stroke's bounds are already
computed (`BrushEngine.CommitBounds`), so a grid or R-tree over those is the
missing piece, and it is also the fix for B30 — rasterising a frame is
`n^1.04` in strokes with a cliff at 50 because it replays every stroke
regardless of where it lands.

## What this predicts, so it can be proved wrong

Written down before building, because a design that cannot fail is not a design:

1. **Recompositing becomes flat in canvas size** — the `n^1.05` above should
   fall toward the `n^0.22` the drawing path already has, since both would then
   cost what they touched.
2. **Memory becomes proportional to ink, not to area.** An empty region costs
   nothing because an untouched tile is never allocated. This is the claim that
   makes "infinite" mean anything at all.
3. **A fixed-size document is bit-identical.** `RuntimeDeterminismTests` already
   pins the render; if tiling changes a pixel, that fails, and it is right to.
4. **B30 improves as a side effect.** If it does not, the spatial index is not
   doing what this document says it is.

## Not in scope, deliberately

**Infinite *time*.** The scene's frame count is a separate axis with its own
measured curve (`n^1.93` in frames, cliff at 24) and its own bug (B28). Bundling
them would put two unbounded dimensions in one change.

**GPU-backed tiles.** Everything here is CPU Skia, as the rest of the engine is.
A GPU path is a real option and it is a different document; deciding it here
would smuggle a rendering-architecture change in behind a canvas feature.

**Changing the stroke record.** Nothing in this needs a document-format change,
and if a proposal starts requiring one, that is the signal it has drifted into
redefining what a document is rather than how it is drawn.

## Open questions

- **Q20** — what frame bounds does an Asset project export from an unbounded
  canvas? Bounds-of-ink, an authored region, or the camera when one exists.
  These give different pixels for the same drawing and it is not derivable from
  the code.
- **Q21** — is the infinite canvas a *document property* an artist turns on, or
  a *project-type default*? The reach rule says every feature is available
  everywhere; this decides only what a new document starts with.
