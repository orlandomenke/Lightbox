# Q65 · Should strokes be merged to shrink a document? — **answered 2026-08-10: no — compress and quantise instead**

Raised by the owner with a detailed proposal: merge expired strokes at the undo
horizon, union paths with CSG, run Ramer–Douglas–Peucker over committed
strokes, chain strokes that meet end-to-end, and fall back to raster caches —
all under the constraint *avoid rasterisation, because the application promises
both raster and vector*.

**The constraint is right and the answer is no anyway**, because the cheap wins
are elsewhere and every merge on that list is either a determinism break or
worth nothing. Measured on a 400-stroke 1920×1080 painting with one brush
preset, which is the ordinary case for a painting:

```
                        raw          gzip
as saved (indented)   9,792,336    1,541,012
compact               3,952,622    1,367,127
points alone          3,675,740    1,353,826   91% of stroke bytes
400 brush blocks        237,601        1,521
points at 2dp         1,920,418      371,661
```

**Three findings, and the third is the one that changes the plan.**

- **The file is uncompressed pretty-printed JSON.** `DocJson.Options` sets
  `WriteIndented = true` and `Save` writes the string straight out. Compressing
  it is 6.4× for no semantic change at all, and it keeps the readable formatting
  the serializer's own comment chose on purpose.
- **Deduplicating brush settings is worth nothing.** This was the most promising
  idea going in — 41 properties inlined on every stroke while `ClipRegion` is
  already a content-hashed registry, so the house pattern existed and the
  retrofit was obvious. 400 identical brush blocks are 238 KB raw and **1.5 KB
  gzipped**: gzip already does the dedupe, and the change would buy ~0.1% of a
  compressed file. Worth recording precisely because it looked like the answer.
- **Coordinate precision is the whole game.** Points are 91% of stroke bytes,
  and storing two decimal places rather than a round-trippable double takes them
  from 1,354 KB to 372 KB gzipped — a 73% cut of the 91%, without touching one
  piece of geometry.

**Why every merge on the list is refused.**

- **RDP is a determinism break, not a compression.** "Reduces coordinates by 70%
  without altering the shape" is true of a plain vector outline and false here:
  every dab dynamic is seeded from the IEEE-754 bits of the dab's position
  through `Hash01`, so moving a control point does not give the same mark with
  fewer points, it gives **a different mark**. That is invariant 2, and invariant
  7 exists because the identical trap bit once already at output scale.
- **Chaining strokes end-to-end breaks the dab walk.** The walk is a fold
  carrying spacing phase, travelled distance and heading; concatenating gives the
  second stroke's dabs the first one's accumulated state, so the dabs move and
  the seeds move with them. B45 is this bug, already paid for once.
- **There is nothing to union.** A stroke is a centreline with width, pressure
  and per-dab dynamics, not an outline. CSG would require outlining it first,
  which destroys the thing that makes the mark.
- **Merging at the undo horizon makes the document depend on session length**
  and on a UI preference — the same drawing saved twice would differ.
- **The raster cache already exists** and is better than the proposal's version:
  `TileStore`, `TilePyramid`, `FramePrewarmer`. Vectors on disk, pixels in
  memory, non-destructive. That part is a no-op.

**And one cost the proposal could not see: stroke identity is an input to the
inbetweener.** It matches strokes between frames. Merging strokes to save bytes
spends the thing the headline feature runs on — which is a worse trade than any
byte count makes it look.

**So the order of work, if this is ever picked up:** compress the container
first; quantise coordinates second, and *at capture, never at save* — rounding a
committed point is RDP's bug wearing a smaller hat, and it has to happen before
the point enters the record so the live preview and the commit see the same
numbers. Flat point arrays third, reusing Q18's answer. Together roughly 10×
before a single stroke is merged.

**Not filed as a bug**, because nothing is broken: a large file is a cost, not a
defect. It belongs on the roadmap when file size actually hurts somebody.

**2026-08-13: it hurts somebody, and the first step is built.** The owner
reported large paintings costing minutes to open and slowing the session, and
chose (prompted, two questions): **phased** — container compression now, the
raster checkpoint (Q60/B30) next on its own branch — and **quantisation
deferred** to its own branch, on the recommendation that gzip alone is 6.4× and
a capture-path change deserves its own tests. `DocJson.Save` now writes gzip
(`CompressionLevel.Fastest`, streamed, atomic) and `Load` sniffs the container
so every pre-existing plain-JSON document loads unchanged;
`DocJsonCompressionTests` guards both directions and prints the achieved
sizes. Flat point arrays (Q18's answer) remain third in the order this entry
set. The in-session half of the report was B187 — autosave serializing on the
UI thread — fixed on the same branch.

---
