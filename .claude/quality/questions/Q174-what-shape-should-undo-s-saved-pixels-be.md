# Q174 · What shape should undo's saved pixels be? — **answered 2026-08-31: the exact rectangle**

Raised by: building the *Undo restores a mark's pixels instead of rebuilding
them* roadmap item, which **Q167** answered — and which contains one instruction
that does not survive contact with the code it has to be written against.

What it blocks: nothing now. It blocked the shape of `MarkSnapshot`'s storage,
and it is recorded because the answer **goes against a sentence in an answered
question**, which is exactly the kind of deviation that must not be made quietly.

## The instruction, and why it does not fit

Q167 says:

> The tile machinery exists and should be reused, not reinvented — `TileGrid`,
> `TiledRasterizer` and `_tileFrames` are already the squares this would
> snapshot.

That is right for Krita, which is the app the answer was reasoning from, and
whose canvas **is** tiles: snapshotting a tile there is snapshotting the unit the
pixels already live in, and unchanged tiles can be shared between history states
by reference.

Lightbox's are not. The pixels undo has to repair live in
`FrameBitmapCache`, which holds **one flat `SKBitmap` per frame**. There is no
tile to take and nothing to share by reference, so tile addressing would not
reuse machinery — it would round a mark's footprint out to a grid for no benefit
and considerable cost.

**Recommendation: the exact rectangle** — and it was chosen.

| | What it costs |
| --- | --- |
| **The exact clamped footprint** (recommended, **chosen**) | Deviates from the sentence above, and the roadmap's evidence anchors had to be renamed from `TileSnapshot` / `UndoTileSnapshotTests` to `MarkSnapshot` / `UndoMarkSnapshotTests`. |
| Tile-aligned via `TileGrid` at 256 px | Follows the instruction literally. A 42×34 mark becomes one to four tiles — **256 KB to 1 MB per step against 6 KB** — which contradicts the measured figure Q167's own budget argument rests on. |
| Tile-aligned with a smaller tile size | Bounds the waste, but introduces a second tile size to the codebase and buys nothing the rectangle does not already give. |

## Why the number matters rather than the tidiness

Q167 decided the budget on this table, and the first row is the ordinary case:

| drawing | patch |
| --- | --- |
| scattered, 800 strokes | 42×34, **6 KB** |
| hatched band, 800 strokes | 322×222, 279 KB |
| canvas-crossing, 50 strokes | 952×531, 1 975 KB |

Its conclusion — *"on ordinary drawings a mark is 6 KB and it never engages"* —
is what makes the budget a guard rather than a design constraint. Tile-aligning
multiplies the first row by 43× to 170×, and the guard would then engage in
normal drawing: a 64-step history of small marks would cost tens of megabytes
instead of the **713 KB** measured on the built version.

So the two halves of Q167 were in conflict, and the half backed by measurement
won. The rectangle is also what makes the saved region and the repaired region
**the same rectangle by construction** — `MainViewModel.RegionOf` and
`FrameBitmapCache.ClampedRegion` are each one function with three callers, rather
than two readings of one rounding rule.

## What holds it

`AMarksPatchIsItsOwnAreaRatherThanAGridOfTiles` asserts a small mark's patch is
kilobytes. It is an upper bound rather than an exact size, because pinning the
size exactly would make it a test of `BrushEngine.CommitBounds` instead — but it
fails immediately if a patch ever quietly becomes tile-sized, which nothing else
in the suite would notice.
