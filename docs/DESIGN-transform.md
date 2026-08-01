# Transform tool design (M14)

## Research summary

**Krita** splits its transform tool into five modes — Free Transform
(move/rotate/scale/shear), Perspective (corner + vanishing-point drag), Warp
(grid/point displacement with Rigid/Affine/Similitude falloff), Cage
(closed-polygon deformation via generalized barycentric coordinates), and
Liquify (brush-painted displacement). See the
[Krita transform tool manual](https://docs.krita.org/en/reference_manual/tools/transform.html).

**Animation is where every raster tool struggles.** Krita's own answer is the
[transform mask](https://docs.krita.org/en/reference_manual/layers_and_masks/transformation_masks.html):
a non-destructive transform attached to one layer (or group), tweenable since
5.0 via the animation-curves docker. It cannot transform *the drawings
themselves* across a range of frames: users report going frame by frame with
the transform tool to keep elements aligned
([community discussion](https://krita-foundation.tumblr.com/post/139478685143/krita-animation-and-transform-mask-question)).
Photoshop's video timeline has the same shape: Free Transform touches one
layer at one time; batch moves need smart-object surgery. The reason is
structural — for a raster tool, every transformed frame is a fresh
resampling of pixels, so a 24-frame transform is 24 destructive resamples
and 24 undo records.

**Lightbox does not have that constraint.** Frames are stroke records; a
transform is a deterministic geometry operation on `StrokePoint`s. Applying
the same operation to 200 frames is 200 cheap point-map passes with *zero*
generational quality loss (strokes re-render through `BrushEngine` at full
fidelity), one undo step, and a record the AI inbetweener can still read.
That is the unfair advantage this milestone exploits.

## Scope model — the novel part

A transform session has a **target scope** chosen in the tool options:

| Scope | What is transformed |
|---|---|
| **Active cel** | The exposed drawing on the active layer at the playhead (classic Ctrl+T). |
| **All layers at this frame** | Every visible layer's exposed drawing at the playhead — move a whole composed pose. |
| **Selected cel range** | The cels marked with the timeline range selection (Set start/end), on that layer. |
| **Entire animation** | Every drawing on every layer — reframe/rescale the whole shot. |

Rules that make ranges safe:

- **Deduplicate by `Frame.Id`.** Holds expose the same `Frame` instance on
  several timeline columns; a scope collects *distinct* frames so nothing is
  transformed twice.
- **Region limiting.** If a selection is active when the session starts, only
  strokes whose points lie (majority) inside the selection mask are
  transformed — in *every* frame of the scope. This is "enlarge this part of
  the document across all frames": select the region once, pick a range
  scope, scale. Strokes are moved whole (no point-tearing) in v1; soft
  boundary falloff is a warp-phase follow-up.
- **One undo step.** The whole session commits as a single `Perform`
  snapshot, whatever the scope size.

## Geometry

- **Affine** (move/scale/rotate, uniform or free): closed-form 3×2 matrix
  built from the gizmo (pivot, scale x/y, angle, translation). Applied to
  every stroke point. Brush size scales with √(|sx·sy|) so lines thicken
  proportionally — stored per stroke, so provenance holds.
- **Perspective**: homography solved from the 4 corner drags (direct
  8-unknown linear solve, no external deps), applied per point.
- **Warp / cage / liquify** (phase 2/3): all are per-point displacement
  fields, which the stroke record absorbs naturally; cage uses mean-value
  coordinates. Not in this milestone.
- **Raster baselines** (`PaintedFrame.PngBase64`): the one pixel path.
  Affine/perspective baselines are resampled once per commit through a Skia
  matrix draw. Warp-family transforms will refuse baselines until phase 2.
- Fill strokes and clip regions transform with the same point map, so fills
  and masked strokes stay consistent.

## UI

- **Ctrl+T** enters transform mode (any paint/select tool); **Enter/click
  outside commits, Esc cancels**.
- Gizmo: bounding box of the scoped content with 8 scale handles, rotation
  by dragging outside a corner, move by dragging inside. **Perspective
  sub-mode** switches the corners to independent quad drag.
- Live preview: the active frame's affected pixels are drawn through the
  session matrix (cheap bitmap warp); exact stroke re-render happens on
  commit. Other frames in the scope commit without preview — the timeline
  thumbnails refresh immediately after.
- Tool options while in session: scope combo, perspective toggle, numeric
  angle/scale readout, Commit / Cancel buttons.

## Why not transform masks?

Non-destructive animated transforms (Krita-style masks + curves) solve a
different problem — *motion over time*. Lightbox's inbetweener already owns
motion synthesis from drawings. What artists cannot do elsewhere is edit the
drawings themselves in bulk; that is what this tool does, destructively but
losslessly, in the stroke domain.
