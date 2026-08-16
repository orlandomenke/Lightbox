# Q98 · What part of a moved thing snaps to a guide? — **answered 2026-08-16: its bounds, corners and centre**

Asked at the end of **B216**, which gave the guides to every tool that *places a
point* — the brush's start, a shape's corners, a gradient's axis, a pen node, a
polygon vertex, a marquee corner, a dragged path node — and deliberately stopped
there. The tools left out move a *thing* by a delta rather than placing a point:
the Move tool, the transform gizmo, and the Arrow's line drag. Snapping them
needs an answer to a question placing a point never raises, because a moved
thing has extent and a placed point does not.

| | What it costs |
| --- | --- |
| **Its bounds — edges and centre** (recommended, **chosen**) | A bounds rect per drag, computed once at the press. |
| **The grab point** | Simplest to build and to predict, and the only option that lets you land a specific *feature* on a guide — but aligning an edge then means grabbing exactly the edge. |
| **Both, nearest wins** | Most capable, least predictable: two rules compete for one drag and which one won is invisible. |

**Photoshop's Move tool snaps bounds**, so the reflex transfers — but the reason
stands without the precedent: *"line this up with the guide"* is a claim about
the edge of the artwork. Snapping the grab point would mean an artist who wants
an edge on a ruler has to take hold of that exact edge first, which is a
requirement the gesture never otherwise makes.

## The five candidates, and why not nine

Four corners and the centre. Corners are what you line up against a ruler or a
grid; the centre is what you line up against a vanishing point. Edge midpoints
were considered and left out: a corner already covers each edge on one axis, and
nine candidates competing for a single drag is how a snap starts feeling like it
is arguing with you rather than helping.

**The smallest correction wins.** Every candidate is offered to `SnappedPoint` —
B216's shared helper — so a move obeys the same guides, the same tolerance and
the same on/off switch as everything else rather than inventing a second kind of
snapping. The candidate that moves least is the one the hand was closest to
meaning.

## Two things the implementation had to get right

**The bounds are the gizmo's, not `MovingBounds`.** The obvious field was the
wrong one: `TransformSession.MovingBounds` is a repaint-region optimisation and
returns *null* whenever the moving content is not bounded by strokes alone — a
frame with a baseline, or with placements. Snapping against it would have worked
on a clean drawing and silently done nothing on a painted one, which is the
worst shape a snap can have. `SnapBounds` is the rect the artist can see the
handles around, which is the only box it makes sense to line up.

**The guides apply after the axis lock.** Shift asks for an axis out loud; the
guides work within it. Snapping first and constraining second would pull a
held move back off the axis it was held to, and the two would be unusable
together. Same order the gradient uses for the same reason (B216).

## What this did not answer

**The transform gizmo's scale and rotate handles are not snapped.** Translation
has one obvious question — which part lands on the guide — and this answers it.
A resize handle raises a different one: does the dragged corner snap, or the
opposite one that is staying put, or does the *scale factor* snap to a round
number? Rotation raises a third. Those are three more decisions and no part of
them is implied by this one, so they are left for whoever asks for them rather
than guessed at now.
