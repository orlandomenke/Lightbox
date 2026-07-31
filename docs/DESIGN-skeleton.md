# Design: pose-assisted frame-by-frame animation (rigid bones + AI gap filling)

Status: **agreed design, not yet implemented** (character sheets — the
prerequisite — shipped as M5a). This is deliberately **not** Spine/Toon Boom
puppet animation: the output of every pose is an ordinary drawn keyframe on
the timeline, keeping the frame-by-frame feel. Bones exist to *draft the next
frame*, and AI keeps the character complete and on-model where the pose
reveals things that were never drawn.

## The idea in one scenario

Side-view run cycle. The character is drawn once; the near arm covers part of
the torso. The animator swings the arm forward with a bone:

1. The arm's strokes move rigidly with the bone — the artist's own lines,
   re-posed, as the draft of the next frame.
2. The torso region the arm used to cover is now blank, and part of the
   character's back has rotated into view that never existed in any frame.
3. The AI is asked to fill exactly those regions with strokes, using the
   document's **character sheet** (front/side views) as the authoritative
   design.
4. The result is committed as a normal keyed frame the artist keeps drawing
   on. Nothing about playback, inbetweening, or export changes.

## M5b — Rig: bones + stroke bindings

Per-layer rig, serialized in the document like everything else:

```jsonc
"rig": {
  "bones": [ { "id": "b1", "name": "upper-arm", "parentId": null,
               "x": 410, "y": 220, "length": 60, "angleDeg": -35 } ],
  "parts": [ { "id": "p1", "name": "arm", "boneId": "b1", "zOrder": 2,
               "region": [ {"x":..., "y":...}, ... ],   // closed spline
               "strokeIds": ["s12", "s13"] } ]
}
```

- **Rig mode** in the tool bar: draw bones (click joint, drag tip; a bone
  started on another bone's tip becomes its child), then draw closed spline
  regions to cut parts. Strokes are assigned by containment; a stroke crossing
  a region boundary is split at the intersection so each side belongs to its
  own part (`GeometryOps` gains point-in-polygon and segment–polygon
  intersection).
- A part with no region binds a whole layer — the trivial pre-separated case.
- `Part` is the extension point for the **later mesh/spline deformation
  feature**: a lattice per part can replace the rigid transform without
  touching bones, bindings, or the AI contract.

## M5c — Posing → next-frame draft

- Pose mode on a keyed frame: dragging a bone rotates it (and its children)
  around its joint; each bound part's strokes get the same rigid transform,
  rendered in `zOrder`.
- **Commit pose** creates a new keyed frame whose stroke record is the
  transformed copy (role: key). It is a draft: the artist draws over it with
  the normal tools.
- No automatic pose tweening — the deterministic stroke inbetweener stays the
  interpolation between drawn frames. (Pose-aware inbetweening can be added
  later as an *option* once poses exist on keys.)

## M5d — AI gap filling

- After a pose, exposed regions = (previous cover of the moved parts) minus
  (their new cover), via polygon difference on the part regions; coarse
  convex hulls are enough — the AI needs a *where*, not a pixel mask. The
  same computation naturally captures body areas newly revealed to the
  observer (the run-cycle back: the torso region no longer covered by the
  arm).
- Request to the artist (API or MCP): posed frame render + gap hull(s) +
  character-sheet views (`ReferenceImages`, shipped in M5a) + the strokes
  neighbouring each gap. Response: strokes in the existing wire format,
  inserted through the existing validated, undoable path, and tagged with the
  covering part id so subsequent poses carry them along.
- One button in the UI: **“Pose → fill gaps”**. Deterministic fallback when
  no AI is configured: borrow and rigidly fit strokes from the matching part
  of a reference view.

## Later, separate features

- **Mesh/spline deformation** per part (bend a limb instead of rotating it) —
  builds on `Part` without changing the rest.
- **Pose-guide mode**: skeleton-only keys; the AI draws complete frames from
  the stick figure + character sheet.
