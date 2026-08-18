# Q117 · How the fluid solver carries density and temperature — **answered 2026-08-18: conservative flux transport**

Building step 1 of `docs/DESIGN-fluid-effects.md` turned up a defect the design
had not anticipated, and it needed a decision rather than a guess because the
choice trades sharpness against conservation and there is no renderer until
step 2 — so nobody could look at the two options and pick.

**The finding.** Scalars advected semi-Lagrangian — backtrace the flow, resample
the field, the textbook choice and the same thing the velocity still does — and
it is not conservative. It did not merely drift. Measured on a closed swirl over
a hundred steps, with dissipation off, the density came out at:

| peak speed (cells/step) | density after 100 steps |
| --- | --- |
| 0.17 | 107% |
| 0.41 | 141% |
| 0.77 | 219% |
| 1.02 | 293% |

The error compounds with flow speed. A buoyant plume at ordinary settings came
out at 158% and, once it reached the ceiling, fell to 50% — an element inventing
two thirds of its own smoke and then losing half of it, for reasons no parameter
described. **Every individual cell was a plausible number**, which is what makes
it the dangerous kind: it would have been found much later, downstream of the
contour tracer, and blamed on the emitter.

Two things it was *not*, both ruled out before asking: the transport indexing is
correct (a zero-velocity field conserves to 100.0000%, and a mirrored setup stays
symmetric to 0.0004% of peak), and it is not the CFL bound alone — holding the
velocity field inside one cell per step improved the worst case from 158% to
143% and no further.

**Answered: move density and temperature across the faces as fluxes**
(recommended, accepted). Donor-cell upwinding, with the outflow limiter that
keeps it non-negative — a flux is subtracted from one cell and added to precisely
one other, so the total cannot move at all. This is `FluidLattice`'s own argument
for its own transport restated: conservation catches indexing bugs no visual
check would. `TotalDensity` becomes an assertion instead of a diagnostic, and the
swirl above now measures 100.0000%.

Velocity keeps advecting semi-Lagrangian. The two halves are chosen for different
properties on purpose: momentum wants unconditional stability and gains nothing
from being conserved; matter wants the opposite.

**What it costs is sharpness.** First-order upwinding is diffusive — the field
smooths as it travels, and fine wisps blur sooner than they would under a
higher-order scheme. That is a real loss and it is smaller here than it would be
anywhere else, for a reason specific to this feature: **the deliverable is a
traced iso-contour, not the field**. A smoother field yields a smoother line, and
a smoother line boils less — which, given Q116 chose per-frame tracing with no
temporal coherence, converts the scheme's main weakness into help with its main
risk. Step 4 is where that gets judged against an actual picture, and if fire
reads as mushy the escape is a flux limiter on top of the same conservative
machinery rather than a return to resampling.

The alternatives and their costs, as put:

- **Resample and renormalise the total each step** — keeps the sharp wispy
  detail and is exactly conservative in the total, one extra pass. Declined
  because the conservation test would then be testing the correction rather than
  the transport, which throws away the property that makes it worth having; and
  a local error gets smeared over the whole element rather than staying where it
  happened.
- **MacCormack / BFECC** — sharpest of the three, about double the advection
  cost. Declined because it reduces the mass error roughly tenfold without
  removing it: the element still drifts, just slower, and there is still no
  assertion that says when it has gone wrong.
- **Accept it and author around it** — no code at all. Declined because the
  element's look would then depend on grid size and flow speed rather than on
  its parameters, `Dissipation` would stop meaning anything absolute, and step 2
  would build contour tracing on a field whose total is not trustworthy.
