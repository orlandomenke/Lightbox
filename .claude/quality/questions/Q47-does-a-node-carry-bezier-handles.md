# Q47 · Does a node carry Bezier handles? — **answered: yes, on a path beside the points**

**Answered 2026-08-07: handles on every node** — full Illustrator levers,
**against a recommendation of points-only**.

The recommendation was the Curvature-tool model: place points, let the
centripetal Catmull–Rom the renderer already runs infer the curve, Alt for a
corner. It is free — `GeometryOps.Densify` already does the interpolation and
`IsCorner` already exists — and Adobe shipped that tool precisely because the
handle pen is too hard. The owner chose handles anyway, for control and for
transferable muscle memory.

**The cost quoted at the time was the wrong cost, and saying so matters.** The
objection was that `StrokePoint(X, Y, Pressure)` is baked into the record, the AI
wire format, the contour tracer and every geometry op, so handles meant widening
it — a migration and a second curve type in the renderer. **That is avoidable,
because a drawn stroke and an authored path are different things.** A drawn
stroke has hundreds of sampled points and wants no handles; a pen path has a
dozen authored nodes and wants nothing else. So handles go on an **optional
`Stroke.Path`** — a small control net that *generated* the points — and `Points`
stays what renders. `BrushEngine`, `StrokeIndex`, `ContourTracer` and
`StrokeWire` are untouched, a hand-drawn stroke writes no `path` key, and there
is no migration.

**The residual cost is real and is the thing to hold:** a line now has two
representations that can disagree.

> **A stroke's `Path` and `Points` must never disagree.** Any operation that maps
> points maps the path's nodes and handles too, or drops the path.

`TransformOps.TransformStroke` is the first caller that must obey it and
`StrokeInterpolator` the second, and a test asserts it rather than a comment.

**Blocks:** nothing.
