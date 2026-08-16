# Q100 · Bones phase 4: what a drawn corrective stores, and what drives it — **answered 2026-08-16, all four as recommended**

Prompted before starting phase 4 of `docs/DESIGN-bones.md`, with phases 1–3
landed and layer bindings reaching pixels.

The problem, so the answers read against it: linear-blend skinning collapses
the inside of a sharply bent joint and pinches the outside. No amount of
weight painting fixes it, because the correct shape at 120° is not a linear
blend of anything. A corrective lets the artist draw the correct shape once
and have it blend in as the angle approaches.

1. **Rest-space point deltas.** Offsets applied to the rest shape *before*
   skinning — corrective blend shapes, as 3D does them. The decisive property
   is that it composes with everything downstream for nothing: IK, spline
   chains, constraints and the pose all carry the corrected shape without
   knowing correctives exist. Posed-space deltas were declined because a fix
   authored with the shoulder down is wrong the moment the shoulder lifts — the
   correction has to rotate with the limb. A whole replacement drawing was
   declined because it cannot blend: it pops at a threshold, which is the
   thing correctives exist to avoid. Auxiliary bone rotations — Moho's literal
   smart-bone model — were declined last: they live on the rig and so fix every
   drawing at once, which is genuinely better, but they can only do what a bone
   can do and cannot move a line to where the artist drew it. Cost accepted:
   converting the artist's posed edit back into rest space, which is an inverse
   of the blended transform per point.
2. **Several stops on a ramp**, interpolated between and held outside the
   authored range — the pose track's own semantics, so a rig and a camera read
   the same way. One stop is then the degenerate case and costs nothing extra,
   where a single-extreme record would have needed a format change the first
   time an elbow wanted a different fix at 60° and at 130°.
3. **On the drawing.** A corrective names strokes by id, so it belongs to the
   frame that holds them and travels with it through copy, paste and symbols.
   For a cutout rig — one arm drawing reused across the whole sequence — the fix
   is authored once and applies everywhere it matters. Layer scope was declined
   because the ids do not match between drawings, so the deltas would land on
   nothing *silently*; rig scope only works with the auxiliary-bone storage
   that (1) declined.
4. **A named bone's pose rotation drives it** — the joint angle, which is what
   phase 4 is specified as. The design doc's later *Drivers* section wants a
   general driver (joint angles or named scalars like *body turn*) unifying
   correctives, depth swaps and turn interpolation; building it now would ship
   a record whose other two users do not exist, so it waits until they do.

**The honest limit, recorded because the manual has to say it:** a corrective
fixes *a drawing*. It will not fix two hundred hand-drawn frames. That is the
right trade for the workflow correctives serve — cutout and puppet rigs, where
a limb is one drawing — and the wrong tool for frame-by-frame, where the artist
is already drawing each frame correctly.

**One thing decided rather than asked, because it is a gesture rather than a
format:** authoring works by *baking, editing and diffing*. Entering corrective
capture bakes the pose into the drawing in place, so every existing tool works
on the posed shape with no new canvas machinery; capture diffs the edited
drawing against a fresh pose of the original, converts to rest space, and
restores the record. The alternative — teaching the canvas's point editing to
operate on a posed preview — is a much larger change to the pen and transform
paths for the same result.
