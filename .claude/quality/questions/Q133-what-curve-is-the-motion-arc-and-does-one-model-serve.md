# Q133 · What curve is "the motion arc", and does one model serve every motion? — **answered 2026-08-20**

Motion arcs and arc prediction (Pillar 4, Q98's follow-ups) both need a fitted
curve, and what curve it is decides what "off-arc" and "predicted position"
mean. Three candidates were prompted: a least-squares circle with a line
fallback, a parabola for ballistic motion, and a smoothing spline through the
ticks.

**Answered: the circle/line fit for this slice — and eventually all of them,
noted here so the intent survives the branch.** Classic animation arcs —
swings, pendulums, head turns — are near-circular, a degenerate fit falls back
to a line, and a low-order fit is what makes "this tick is off-arc" a
statement rather than a tautology. The owner's addition to the recommendation:
the other models are wanted too, later, not rejected. Where they land when
they come:

- **Parabola** belongs to the *Jump arc analyzer* roadmap item, which is its
  natural home — ballistic motion needs a gravity axis the circle fit never
  has to assume.
- **A model choice** (circle / parabola / more) becomes a setting on the arc
  overlay when a second model exists; until then a picker with one entry is
  furniture.
- **The smoothing spline** stays out as an *arc judgement* — a curve that hugs
  every tick can never say a drawing is off it — but may return as trail
  smoothing, which is a different promise.

The costs declined for now: the parabola reads wrong for swings and turns,
and the spline extrapolates wildly, which would make prediction worse exactly
where it is most needed.
