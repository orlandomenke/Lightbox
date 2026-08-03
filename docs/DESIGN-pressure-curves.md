# Pressure curves — a shape, not an exponent

## What was there, and why it was not enough

Three numbers: `PressureSizeGamma`, `PressureFlowGamma`,
`PressureHardnessGamma`, each an exponent in `p^γ`. Cheap, one control each,
and the tool bar called the slider "Response curve", which is the tell.

`p^γ` is monotone for every γ. So the whole family it can reach is "gentler" and
"fiercer" — and there is no member of it that is thin at a feather touch, thick
in the middle and thin again when leaned on. That is not an exotic request; it
is what an ink brush that spreads and then floods actually does, and it is the
first thing an artist tries to build.

A drawn curve reaches it, and reaches everything the exponents did.

## The interpolant, and why not the obvious one

**Monotone cubic, Fritsch–Carlson.** The obvious choices both fail on a
requirement that is easy to miss until it produces a bug report:

| | What goes wrong |
| --- | --- |
| Straight segments | Visible corners at every handle. A response that changes slope in one step reads as the pen snagging. |
| Catmull–Rom / natural cubic | **Overshoots.** A curve whose handles all sit inside 0..1 evaluates past 1 between them. |

The overshoot is the disqualifying one. For size it is a dab wider than the
brush; for flow it is paint darker than the colour that was picked; and for
either it happens at pressures the artist never placed a handle at, so it looks
like the engine is broken rather than the curve.

Fritsch–Carlson is the same Hermite spline with two extra rules — zero tangent
where the neighbouring secants disagree in sign, and a cap at three times the
smaller of them — and those rules are exactly what makes the interpolant stay
between its own handles.

**They also set a floor on how sharply it can turn.** `FromGamma` reproduces
`p^0.5` to about 2%, not to a byte of alpha, and no number of handles fixes
that: 16 instead of 13 moves it by 0.003. This is the right trade rather than a
shortfall, and the reason is that `FromGamma` is only ever a *shape to start
editing from* — the render path still evaluates the exponent itself until a
curve exists.

Sampling for it goes along whichever axis the exponent is well-behaved in:
evenly in pressure above γ = 1, evenly in output below it, where the slope at
zero is infinite and even pressure steps leave the whole knee to the
interpolator.

## The gamma is the curve's default shape, not a rival

Every file, preset and imported `.abr` in existence is written in gammas.
Three options, and only one of them is safe:

1. *Convert on load.* Silently repaints art that is already finished, because
   the conversion is 2% off. No.
2. *Two mechanisms at render time.* Two code paths that must agree forever.
3. **A target with no curve falls back to its own gamma.** One evaluation path
   — `PressureResponse.Factor` — nothing to migrate, and old art renders
   byte-identically because it takes the same arithmetic it always did.

The editor hides the difference: it shows `PressureResponse.Shape`, which is the
artist's curve or the one the gamma describes, so a brush made before curves
existed opens on its real response rather than on a straight line that would
flatten it at the first drag.

## What pressure is allowed to drive

Seven targets, and the list is a judgement rather than an inventory.

| Target | Why |
| --- | --- |
| Size, Flow, Hardness | The three that already had exponents |
| Scatter | Press harder, throw wider |
| Roundness | A flat brush pressed down spreads toward circular |
| Colour rate | A smudge that adds more of its own colour when leaned on |
| Smudge length | …and drags what it picked up further |

Two deliberate absences:

- **Opacity.** It is a *stroke-level* cap that overlapping dabs never exceed, so
  "opacity follows pressure" would have to mean something different at every dab
  and could not mean anything at the stroke. Flow is the per-dab control and is
  what an artist means by transparency.
- **Rotation.** No pen gesture makes "press harder, turn the nib" a thing
  anybody does. Rotation follows the stroke's direction or it jitters; both
  exist.

### Scatter keeps its direction

Pressure scales how far scatter throws, never which way. The angle stays the
position hash's, so a harder press spreads the same pattern rather than picking
a new one — dabs move outward instead of jumping about. That is invariant 2
seen from the artist's side: **visual variation is wanted, logical randomness is
forbidden**, and a scatter that reshuffled under pressure would boil at 12 fps.

## The control

Draws and reports; it does not decide. Every edit leaves as a whole new
`ResponseCurve` on `CurveChanged`, so the change lands through the view model
with the rest of the brush rather than mutating a record the view model believes
it owns — which is also what lets undo see it.

The curve is drawn by **sampling the same evaluator the engine uses**, one
sample per pixel, rather than by converting the handles into Avalonia's bezier
primitives. A second implementation of the interpolant is a second thing to keep
in step, and the failure mode — an editor that shows a shape the brush does not
paint — is the worst one available here.

The ends keep their pressure: dragging the handle at full pressure sideways
would leave the curve saying nothing about the top of the pen's range, where it
is then flat, and the brush would read as having stopped responding rather than
as having been edited.

## Not in scope

Curves driven by anything other than pressure. Tilt and speed are the obvious
candidates and neither is in the record yet — `StrokePoint` is `(X, Y,
Pressure)` and nothing stores time. When they arrive, the shape of this is
already right: `BrushDynamic` names the target, and the input would become a
second key rather than a second mechanism.
