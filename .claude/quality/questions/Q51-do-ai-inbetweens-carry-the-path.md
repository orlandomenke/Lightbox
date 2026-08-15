# Q51 · Do AI inbetweens carry the path? — **answered: only when node counts match**

**Answered 2026-08-07: carry the path through when both keys have the same number
of nodes, plain strokes otherwise** — **against a recommendation of never**.

The recommendation was that generated frames always come out as ordinary strokes
with no path, consistent with `StrokeInterpolator` already dropping `Holes`,
`ClipId`, `GradientId` and `SwatchId`. The owner took the middle: matched counts
are the common case when one key was copied from the other and edited, and
node-level correction of frame 4 is worth having when it is honestly available.

**What it costs, stated when it was chosen: the same command produces two
different results depending on something invisible.** An artist runs *inbetween*
twice and gets editable nodes once and not the other time, with nothing on screen
explaining why.

**So the mitigation is not optional and is part of the decision.** The AI status
line says which happened *and why* — "paths carried" versus "paths not carried:
keys have 12 and 9 nodes" — the same way every bulk edit in the project window
says what it did. **A silent version of this answer is a defect, not a
simplification**, and the test asserts both messages rather than only the
behaviour.

**Blocks:** nothing.
