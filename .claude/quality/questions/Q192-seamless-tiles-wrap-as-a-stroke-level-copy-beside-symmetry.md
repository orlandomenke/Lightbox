# Q192 · Seamless tiles: wrap as a stroke-level copy beside symmetry, and what the first landing reaches — **answered 2026-10-06**

**Answered:** "seamless" means the **tile edges**, spatially — the left edge
continues on the right and the top on the bottom; the **symmetry UI lands
first**, because the engine behind it is complete and no artist can reach it;
the wrap setting lives **on the stroke**, nullable, exactly as `Symmetry` does;
and the first wrap landing reaches **brush and eraser**, with the export fix and
the tiled view, and leaves fill, smudge and blur for their own branches. Every
recommendation was taken.

Raised by the request *"focus on symmetry drawing and especially patterns — for
my game I want seamless sprites, so start and ends should line up perfectly."*

## 1 · What "start and end" means

Asked because the sentence reads two ways and the two features share no code:
the first and last **frame** of a cycle lining up is onion skin, playback and the
inbetweener; the left and right **edge** of a tile lining up is the brush path.
The answer is the edge. Temporal looping is noted here as unasked — the scene
already carries `Loop`, and the walk analyser knows about loop slack, but onion
skin does not wrap round a cycle and nothing checks that frame N flows into
frame 0.

## 2 · Symmetry UI first

`SymmetryAxis`, the stamping and the live preview all landed on 2026-09-10
(Q185). `MainViewModel.ActiveSymmetry` is set by tests and by nothing else: no
toggle, no gizmo, no shortcut, and the manual lists brush symmetry as *Planned*.
Wrap wants the same tool-options surface and the same gizmo, so building the
surface once for symmetry and adding wrap to it is cheaper than building it twice.
The alternative — wrap first with a bare toggle — gets tiles into hands sooner
and pays for the surface twice.

## 3 · Where the wrap record lives

**On the stroke**, as a nullable sibling of `Stroke.Symmetry`, absent unless
used. Invariant 4: it reaches pixels, so a mark carries the tiling it was drawn
under and turning the mode off later never changes existing art. A per-document
flag would be a smaller record, and switching it off would re-render every
stroke in the document; a reload could not tell which marks were drawn wrapped.

## 4 · Why wrap is the same mechanism as symmetry, and what that buys

Symmetry stamps the stroke once per copy at its **authored** coordinates onto a
canvas carrying the copy's matrix, so `Hash01` sees the drawn coordinates every
time (Q185 §2). A tile wrap is one more kind of copy — a translation by
`(±W, 0)`, `(0, ±H)`, `(±W, ±H)` — and inherits everything that argument bought:

- the copy that comes in on the far edge carries identical grain, scatter and
  jitter, so **the seam is invisible by construction**, with no edge-matching;
- each copy already bounds itself and clips to the document, so a stroke in the
  middle of the tile culls to the single drawn copy and **cost follows the edge,
  not the canvas** — the `DrawingCostBaselineTests` fingerprint must not move;
- placements compose: symmetry × wrap gives the rectangular wallpaper groups
  (mirror + wrap is pm/cm, order 2 is p2, order 4 on a square tile is p4/p4m).
  Orders 3 and 6 need a hexagonal lattice and do not tile a rectangle; the UI
  should say so rather than silently produce a pattern that does not repeat.

A brick stagger — the horizontal wrap carrying a half-tile vertical shift — is
one extra field on the record and is deferred, not rejected.

## 5 · Reach of the first landing

Brush and eraser, free from the placement loop. Two things ride along because
they are correctness rather than features: the sprite-sheet export must not
**trim** a wrapped tile (union trim cuts it and destroys the seam) and its gutter
should be **extruded** from the wrapped neighbour's pixels rather than left
transparent, which is what an engine wants against bilinear bleed. The tiled
3×3 view, and painting into a neighbour cell by taking pointer coordinates
modulo the tile, are view-only (invariant 5).

Excluded, as Q185 excluded them for symmetry: **fill** wraps naturally as a
toroidal flood, but its contours must split at the seam to keep invariant 3;
**smudge and blur** read pixels and need wrap-aware reads. Each is its own
branch with its own question. Taking fill in the first landing was offered and
declined — flat-colour tiles lean on it, and it would have doubled the branch.

## Unasked, recorded here rather than filed

- **"Break symmetry" cannot be a geometric expansion.** Q15 named it as the
  act that turns one symmetric stroke into N ordinary ones. Transforming the
  points re-rolls every `Hash01` dynamic, so the broken copies would be
  different marks from the ones on screen — exactly what Q185 §2 refused. It
  needs its own question before it is built; it is not part of the symmetry UI
  branch.
- **Temporal looping** (§1).
- **Fill, smudge and blur under wrap** (§5).

**Blocks:** nothing. The symmetry UI can be built now; wrap follows on its own
branch.
