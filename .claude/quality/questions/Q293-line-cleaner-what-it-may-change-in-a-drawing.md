# Q293 · Line cleaner: what may it change in a drawing? — **answered 2026-11-19: reshape, plus remove specks**

Raised by: the same request as Q292. "Not drastic" needed a definition the code
can hold to.

What it blocks: which operations the cleaner is allowed to propose.

| | What it costs |
| --- | --- |
| **Reshape, plus remove specks** (recommended, **chosen**) | Smooth wobble and small kinks, trim overshoots, close small gaps at junctions, delete tiny orphan marks. Never merges or adds strokes; a doubled sketch pass stays doubled. |
| Also merge doubled passes | Fixes converging double lines, and on a sketch is a visible change of character. |
| Reshape only | Safest; leaves specks and double lines alone. |

The bound that makes it "not drastic": no point moves further than about one
line width (the stroke's own brush size), so shape, pressure and brush survive.
Merging doubled passes is deliberately out — if it comes back it is its own
question.
