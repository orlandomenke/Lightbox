# Q167 · How should undo restore a drawing's pixels? — **answered 2026-08-26: snapshot the mark's tiles, with a memory budget**

Asked at the end of **B327**, which stopped undo re-stamping every stroke on a
drawing and made it repaint the reverted mark's footprint instead. That fix is
real — a scattered 800-stroke drawing went from ~1 498 ms per Ctrl+Z to 6.7 ms —
and it leaves two cases it cannot help, both of which have the same cause: it
still rebuilds pixels by **replaying the stroke record**.

- A mark whose footprint covers most of the drawing. Every other stroke is
  inside the patch, so replaying the patch means replaying everything.
- A **smudge or blur** anywhere in the patch. An effect brush reads the pixels
  it sits on, and outside the clip the bitmap holds the drawing as it stands
  *now* — including strokes painted after it — where a full render would have
  shown it only what came before. B327 has to refuse the whole patch and fall
  back. No coordinate conversion fixes that.

The alternative is what Photoshop and Krita do, and neither of them replays
anything: **the pixels are the document**, so undo copies the affected tiles
aside before a stroke touches them and swaps them back afterwards. Photoshop
calls the results history states and spills them to a scratch disk; Krita keeps
a memento of the changed tiles on its undo stack. Their undo costs the *area*
changed and nothing else — it does not care how many strokes are in the way.

| | What it costs |
| --- | --- |
| **Snapshot the mark's tiles, with a memory budget** (recommended, **chosen**) | Memory per undo step. Falls back to B327's replay when a mark's footprint exceeds the budget. |
| Snapshot everything, no fallback | Simpler, but memory tracks the largest marks, and a full-canvas stroke at 4K is ~33 MB per step against a `MaxUndo` of 64. |
| Keep replay only | Nothing new to build, and accepts both cases above permanently. |
| Record it, do not build | Costs nothing and leaves a P1's two known holes open. |

**Lightbox cannot simply become Photoshop**, and the reason is invariant 1: the
stroke record *is* the document and the pixels are derived. That is what buys
the AI inbetweening — you can only interpolate strokes you still have — the
sharp re-render at any zoom, and a reload producing the identical image. This
question is not about giving that up. It is about whether the **undo stack** may
additionally hold pixels as a cache of a state the record can already describe.
It may: a snapshot that disagreed with the record would be a bug, not a second
source of truth, and `UndoRegionRepaintTests` already compares against a
from-nothing `Materialize` at bit-identity.

## Measured before it was decided, and the measurement moved the answer

The owner's instruction was *"or test it"* rather than decide it on paper. A
throwaway probe compared, on one drawing: copying the pixels under a mark
(what every commit would newly pay), blitting them back (what undo would
become), and B327's replay as it stands. 960×540 document.

| drawing | patch | snapshot on commit | restore on undo | replay (undo+redo) |
| --- | --- | --- | --- | --- |
| scattered, 200 strokes | 42×34, 6 KB | 0.056 ms | 0.036 ms | 41.5 ms |
| scattered, 800 strokes | 42×34, 6 KB | 0.007 ms | 0.016 ms | 39.4 ms |
| hatched band, 200 strokes | 322×222, 279 KB | 0.039 ms | 0.058 ms | 2 104.7 ms |
| hatched band, 800 strokes | 322×222, 279 KB | 0.039 ms | 0.045 ms | **7 497.3 ms** |
| canvas-crossing, 50 strokes | 952×531, 1 975 KB | 0.695 ms | 0.937 ms | 1 600.6 ms |

**Restore tracks the patch area and nothing else** — 0.016 ms to 0.937 ms across
a 300× range of stroke counts. Replay tracks how much ink is in the way, and in
the case B327 cannot help it reaches **7.5 seconds** for one undo-and-redo. The
two designs are not close.

**The worry that prompted the test was the wrong one, and that is worth
recording.** The stated concern was commit cost: undo getting faster is no good
if every pen lift gets slower, because drawing happens a thousand times an hour
and undo does not. Measured, the copy costs **0.039 ms on a 279 KB patch** and
**0.695 ms on a 2 MB one**, against the ~7.5 ms a stroke commit already pays. It
is noise except on a full-canvas mark, where it is ~9% of a commit. The concern
was real enough to test and did not survive the test.

**So the budget's justification moved.** It is not about commit time. It is
memory at large canvas sizes: 1 975 KB per step for a canvas-crossing mark on a
960×540 document is ~126 MB across a 64-step history, and the same mark at 4K is
~33 MB per step — over 2 GB. The budget is a guard against the pathological
case, not a core part of the design, and on ordinary drawings (6 KB a mark) it
will never engage.

## What the answer does not settle

- **The budget's number**, which wants measuring against `MemoryBudget` rather
  than picking. It should be expressed the way `FrameBitmapCache.ByteBudget`
  already is — a total, not a per-step cap — so that trimming is an eviction
  policy rather than a refusal at commit time.
- **Whether snapshots replace B327's replay or sit above it.** Replay is still
  needed for a step that names no footprint, and for a snapshot trimmed out of
  the budget, so both paths stay. `MainViewModel.FrameRenderDrops` is the
  counter that would say how often the fallback fires in real work.
- **Structural undo is untouched.** A `SnapshotStep` has no mark to name, so
  adding a layer or applying a template keeps today's whole-document behaviour.
- **The tile machinery exists and should be reused, not reinvented** —
  `TileGrid`, `TiledRasterizer` and `_tileFrames` are already the squares this
  would snapshot, and B195 records what tiling has and has not been able to
  promise about effect brushes.
