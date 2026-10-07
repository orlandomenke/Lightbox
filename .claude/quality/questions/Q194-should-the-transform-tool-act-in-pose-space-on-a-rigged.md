# Q194 · Should the Transform tool act in pose space on a rigged drawing? — **answered 2026-10-06: yes — transform what is on screen, write it back to rest**

Raised by the owner's report of three transform faults on a rigged character
(B381): the preview showed the old pose, confirming kinked straight lines, and
parts of marqueed strokes vanished. All three had one cause — the record keeps
a rigged drawing at rest, the canvas shows it posed, and the Transform tool
read and wrote the record. Which space a transform *should* act in is a
decision, not a bug, so it was asked.

What it blocks: B381, and B382 beside it (a stroke drawn on a posed layer
jumping away on commit), which turned out to need the same inverse.

| | What it costs |
| --- | --- |
| **Transform in pose space, write back to rest** (recommended, **chosen**) | Exact for every point: linear-blend skinning is affine per point, so the inverse is a 2×2 solve. The authored path is dropped on bound strokes, and a band scale inserts no divider vertices on a posed drawing. |
| Bake the pose first, then transform | Exact, but the frame leaves the rig: it stops following later pose changes, which is the opposite of what an artist adjusting a pose wants. |
| Keep the rest-space transform, fix only the preview | Cheapest. Honest — what you see is what moves — but you transform a pose you are not looking at, and the kinks and lost parts stay by design. |

**Recommendation:** pose space, because it is the only option where the thing
the artist dragged is the thing that moves *and* the drawing stays rigged. The
expected objection — that blended weights make the inverse approximate — did
not survive the arithmetic: posed = A·rest + b per point, with A read off
`Blend` itself, so the write-back is exact wherever A is invertible, and the one
singular case (two bones 180° apart at equal weight) leaves the point where the
artist put it.

## What was decided alongside it, and why

- **The pose is the playhead's.** A frame in a multi-frame scope that is not
  on screen is transformed as it would stand at the playhead's pose. There is
  no other picture of it to mean, and a rule per cel would make the same
  gesture mean different things across a scope.
- **Q121 stands.** Carrying the armature with the transform was declined in
  2026-08 and nothing here reopens it: the skeleton stays where it is, the
  drawing moves relative to it, and the pose-mode carry remains the way to move
  a character.
- **Drawn strokes go the same way (B382).** A layer bound to one bone keeps
  reading its binding and writes no weights; a layer bound to the whole
  skeleton weights the new stroke by where the bones stand and keeps those
  weights, because the render's own auto-bind reads rest geometry and would
  choose differently. The transform does the same for a weightless stroke it
  moves on such a layer. Those are the only cases the record gains weights the
  artist did not paint — and the cost, recorded in the manual: if the layer is
  later taken off the rig, strokes carrying weights keep following it while
  the rest stop, so the drawing comes apart until they are unbound too.
- **Where the inverse cannot be trusted, the point stays put.** A joint folded
  past about 168° at equal weight, or a map that sends a point nowhere finite,
  leaves that point at rest rather than writing a NaN or a hundredfold throw
  into the record. The pinch the picture already shows is the honest outcome.
- **Authored paths are dropped** on every bound stroke a transform moves and on
  every drawn stroke carried back to rest — the pen tool's handles included.
  The points are exact; a later fit brings handles back.
- **Sparse strokes gain points.** The inverse is exact at a control point and
  only approximate on the curve between two of them when the weights differ
  at each end, so a stroke like that is densified to a six-pixel chord before
  the write-back, and a stroke drawn on a whole-skeleton layer before its
  weights are measured. Pen strokes are denser than that already; a shape's
  corners and a hand-placed three-point line are what grow, and the growth is
  what keeps a straight line straight across a joint.
