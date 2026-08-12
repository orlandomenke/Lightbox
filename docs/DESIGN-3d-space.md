# A simplified 3D space to draw in: planes, parallax first, orbit second

Status: **designed, nothing built.** Decided with the owner on 2026-08-12 (Q72),
the same day the infinite canvas was removed (Q71) — this is the direction that
removal made room for. Nothing here exists in the tree yet; the roadmap items
under *Camera and scene* carry the evidence anchors that will say when it does.

## What it is

A document whose drawings live on **planes in a 3D space**: each drawing is a
flat canvas with a position and (eventually) an orientation, the artist can
rotate and zoom around the arrangement while working, and a camera renders it.
There are **no meshes and no 3D strokes** — the unit of art stays a 2D drawing,
exactly as the tool understands it today.

The decided constraint, and the reason the whole thing is cheap relative to
what it looks like:

> **Strokes never learn 3D exists.** A stroke is today's record, in
> plane-local 2D coordinates. The brush engine (the one pixel path), replay,
> undo, fills, selections, and the AI inbetweener are untouched. 3D is an
> *arrangement of drawings*, not a new kind of drawing.

The rejected alternative — stroke points carrying x,y,z — was priced and
declined: it rewrites the stroke record (invariant 1's format), `BrushEngine`
(invariant 1's only door), hit-testing, and the AI payload, to buy lines that
bend through space, which the reference workflow (Grease Pencil) mostly does
not use either.

## The two stages, in order

**Stage 1 — multiplane.** Layers gain a **depth**: parallel planes stacked
away from the picture plane, classic Disney multiplane. The camera stays the
2D record it is today (`CameraFraming`: x, y, zoom, roll); parallax falls out
of depth-dependent response to camera moves — a pan moves a distant layer
less, a zoom scales it less, by the perspective factor `f / (f + depth)`.
No orbiting in stage 1: that is the cost of shipping the small half first,
chosen knowingly (Q72 — the recommendation was free planes first, and the
owner picked the smaller, sooner ship).

**Stage 2 — free planes and orbit.** A layer's depth generalises to a **pose**
(position, orientation, scale); the working view gains an **orbit** — rotate
and zoom around the arrangement while drawing; the camera gains an authored 3D
pose of its own. Stage 1's record is designed as the degenerate case of stage
2's so there is no migration cliff: `depth: d` reads as a pose translated `d`
along the view axis, and a document authored in stage 1 opens unchanged in
stage 2.

## The rules it must keep

1. **Optional means absent.** A layer at its default depth writes no key; a
   document that never uses depth or pose serialises byte-identically to
   today. No 3D UI exists until the artist asks for it. This is the camera's
   precedent, applied again — and it is testable the cheap way:
   `Assert.DoesNotContain("\"depth\"", json)` on a fresh document.
2. **Orbit is view-only** (invariant 5). Rotating and zooming *while working*
   is navigation: never serialised, never in an export. What renders and
   exports is the **authored camera**. A document with no camera shows its
   planes head-on and behaves exactly like today — which also means **depth
   without a camera does nothing**, so multiplane never taxes an asset
   document and never conflicts with sprite export. No feature conflict is
   needed anywhere in this design.
3. **The stroke record is the document** (invariant 1). A plane's pixels are
   derived by exactly today's pipeline; the pose is document data *about the
   plane*, never about the strokes on it.
4. **Render bigger by scaling the surface, never the geometry** (invariant 7).
   Projection happens to the plane's *rasterised surface*, not to stroke
   coordinates — a plane is rasterised flat at a resolution chosen from its
   screen footprint, then drawn through a projective matrix (Skia's 3×3
   carries perspective). `Hash01`-seeded dab dynamics are therefore identical
   at every viewing angle, which is what keeps a turntable of the scene from
   boiling.
5. **No randomness** (invariant 2). Projection is a pure function of pose and
   camera; there is nothing stochastic to seed.

## How drawing works when the view is tilted (stage 2)

The pointer ray is unprojected against the **active layer's plane**; the
intersection is a plane-local 2D point; from there it is today's input
pipeline, unchanged. Drawing at a grazing angle is imprecise by geometry, not
by bug — the input maths must be exact and the UX may still warn. Stage 1 has
none of this: planes are parallel to the picture plane and input is untouched.

## Rendering and cost

Stage 1 is one extra affine per layer pass — the compositor already draws
passes through optional matrices, so multiplane is a per-layer scale/translate
derived from `(camera, depth)` where the identity is today's behaviour. It
must hold the existing performance shape: parallax changes *matrices*, not
*allocation*, so a camera move re-composites (which a camera move already
does) and repaints nothing per-layer that a plain pan would not.

Stage 2 replaces the affine with a homography per plane and adds the
footprint-driven raster scale. The known risks, written down before building:

- **Sampling under projection.** A tilted bitmap needs filtering; a filtered
  tile edge was the seam lesson of the tiled compositor. Planes are single
  surfaces (not tiles), so the seam risk is absent, but the filter choice must
  be pinned by a determinism test the way `ATiledRenderIsBitIdenticalToAnUntiledOne`
  pins the flat path.
- **Depth sorting, not intersection.** Planes composite painter's-algorithm by
  camera distance. **Intersecting planes are out of scope, permanently** — the
  simplification that keeps this a drawing tool rather than a renderer. Two
  planes at the same depth keep layer order, so today's documents (all planes
  at depth 0) keep today's compositing exactly.
- **Onion skin and the light table** show ghosts on the ghost's own plane;
  they ride the same projection and need no design of their own.

## Export

Through the authored camera, as today — parallax (stage 1) and pose (stage 2)
are camera-time transforms, so `SequenceExporter` picks them up where it
already applies `CameraTransform`. Assets are untouched by construction: no
camera, no parallax, no change.

## Reach and defaults

The Q21 shape, unchanged by Q71's removal of the conflict machinery: the
capability lives on the **document** (a depth or pose is authored data), a
**project** supplies what a new document starts with, and no project type gates
any of it. Shot-flavoured project types may default the Scene panel visible;
nothing more is needed.

## Not in scope, deliberately

- **Meshes, lighting, materials.** It is a drawing space, not a renderer.
- **3D stroke points.** Priced and declined above.
- **Intersecting planes.** See rendering.
- **Camera paths in 3D beyond keyed poses** (cranes, rigs) — keyed poses
  interpolate exactly as 2D camera keys do today; anything richer is its own
  design.

## Open questions

None blocking stage 1. Stage 2 will need: the pose interpolation space
(quaternion slerp vs Euler — determinism says pick one and pin it), the
grazing-angle drawing UX, and where the orbit gesture lives in `ShortcutMap`.
Ask them when stage 2 starts, per the rules — not guessed here.
