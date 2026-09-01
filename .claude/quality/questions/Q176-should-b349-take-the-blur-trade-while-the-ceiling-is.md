# Q176 · Should B349 take the blur trade while the ceiling is redefined? — **answered 2026-09-02: no, hold out for the real fix**

Raised by: B349's afternoon of measurement, which left one working repair on
the table — a blur of the footprint ceiling, taking the ridge from 7.8/255 to
about 0.3/255 at no measurable cost — and four that fail by construction.

What it blocks: whether large soft brushes ship with visible ridges and a hard
rim until the ceiling is redefined, or with a 4% residual ripple now.

| | What it costs |
| --- | --- |
| **Hold out for the real fix (recommended, chosen)** | The artefact stays until a redefined ceiling wins on both ripple and Q157's edge tests. Cost: L, design note first. |
| Blur behind a flag, off by default | A flag and a report line; lets the owner compare on their machine at no risk to anyone's feel. |
| Take the blur now | Ridges mostly gone today; a 4% ripple stays, recorded as a trade rather than a repair, and one `FootprintCapTests` assertion widens at the centre. |

Chosen with the standard stated in the same conversation — *"this cannot be
anything else less than perfect"* — which makes a 4% artefact a 4% artefact.
The design note redefines what a *swept* soft mark's ceiling is (Q157's
premise is right at the edge and wrong in the interior), candidates are
prototyped in raster tests that measure ripple, and nothing reaches the paint
path until one is proven strictly better on every constraint.
