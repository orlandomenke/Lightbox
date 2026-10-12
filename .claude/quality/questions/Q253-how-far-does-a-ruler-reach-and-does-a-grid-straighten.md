# Q253 · How far does a ruler reach, and does a grid straighten strokes? — **answered 2026-10-12: a ruler only near its line, a grid never**

Raised by: the owner's report, 2026-10-12, alongside **B455** — *"The annoyance
could in part be caused by the fact that strokes follow the guide even if not even
near the guide itself."* (**B457**.)

What it blocked: B457. The behaviour was deliberate and documented: a guide line
acted as a parallel ruler, and `Snapper.Lock` took any stroke within 15° of a
guide's direction wherever on the canvas it was drawn. One horizontal ruler
therefore straightened a sixth of all strokes, and a grid — two directions — a
third. Changing that is a design change, so it was asked with `AskUserQuestion`.

| | What it costs |
| --- | --- |
| **Only near the line** (recommended) | A guide line takes a stroke only when it starts within the snap tolerance of the line. Grids, vanishing points and isometric rigs unchanged. Loses drawing parallels to a ruler at a distance. |
| **Near for lines, and grids stop straightening** (**chosen**) | As above, and a grid only places points. Loses ruled lines along grid axes without a separate ruler. |
| **A per-guide "parallel ruler" switch** | Keeps both behaviours; one more option, one more serialized key, more UI. |
| **Leave it canvas-wide** | Nothing changes but the switch being remembered (B455). |

**Decided one step past the recommendation**: the grid's direction lock goes too.

## What was not a preference

**The middle of an unheld stroke follows the hand.** Not asked, because it was
already the stated rule — the manual says a brush stroke snaps its start and not
its middle, and the lasso's test says "the same rule the brush follows" — and the
code did not do it: every point of a stroke that had not locked went through
`Snapper.Point`, so a freehand line was flattened onto a ruler for as long as it
stayed within tolerance, and collected by grid intersections. With the grid's lock
removed that would have turned every stroke on a grid into a run of intersections,
so it is fixed in the same change.

## What this did not answer

**Drawing parallels at a distance** is now out of reach for a guide line, by
decision. If it is missed, the per-guide switch in the table is the shape it would
come back in. The isometric rig still constrains everywhere, which is the nearest
thing left to a parallel ruler.
