# Q119 · What does a pose-mode grab do to a bone? — **answered 2026-08-18: the same thing the grab does in bind mode**

Reported by the owner, testing the rig as a construction guide for a run cycle:

> *"In pose mode I am unable to move the entire skeleton. Grabbing the first bone
> or any bone for that matter, just rotates the bones. Using transform only
> transforms the image. Not the bones."*

Both halves were true and neither was a bug in the ordinary sense. Every
pose-mode drag went to `PoseBoneTo`, which writes a rotation, whatever it had
hold of — so **no gesture translated an ordinary bone at all**. IK handles and
spline handles were the sole exception, because a handle is placed rather than
aimed and a rotation on one moves nothing. A character could be posed and never
moved, and the tool the owner reached for next moves strokes rather than bones.

| | What it costs |
| --- | --- |
| **The shaft and the joint carry, the tip aims** (recommended, **chosen**) | Rotation becomes a tip-handle drag only, so the muscle memory built over bone rounds 1–3 changes. |
| Shaft keeps rotating; the joint handle translates | Nothing existing changes — but the two modes stay inconsistent, and the move grab is a five-pixel handle instead of the whole bone. |
| A modifier + drag translates | Cheapest to learn from where the owner was standing, and invisible: nothing on the canvas says the gesture exists, and Shift and Ctrl already carry extrude and the weight modes. |

## Why the symmetry is the answer rather than a tidy-up

The grab already meant something in bind mode: **tip aims, shaft and joint
move**. Pose mode was reading the same three hits and collapsing them to one
verb. Restoring the distinction gives one sentence that covers both modes —
*the grab decides what kind of edit a drag is, the mode decides whether it
lands on the rest pose or on the pose key* — and it is the sentence the cursor
was already trying to tell: `CanvasCursor.ForBone` lost a branch when this
landed, because with the modes agreeing there was nothing left for it to say
about the mode.

It also answers *"move the entire skeleton"* without a command for it. Children
ride their parent through FK, so a translation written on the root carries
everything under it — one drag, no new concept, and it keys at the playhead
like every other pose edit.

## What is deliberately left alone

**Chain and spline bones keep their existing answer for every grab.** They are
*placed* rather than aimed or nudged, and the bone a drag on one moves is often
not the bone under the pointer — grabbing a forearm asks the hand to end up
somewhere — so a delta would have nothing to be a delta of.

Guarded by `PoseGrabTests`, which pins the carry, the aim, the joint place, the
child's parent-frame arithmetic, the preview agreeing with the release, and the
cursor. Five of its eight fail against the previous dispatch.
