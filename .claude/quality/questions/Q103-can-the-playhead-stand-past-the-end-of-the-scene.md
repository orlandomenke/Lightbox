# Q103 · Can the playhead stand past the end of the scene? — **answered 2026-08-16: yes when scrubbing, no when playing**

Raised by the owner: *"I am unable to drag the playhead passed the hatched
cells. Thus always have to add a keyframe, or a hold before I can animate
bones. Secondly Let's say I have one cell with an animation and I want to hold
that frame for 10 frames. I want to be able to drag the play head there or add
a clear cell (hold cell) directly. Playhead is preferred, so that it auto add a
keyframe on changes. Though then we should have 2 paths: while not playing, the
playhead can be dragged to all cells. While playing, the playhead can only play
up to the hatched or if specified start- -> end-time."*

**The diagnosis the answer rests on: the scene's length was a gate you had to
open before working, when it should be a consequence of where you worked.** On
paper you write on row forty and the sheet is forty long — you do not declare
the length and then fill it. The gate was one line, `ClampCurrentFrame` pinning
the playhead to `Scene.FrameCount - 1`.

**Answered: the two paths as proposed**, and half of it already existed.
Playback runs to `EffectiveEndFrame`, which already clamps to the scene and to
the playback range, so nothing there needed changing — only the scrub side
moves.

**The refinement the owner's own wording already carried — "auto add a keyframe
*on changes*" — and it is the load-bearing part.** Dragging the playhead must
not lengthen the document:

- scrubbing past the end authors nothing and leaves no undo entry;
- the first *edit* there grows the scene to that frame, in its own step.

That is the line B206 and B207 drew between picking and editing, and it is what
stops a mis-drag to frame 500 from silently making a 500-frame document. The
gap fills itself: an unkeyed cel already *means* hold, so a drawing made at
frame twenty holds drawing five across the gap without anything filling
anything in.

## What the canvas shows out there

| | What it costs |
| --- | --- |
| **Empty, with onion and the rig still showing** (recommended, **chosen**) | Nothing, beyond one comparison in the pass builder. |
| Show the last drawing, held | Past-the-end becomes indistinguishable from a hold on the canvas, undoing the distinction Q89's hatching draws. |
| Fully empty, onion off too | Unambiguous, and makes the region useless to work in — you would pose a rig with no reference to what came before. |

`ExposureSheet.ExposedFrame` walks back from the last cel, so the naive answer —
asking it — is the rejected middle row. The ghosts and the armature still
resolve through the exposure, which is right and is left alone: an armature is
continuous, so posing the next frame needs to see the last pose.

## How far out

| | What it costs |
| --- | --- |
| **As far as the X-sheet draws** (recommended, **chosen**) | Nothing; `TimelineExtent` already exists and scrolling extends the reach. |
| A fixed lookahead | A number nobody can see, wrong for somebody's scene length. |
| Effectively unbounded | Invites a mis-drag to frame 4000 — free until you draw there, and then a very long document. |

## What the change opened, and had to close in the same breath

`Anchors.SetAcross` and `CollisionShapes.SetAcross` **clamped** their index to
the last cel. Unreachable while the playhead was pinned inside the scene — and
the moment it could stand outside, posing a bone out there would have written
the pose onto the **last drawing**, silently, on a frame the artist is not
looking at. That is B206's exact shape, and this feature would have *introduced*
it rather than found it. They refuse now instead of clamping, so any path not
explicitly grown fails safe rather than corrupting an earlier frame.

## A note on this question's own id

Filed as Q90 and renumbered to Q103 on merge: `main` took Q90 for *how a drawing
knows which bone moves it* while this branch was open. The branch predates the
move to one-file-per-question (Q91/Q92), so it raised its question by appending
to `QUESTIONS.md` and collided on both the id and the file — which is precisely
the failure that restructure was built to end, observed one last time from the
far side of it.
