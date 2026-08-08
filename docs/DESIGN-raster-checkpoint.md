# The raster checkpoint: making a painting cheap to reopen

Status: **design only, nothing built.** Opened 2026-08-08 after B30 was measured
against a painting rather than a frame of line art. The decision to design before
implementing was deliberate: this adds a cache *inside* the artwork, and a cache
that can be silently wrong is worse than one that is slow.

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

## The open shape

Enough is settled to name the decisions rather than guess at them. Recommendation
marked in each case; costs stated for the alternatives.

### Where the pixels live

- **(a) In the document, in a new nullable field beside the strokes.** Simple,
  travels with the file, survives being copied to another machine. Cost: file
  size grows by a full-canvas PNG per checkpointed cel, and a `.lightbox.json`
  goes from megabytes to tens.
- **(b) In a sidecar cache file next to the document, or under the project.**
  *Recommended.* The document stays exactly what it is — no format change, no
  size growth, nothing new to migrate, and a cache that is obviously a cache is
  harder to mistake for art. It is also deletable as a support action: "throw
  the cache away and reopen" is a repair an artist can perform. Cost: a
  document mailed to somebody else arrives without its checkpoint and pays the
  replay once; the cache needs a location convention and an eviction policy.

Q55's reasoning applies almost verbatim — *"with nothing committed there is
nothing to drift, nothing to merge and nothing to verify"* — and it argued for
keeping derived artefacts out of the tracked file. This is the same argument
pointed at a document instead of a repository.

### When a checkpoint is taken

- **(a) On save.** Predictable, and the artist is already waiting.
  *Recommended*, with the render on a background thread so a save does not block
  drawing.
- **(b) Every N strokes.** Bounds the worst case tightly, but pays repeatedly
  during work, which is the one moment that must stay cheap (invariant 6).
- **(c) On idle.** Best of both in theory; needs an idle notion the app does not
  currently have.

### How it is invalidated

The hard half. A checkpoint is valid for a specific *prefix* of the record, so
it has to name that prefix in a way that cannot be forged. Sketch: store the
stroke count it covers plus a hash of those strokes' ids in order, and accept the
checkpoint only when the frame's leading strokes still hash the same. An
`AfterStrokeEdit`-style hook already exists for the selection actions and is the
natural place to drop a stale one.

**What must be true regardless of the answers**, because these are the
invariants rather than the preferences:

- **A missing or unreadable checkpoint is not an error.** It degrades to a full
  replay, silently. This is B137 with a second caller: today unreadable pixels
  throw out of `Materialize` and take the repaint down.
- **Deleting every checkpoint changes no pixel.** The test writes itself: render
  a document with and without, compare bytes. If that ever fails, the cache has
  become the source of truth and invariant 1 is gone.
- **A checkpoint is never authored, exported, or sent to a model.** It is not
  content. `ProjectIo.Flatten` and every export path must ignore it and work
  from the record.
- **It is per cel, not per document.** A painting is one cel, but the machinery
  belongs where the strokes are, or an animation with one heavy cel cannot use
  it.

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
