# Q114 · How do tilt and speed plug into the brush dynamics model? — **answered 2026-08-17: new curve inputs beside pressure, and a tilt rotation mode**

Raised by Q112/Q113 giving the record two new axes with nothing to drive.
`docs/DESIGN-pen-dynamics.md` §Render has the full shape.

Today the model is: `Curves` (or a legacy gamma) maps **pressure** through a
monotone `ResponseCurve` per target — `Size, Flow, Hardness, Scatter,
Roundness, ColorRate, SmudgeLength` — evaluated per dab in `PressureResponse`,
the one place pressure becomes a multiplier.

| | What it costs |
| --- | --- |
| **Add `SpeedCurves` and `TiltCurves` beside `Curves`**, same targets, same `ResponseCurve`, factors multiplying; plus `AngleFollowsTilt` for azimuth → dab angle (recommended, **chosen**) | Two more nullable dictionaries on `BrushSettings`, absent unless used; no migration; the curve editor, the monotone interpolation and its tests are reused as-is. |
| **Full sensor-matrix redesign** (Krita's model: any input × any target in one mechanism) | One general mechanism, bought with a migration of every stored `Curves` dictionary, a rebuilt dynamics UI, and generality that mostly pays for input combinations nobody has asked for. The chosen shape *grows into* this — a third axis is a third dictionary — rather than being replaced by it. |

**Tilt is two different things, and only one of them is a curve.** Tilt
*altitude* (how far from vertical) is a 0..1 magnitude and drives targets
through `TiltCurves` exactly as pressure does — lay a pencil over, the mark
widens and softens. Tilt *azimuth* (which way the pen leans) is a direction,
not a multiplier: it steers the dab the way `AngleFollowsDirection` already
steers it from the heading, so it lands as a sibling mode, not a curve.

**Multiplication is the combining rule**, as it already is between a curve and
the base value: each enabled input contributes a 0..1 factor and an input
nobody enabled contributes exactly 1. That keeps `IsDriven` honest per axis, so
the engine still skips all work for a brush that uses none of this.

**Determinism holds by construction:** the factors are pure arithmetic on
stored per-point values; `Hash01` seeding and the no-RNG rule (invariant 2)
are untouched. Tests must respect the saturation trap — measure width or
cross-profiles, not alpha down the stroke's middle.
