# Q121 · Should the Transform tool carry the armature? — **answered 2026-08-18: no — the pose-mode shaft drag is the answer**

Raised by the second sentence of the owner's report: *"Using transform only
transforms the image. Not the bones."*

| | What it costs |
| --- | --- |
| **Leave it; the pose-mode carry answers it** (recommended, **chosen**) | Moving a rigged drawing with the transform gizmo still leaves the skeleton where it was. |
| Transform carries the armature when the layer is rigged | A real design with real edges — see below. |
| File it and decide later | Nothing changes now, and the complaint stays live. |

**The owner picked the coupling first and reversed to the recommendation the
same day.** Recording both, because the reversal is the useful part: the
complaint dissolved once Q119 landed. It was never "the transform tool is
wrong", it was "there is no way to move a character", and there now is.

## The edges the coupling would have had to answer

Worth keeping written down, because the question will come back the first time
somebody nudges a rigged drawing:

- **The armature is document-global; a transform is scoped.** Carrying the rig
  on an `ActiveCel` transform would move the skeleton on *every* frame to fix
  the one being looked at.
- **The pose track could absorb the frame-local case** — write the delta as a
  root pose key at that frame — but only translation and rotation. `BonePose`
  has no scale, so a squash would move the joints correctly and leave the bone
  lengths behind.
- **A partial selection is not a character.** Transforming three lines of a
  drawing must not move the skeleton, so the coupling needs a "this is the
  whole thing" test that the transform tool does not currently have a reason to
  compute.
- **Perspective warps have no bone equivalent at all.**

None of these is unanswerable. All of them together are a round of work, and
the thing they were being asked to fix has a one-drag answer that ships in the
same commit as Q119.
