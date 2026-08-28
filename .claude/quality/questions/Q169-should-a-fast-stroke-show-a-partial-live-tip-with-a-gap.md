# Q169 · Should a fast stroke show a partial live tip with a gap behind it

Raised by: B322's seventh attempt, 2026-08-27, once the tip's cost was finally
measured rather than estimated.

What it blocks: the only fallback B322 had left after six attempts, and
therefore whether the entry can be closed at all.

**Answered 2026-08-27 — and the answer went against the recommendation.**

## The question

B322 draws the dabs the post-process pass has not reached yet, so the tip of the
mark is under the nib instead of lagging behind it. A budget decides whether
that is affordable, and on a fast stroke with a large brush it is not:

| | dabs | cost at the measured ~45 us a dab | of a 44 ms publish cycle |
| --- | --- | --- | --- |
| outstanding, median | 268 | **12.5 ms** | 28% |
| outstanding, p90 | 2072 | **94.4 ms** | 215% |

A 3 ms budget buys 59 dabs. Six attempts treated that as a constant chosen
badly; `LiveTipDabCostTests` establishes that it is not a constant at all but an
area, and that no value of the budget covers a fast stroke at size 70.

So the tip either draws **all** of the outstanding run or **none** of it — which
is today's behaviour, and the reason the preview vanishes exactly when the pen
moves fast. The alternative is to draw the newest dabs the budget affords and
leave the rest to the pass: ink under the nib on every stroke, at the price of a
**visible hole** between that ink and the processed body.

**Recommendation:** draw the partial tip. It is the only shape that never
refuses, so the preview never disappears mid-stroke, and the owner's verdict on
the all-or-nothing version was *"To be honest this isn't optional. This is
mandatory to work."* The alternative costs B322 staying open for fast strokes
indefinitely.

**Decision: no. "A broken mark is worse than a late one."**

All-or-nothing stands. No partial tip was built, and no code was written before
this was asked — the shape of that fallback had sat in the ledger for two days
as the thing to do next, and it is ruled out on the one ground no measurement
could have supplied.

## What this leaves

The cost of a dab, which is the only quantity in the table above that is not
fixed. See **Q170**: stamping the tip at the resolution it is displayed rather
than at the document's is a measured **4.2x**, which puts the median outstanding
run inside the existing 3 ms budget. It changes the area rather than rationing
the dabs, so it satisfies this decision instead of working around it.
