# The raster checkpoint: making a painting cheap to reopen

Status: **designed and decided, nothing built.** Opened 2026-08-08 after B30 was
measured against a painting rather than a frame of line art; the five open
decisions were answered the same day and are recorded in **Q60**. The decision to
design before implementing was deliberate: this adds a cache *inside* the artwork,
and a cache that can be silently wrong is worse than one that is slow.

| | Decided |
| --- | --- |
| Where the pixels live | **In the document**, in a new field beside the strokes |
| When one is taken | **On save, rendered on a background thread** — the save returns first |
| What invalidates it | **Any edit it covers drops it**; the next save makes a fresh one |
| Undo limit | **A memory budget, not a step count** |
| The clone stall found on the way | **Filed as B142**, fixed separately |

## The problem, measured

The document stores strokes, not pixels — invariant 1, and the reason every
stroke stays re-editable, re-colourable and legible to the inbetweener. The cost
is that materialising a drawing means replaying its whole record.

For one frame of an animation that is fine. For a single painting somebody
returns to for a week, it is the dominant cost of using the application.

`AnimationSweeps.PaintingRebuild`, 1920×1080, a 60 px soft brush at 0.05
spacing, flow 0.5:

| strokes | p95 | per stroke | allocated |
| ---: | ---: | ---: | ---: |
| 250 | 2 680 ms | 10.7 ms | 18.7 MB |
| 500 | 5 289 ms | 10.6 ms | 37.4 MB |
| 1 000 | 10 714 ms | 10.7 ms | 74.8 MB |
| 2 000 | 21 116 ms | 10.6 ms | 149.5 MB |

**`n^1.00`.** Perfectly linear, no cliff to fall off — the cost simply
accumulates and never comes back down. The per-action budget is 100 ms, so the
*smallest* sample in the sweep is over by 27×.

Measured by hand at 2000×1500, beyond what the sweep will sit through: **10 000
strokes is 106 seconds.** That is the number to hold on to, because 10 000
painterly strokes is not an unusual painting, it is a finished one.

Allocation tracks it at roughly 75 KB a stroke, which is its own reason to care:
150 MB of garbage to open a document is GC pressure on the interaction that
follows it, not only a wait.

### It is fill rate, not dab count

The intuitive explanation is wrong and worth killing early, because it points
optimisation at the wrong end.

Spacing is a *fraction of brush size*, so a 60 px brush at 0.05 steps 3 px while
a 9 px brush at 0.1 steps 0.9 px — **the big soft brush lays fewer dabs per unit
length, not more.** The two bench scenarios are deliberately sized to land near
110 dabs a stroke each, which isolates what actually differs: a painterly dab
covers about **44× the area**.

Same dab count, 44× the area, **5.3× the time** — line art measures 2.0 ms a
stroke (B30's 1594 ms at 800) against painterly's 10.6 ms. Sublinear in area,
because Skia blits large spans efficiently and a fixed per-dab overhead is being
amortised. So: the cost is coverage, and reducing dab counts would buy almost
nothing.

## Why the tiling that just landed does not fix this

`TileFrameCache`, `TilePyramid` and `StrokeIndex` all exist (PR #85, and the
index earlier). Rebuilding only the squares on screen is exactly what B30's own
entry names as the fix, and `StrokeIndex` measures a 26× narrowing — 31 strokes
of 800 reach one 256 px tile.

**It still does not help a painting, for two independent reasons, and both were
checked rather than assumed.**

1. **It is gated on the infinite canvas.** `MainViewModel.cs:111` and `:10018`
   both read `if (UnboundedCanvasOn && TileFrameCache.CanTileFrame(...))`. A
   bounded document — every painting today — takes the bitmap path and pays the
   full replay. Infinite canvas is paused and known broken, so this is not a flag
   to flip casually.
2. **Even un-gated, the zoomed-out view needs every tile at full resolution.**
   `TilePyramid` builds its reduced levels by downsampling `_source`, which is
   level 0. Opening a painting shows the whole canvas, so every tile is visible,
   so every tile must be built — and building all the tiles is replaying all the
   strokes.

Tiling defers work you *cannot see*. A painting viewed whole has nothing to
defer. It remains worth un-gating eventually for working zoomed-in on a large
canvas, which is a real want; it is not this problem's answer.

## What a checkpoint is

Store the rendered result beside the stroke list, and replay only what came after
it.

Opening becomes "decode one image, replay the last few strokes" instead of
"replay ten thousand strokes". The strokes stay the truth; the image is a
shortcut that can always be thrown away and recomputed. Invariant 1 is unharmed
as long as that direction is never reversed.

The bank-statement shape: the strokes are every transaction, the checkpoint is a
balance written on a date, and you only add what happened since.

### What it does not fix

- **Editing an old stroke.** Change stroke #3 and everything after it must replay
  regardless. A checkpoint helps append-only history, which is what painting
  mostly is, and does nothing for a deep edit.
- **Undo past the checkpoint**, for the same reason.
- **The first render.** Somebody has to pay the 106 seconds once, at save time
  rather than load time — which is the better place for it, but it is not free.

## The field it must not reuse

`Frame.PngBase64` looks like exactly the right home and is a trap. Three separate
things break, and each is enough on its own:

1. **The art would double.** `FrameRasterizer.Materialize` draws `PngBase64`
   first and *then* replays every stroke on top (`FrameRasterizer.cs:115-121`).
   A checkpoint there renders the checkpoint plus the full record — every stroke
   painted twice.
2. **Tiling would switch itself off.** `CanTileFrame` requires `!HasBaseline`,
   because a tile is rebuilt from strokes and stored pixels cannot be
   reproduced. Every checkpointed painting would become ineligible for the
   optimisation it most needs.
3. **The AI warning would fire on every painting.** `UnseenByTheModel` asks
   `HasBaseline` to mean *"this frame holds imported pixels the inbetweener
   cannot read"*. A checkpoint is not imported content — it is a rendering of
   content the model can read perfectly well.

`Templates.StrokeIds` also fingerprints the baseline's length into a template
identity, which a checkpoint has no business changing.

**The distinction to keep:** `PngBase64` is *content with provenance* — pixels
that came from outside and cannot be derived. A checkpoint is *derived state* —
pixels that can always be recomputed and must never be trusted over the record.
Same bytes, opposite meanings. They want different fields.

### The precedent worth copying

`BakedSample` (`Stroke.Baked`) already does something structurally similar: it
freezes pixels a smudge stroke read, and its own remarks record two decisions
this design should follow —

- **cropped to the stroke's reach, not the canvas**, because "the alternative is
  a full-size PNG per smudge";
- **shared by reference on clone**, because copying a megabyte of base64 per cel
  duplication buys "a guarantee nothing needs".

It also calls itself *"the one thing in the record that is pixels rather than
instructions"*, which a checkpoint would become the second of. That sentence
should be updated rather than quietly falsified.

## How everybody else avoids this problem

Worth writing down because it reframes the whole thing: **no comparable
application solves this. They each give up one of the two properties Lightbox
insists on.**

| | Document is | One mark costs | So reopening is |
| --- | --- | --- | --- |
| Photoshop, Krita, GIMP, Procreate, TVPaint | **pixels** | irrelevant, pixels are stored | fast: load the buffers |
| Illustrator, Inkscape, Affinity Designer | **geometry** | cheap — a filled path, no per-dab stamping | fast: replay is trivial |
| Blender Grease Pencil | **geometry** | expensive, but on the **GPU**, redrawn every frame | fast: never cached |
| **Lightbox** | **geometry** | **expensive, per-dab, on the CPU** | **slow — nobody else sits here** |

Lightbox chose textured per-dab marks *and* the stroke record as the truth. That
intersection is the whole differentiator and the whole bill.

- **Photoshop.** Pixels, tiles, scratch disk — and a `.psd` stores a
  **pre-composited flattened image alongside the layer data** so other
  applications can preview it. That is a raster checkpoint, shipped since 1990.
  History states are deltas on the scratch disk and are **bounded** (default
  around 50), which is Adobe reaching the same conclusion as Q60 about undo depth.
- **Krita.** Paint layers are tiled pixels with copy-on-write and a pool that
  swaps to disk; a `.kra` is essentially a zip of per-layer images; undo commands
  hold only the tiles they changed. It also has an instant-preview level-of-detail
  mode for large canvases — the same idea as rung 1 in
  `docs/DESIGN-4k-playback-and-8k.md`, which has landed. **And its vector layers
  do not get the brush engine**, which is the trade Lightbox refused.
- **Illustrator.** Geometry, and it gets away with it because its primitives are
  cheap. Where they stop being cheap — gradient meshes, blends, blur — it caches
  raster previews at a document raster-effects resolution, and offers **Expand**
  and **Rasterize** as explicit destructive escapes. `.ai` also embeds a PDF
  stream: again a derived copy stored beside the source.
- **Blender Grease Pencil.** The closest structural relative: point lists with
  pressure, rendered by a GPU engine every frame, no checkpoint because the render
  is fast enough to keep redoing. **That route is already closed here**, and
  deliberately — `docs/DESIGN-4k-playback-and-8k.md` rejects GPU stamping because
  GPU rasterization is not bit-identical across drivers, generations or operating
  systems, which under invariant 2 is flicker on a replayed sequence; and because
  smudge and the wet media read pixels back per dab, which on a GPU is a
  round-trip per dab.

**The pattern that decided Q60's first question.** Every application offering
geometry-as-truth restricts mark quality on those layers, and every application
that keeps full mark quality stores the pixels. Nobody makes replay fast — they
stop replaying. The initial recommendation here was a sidecar cache, reasoning
from Q55's logic about derived artefacts; the industry went the other way and
portability is why, so **in-document won**.

### Harmony is the same combination, and its own manual documents the same bill

Researched 2026-08-08 rather than assumed, because Toon Boom Harmony was the one
analogue whose internals were unknown here. It turns out to be the closest thing
to a precedent that exists, and it is not encouraging or discouraging so much as
*clarifying*.

Harmony offers textured brushes on vector layers, and describes them as
*"bitmap textures contained within vector envelopes, which results in the editing
capabilities of a vector line with the texture of a bitmap"* — on a vector layer,
*"a greyscale bitmap mask applied to their colour"*. So: geometry as truth,
textured marks, for animation. The same bet.

And their documentation states the cost plainly:

> *"While bitmap drawings are made of a single flat canvas, vector brush strokes
> are kept as separate objects, which means that laying on a lot of textured brush
> strokes on a vector drawing will require Harmony to store the texture for each
> of these strokes, and to composite them together in real time to display your
> drawing. This can cause texture-heavy vector drawings to be heavier on
> application performance and in file size than bitmap drawings."*

**Their mitigation is a stored bitmap per stroke** — a raster cache at stroke
granularity rather than a document-level checkpoint, and the reason the file grows.
It is the same instinct as this design, pushed one level down.

**And the price they pay is the one thing Lightbox does not.** Harmony's textured
strokes are *"resolution dependent, and are liable to lose quality and appear
pixelated if they are enlarged or zoomed in"*, with a pixel-density setting the
artist is told to pre-declare: *"if you intend to scale or zoom in on your artwork,
make sure your pixel density is set to be at least the factor by which your artwork
will [be] scaled or zoomed."*

That is the trade named exactly. Cache pixels per stroke and materialising is
cheap, but the mark is frozen at a resolution somebody had to guess in advance.
Re-stamp from geometry — invariant 7, output scale as a canvas transform — and the
mark is correct at any scale, but a bulk rebuild costs what B30 measures.
**The replay cost is what buys resolution independence.** It is a price for
something rather than an oversight, which is worth knowing before optimising it
away.

It also sharpens the checkpoint's shape: a checkpoint at *document* granularity
keeps resolution independence, because the strokes are still the truth and the
snapshot is discardable. Harmony's per-stroke texture *is* the truth, which is why
theirs cannot be thrown away and re-derived. Same technique, opposite standing.

### Three other things the research changed

- **PSD's composite is opt-in, not automatic.** It is written only when *Maximize
  (PSD and PSB) File Compatibility* is on — Adobe made the checkpoint a preference
  precisely because of the size cost. Worth copying: the in-document decision above
  should probably grow a setting rather than being unconditional, and that is a
  smaller question than the one Q60 answered.
- **Photoshop's history is bounded at 50 by default and 1 000 at maximum**, and
  Adobe ties it directly to scratch-disk pressure — *"you can save scratch disk
  space and improve performance by limiting or reducing the number of history
  states"*. The byte-budget framing Q60 chose is the same reasoning.
- **Blender Grease Pencil is not free either**, which weakens "the GPU solves it".
  Blender's own optimisation task records GP objects being *"several orders of
  magnitude slower than 3D meshes with the same complexity"*, because *"GP objects
  create a shading group for each stroke"*. Per-stroke overhead scaling with stroke
  count, on the GPU, tracked as a known problem. Nobody has this for free.

**Krita's limitation is confirmed and stronger than stated above**: the brush tool
is unavailable on a vector layer at all and *"you cannot use any brush preset on
it"* — its vector layers are general-purpose SVG, and a Krita developer's own
write-up (*Study of Editable Strokes for Inking*) treats editable textured strokes
as an open problem rather than a shipped feature. So this is not a capability
Lightbox is reimplementing late; it is one the most capable open-source painting
application has studied and not shipped.

## The decisions

Q60 carries the reasoning; this is what they mean for the build.

### The pixels live in the document

A new nullable field beside the strokes — **not `PngBase64`**, for the three
reasons above. Absent unless used, so a document that never checkpoints
serializes exactly as it does today.

The cost accepted: the file grows by roughly one full-canvas PNG per checkpointed
cel, so a big painting goes from a few megabytes to tens. Taken knowingly, because
a document that arrives on another machine without its checkpoint is a document
that opens in 106 seconds, and PSD and `.kra` both made the same call.

### Taken on save, rendered on a background thread

The save writes the record and returns immediately; the snapshot renders off-thread
and is attached when it finishes. Saving must never stall — that is the constraint
this was chosen against.

The consequence, stated: quit straight after saving and the checkpoint may be
missing. Harmless by construction — a missing checkpoint is a slow open, never a
wrong one.

### Any edit it covers drops it

Touch a stroke the checkpoint includes and the checkpoint is discarded; the next
save makes a fresh one. One slow reopen, then fast again.

Deliberately the coarsest of the three options considered. Keeping several
checkpoints at different depths would preserve the fast path more often and costs
several full-canvas images per cel plus a much subtler invalidation — and
invalidation is the half that fails by **showing stale art**, which this ledger
ranks worse than being slow. Coarse and obviously-correct wins.

Mechanically: store the count of strokes covered plus an ordered hash of their
ids, and accept the checkpoint only when the frame's leading strokes still hash
the same. `AfterStrokeEdit` already exists as the funnel the selection actions go
through and is where the drop belongs.

### Undo is bounded by memory, not by step count

Measured, because the intuition here is wrong too. On a 5 000-stroke painting:

| step kind | pushed in | held per step | undo cost |
| --- | ---: | ---: | ---: |
| Delta — every brush stroke | ~0.002 ms | ~0.9 KB | **0.07 ms** |
| Snapshot — every structural edit | **615 ms** | 2.85 MB | forces a full rebuild |

500 delta steps push in 1 ms, hold **433 KB total**, and undo at 0.07 ms each.
**Depth is free for painting**, so a step count prices a cost that is not there —
while 500 *snapshots* would hold 1.4 GB. So the limit is a byte budget with a
generous step ceiling, the way the frame cache is already budgeted: painting gets
its hundreds of steps for under a megabyte, and a snapshot-heavy session
self-limits.

`MaxUndo` is 64 today and appears in no UI. Raising it and exposing it is a
separate, smaller piece of work than this design; it is named here so the number
stops being invisible.

### The stall found on the way is B142

Measuring undo turned up something worse than undo: `DocumentEditor.Perform`
pushes `SnapshotStep(DocJson.Clone(Doc))`, so **every structural edit
serialize-and-deserializes the entire document** — 615 ms warm and ~1.1 s cold at
5 000 strokes, 72.5 MB allocated. Adding a layer to a painting freezes.

Filed as **B142** rather than folded in here: B30 is pixel replay, this is record
cloning, and one fix does not touch the other. They compound, because a snapshot
undo restores a whole document tree and then forces the rebuild B30 measures.

### What must be true regardless

- **A missing or unreadable checkpoint is not an error.** It degrades to a full
  replay, silently. B137 with a second caller.
- **Deleting every checkpoint changes no pixel.** Render with and without, compare
  bytes. If that ever fails the cache has become the source of truth and invariant
  1 is gone.
- **A checkpoint is never authored, exported, or sent to a model.** `Flatten` and
  every export path work from the record.
- **It is per cel, not per document**, or an animation with one heavy cel cannot
  use it.

## Not in this design

- **Un-gating tiling from `UnboundedCanvasOn`.** Worth doing, different problem,
  and it depends on why infinite canvas is broken.
- **Multi-threading the replay.** A real option — the dabs are deterministic and
  the frame could be split into bands — but it buys a constant factor against a
  linear growth, so it postpones the wall rather than removing it. Measure before
  believing it is enough.
- **Reducing the per-dab cost.** The 44× area ratio is the medium behaving
  correctly; a soft 60 px brush is *supposed* to cover 2 800 px². Nothing here
  should make a painterly mark cheaper by making it worse.
