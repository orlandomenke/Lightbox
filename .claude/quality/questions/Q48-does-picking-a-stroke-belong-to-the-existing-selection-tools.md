# Q48 · Does picking a stroke belong to the existing selection tools? — **answered: a separate line-picker**

**Answered 2026-08-07: a new tool.** The black arrow picks whole strokes — click,
shift-click, drag a box — and the existing marquee, lasso and wand keep selecting
*areas of pixels*. Two tools that look different and do visibly different things.

**The rejected option is the interesting one:** folding both into one tool, so a
click picks a line and a drag on empty canvas picks an area. Fewer tools, and it
reintroduces exactly the ambiguity Q53 exists to remove — the same click meaning
two things depending on what happens to be underneath it.

**What it costs.** One genuinely new primitive: a stroke-under-point query, which
the codebase has never needed. All three pieces exist and are tested and nothing
composes them — `StrokeIndex.Intersecting`, `GeometryOps.DistToSegment`,
`BrushEngine.CommitBounds`. `StrokeIndex`'s contract is *ascending record
position, not speed*, so the picker reverses it for hit order and must say why.

**Blocks:** nothing.
