# Q72 · The 3D drawing space: what carries the art, what the view is, and how much ships first — **answered 2026-08-12**

**Asked with the question prompt, answered by the owner in one pass.** This is
the feature the infinite canvas was removed for (Q71): a simplified 3D
environment to draw in — rotate, zoom — carrying 2D line data, no meshes.
Design in `docs/DESIGN-3d-space.md`; roadmap items under *Camera and scene*.

Four questions, four answers:

1. **What carries the drawings? — (a) planes in space, as recommended.** Each
   drawing is a flat canvas with a 3D placement; strokes stay today's 2D
   records in plane-local coordinates, and the brush engine, replay, undo and
   the inbetweener never learn 3D exists. True 3D stroke points were priced —
   a rewrite of the stroke record, `BrushEngine`, hit-testing and the AI
   payload — and declined.
2. **View vs camera? — (a) orbit is view-only, as recommended.** Navigation
   while working is never serialised and never exported (invariant 5); what
   renders is the authored camera, extended to a 3D pose in stage 2. A
   document with no camera shows planes head-on and behaves exactly like
   today.
3. **How much first? — (b) multiplane first, against the recommendation.**
   The recommendation was free planes + orbit in the first version, because
   "rotate around the scene" was the stated wish and multiplane cannot do it.
   The owner chose the smaller ship: stage 1 is depth-stacked parallel planes
   with parallax under the existing 2D camera; free orientation and orbit are
   stage 2. **What that choice costs:** no orbiting until stage 2 — stage 1
   delivers depth and parallax, not the rotatable space. What it buys: a
   ship that touches only per-layer matrices, and a record (`depth`) designed
   as the degenerate case of stage 2's pose so nothing is thrown away.
4. **Deliverable now? — (a) design doc + roadmap, as recommended.**
   Implementation starts as its own branches per the one-objective rule.
