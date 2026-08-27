# Q168 · Should the live tip be shown when it breaks live-matches-committed

Raised by: B322's fourth fix attempt, 2026-08-27.

What it blocks: any fix that draws the newest dabs raw over the processed body.

**Answered 2026-08-27 — and the answer went against the recommendation.**

## The question

B322 is that a brush with an effect shows the mark as of the last completed
post-process pass, so everything stamped since is not drawn at all. The fix is
to draw those dabs raw, over the processed body.

A raw tip is **by construction** a difference from the processed commit, and
`LiveMediumPixelTests` and `LiveMatchesCommittedTests` hold the promise that
*"a stroke looks while you draw it exactly the way it will look when you let
go"*, to within **1 part in 255 averaged over the image**.

Measured with the tip drawn:

| effect | inside the tolerance? |
| --- | --- |
| granulation | yes |
| wet edge | yes |
| footprint ceiling | yes |
| simulated medium (Watercolour, Gouache, Oil, Ink wash) | **no — 1.87/255** |
| paper texture | **no** — an untextured tip over a textured body is exactly what the test looks for |

The two promises cannot both hold while the pass lags the pen. An artist can
have a mark that is missing its tip, or a tip that is visible but not yet fully
rendered. Not both.

**Recommendation:** show the tip everywhere, and change those tests to drive the
pass to completion before comparing rather than widen their tolerance — because
`LivePaintSession`'s own design note already calls the intent *"the true mark
converging a fraction behind the tip rather than flat dabs until pen-up"*, so a
transient difference is the documented design rather than a regression; and
because raising a tolerance weakens a guard for every future change where
waiting for quiescence does not. The alternative costs B322 staying open for the
effects most likely to show it.

**Decision: keep `live matches committed` literal.** Do not show the tip for the
effects that breach it — the simulated media and paper texture. They keep
today's behaviour and B322 stays open for them; granulation, the wet edge and
the footprint ceiling would get the fix.

Recorded as the owner's call over the recommendation, because a decision quietly
written up as a conclusion is the failure this file exists to prevent.

## What happened next, and why this is not settled in practice

**The fix this was gating never worked.** It was reverted the same hour on a
performance collapse unrelated to the trade above: the tip was defined as every
dab since the last pass, which at 1263 points with 10 rendered was 99% of the
stroke, restamped per publish — invariant 6 broken, and self-amplifying, because
a bigger tip starves the worker that would have shrunk it. See B322.

So this decision **constrains the next attempt** rather than describing anything
that shipped, and its figures were taken on that attempt's build. Re-measure
them: a bounded tip composes differently from an unbounded one, and the effects
that breach the tolerance may not be the same set.
