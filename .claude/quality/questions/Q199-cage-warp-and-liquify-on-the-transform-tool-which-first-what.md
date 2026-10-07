# Q199 · Cage warp and liquify on the Transform tool: which first, what shape, how undone, and what happens to imported pixels — **answered 2026-10-06**

Raised by the owner, 2026-10-06, during the B381 work: *"I also want to add
liquify/caged transform options to the tool."* The roadmap already held
`[x] Warp transform` (the perspective quad and the band scale, Q184) and
`[?] Liquify` with nothing decided.

What it blocks: the cage warp (this branch) and liquify (its own branch
after it).

Both fit the Transform tool as it stands: perspective and the band scale are
point maps applied to the stroke record, the preview composes the moving
pixels through per-cell passes, and the pose-space write-back from B381 takes
any point map. What had to be decided was order, the cage's shape, liquify's
undo grain, and what a raster baseline does under a warp that is not one
matrix — the band scale leaves those pixels where they are and says so.

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **Order** | **Cage first, then liquify** (recommended) | Liquify first: a brush-driven interaction, cursor and undo story to design before anything shows. Both at once: two objectives on one branch. |
| **Cage shape** | **Lattice grid, 3×3 by default, adjustable 2–6, smooth (bicubic) interpolation** (recommended) | A free polygon cage with mean-value coordinates bends a limb better and costs heavier maths per point; a single Coons patch previews cheaply but its inverse is iterative, which determinism has to be careful with. |
| **Liquify undo** | **One undo step per brush stroke** (recommended) | A confirm-or-cancel session like Ctrl+T leaves one mistake fixable only by pushing back or cancelling the lot. |
| **Imported pixels** | **Warped through the same mesh** (recommended) | Pixels staying put, as under the band scale, is cheapest and leaves a photo under a drawn character behind. |

**Recommendation:** all four as marked, and the owner took all four.

## What follows from them, not up for preference

- **The record stays strokes.** A cage or a liquify push is a point map over
  the stroke record — never a pixel warp of the drawing (invariant 1). The
  pixels an artist sees after a confirm are a re-render of moved points.
- **Straight segments must bend.** A point map applied to two endpoints keeps
  the segment between them straight, so both modes insert points along a
  segment the warp curves, the way the band scale inserts on its dividers,
  and the way B381's write-back densifies a sparse bound stroke.
- **Deterministic by construction.** Bicubic interpolation over a fixed grid
  and a displacement field summed in stroke order are both plain arithmetic;
  nothing seeds from a clock or an index (invariant 2).
- **Posed drawings go through B381's mover**: the cage acts on the posed
  picture and is written back to rest, like every other transform now.
- **Preview and commit are the same map.** The band scale's seam — a pass list
  of axis-aligned crops with one matrix each — cannot carry a lattice, so the
  preview grows a mesh pass (Skia's `DrawVertices` over the lattice's
  cells, one textured quad per cell, split into triangles) and the commit
  resamples a baseline through that same mesh.
