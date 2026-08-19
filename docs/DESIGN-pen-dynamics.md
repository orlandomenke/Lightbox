# Pen dynamics: tilt and speed

Status: **designed, not built** — decisions taken 2026-08-17 (Q112–Q114; Q115
for the neighbouring Huion work), implementation phased below. This is the
design for the roadmap's *Tilt & velocity recording* item, whose one named
blocker — "StrokePoint migration" — Q112 resolves.

The one-sentence version: **tilt and speed become two more per-point axes in
the stroke record, captured once, stored forever, and driven through the same
response-curve machinery pressure already uses.** Nothing about how a mark is
reproduced changes; there is simply more of the hand in the record.

## What exists, and what this rides on

- `StrokePoint(X, Y, Pressure)` is the sample; its doc comment has promised
  since day one that tablet support is *"a data-source change, not a model
  change"*.
- `PressureResponse` is the one place pressure becomes a multiplier: an
  artist-editable monotone `ResponseCurve` (or legacy gamma) per target —
  `Size, Flow, Hardness, Scatter, Roundness, ColorRate, SmudgeLength` —
  evaluated per dab inside the walk.
- Rotation already has two sources: `TipRotationDeg` (fixed) and
  `AngleFollowsDirection` (the walk's heading), plus seeded `RotationJitter`.
- Avalonia 12.1.1 delivers `XTilt`, `YTilt`, `Twist` (degrees) and `Pressure`
  per pointer point. **Checked against the shipped assembly, not the docs:**
  coalesced points from `GetIntermediatePoints` carry *no timestamps* — only
  the event does. That fact shapes the speed design below.

## The record (Q112)

`StrokePoint` widens with three nullable fields, absent from JSON when null:

```csharp
public readonly record struct StrokePoint(
    double X, double Y, double Pressure,
    double? TiltX = null, double? TiltY = null, double? Speed = null);
```

- **Old files load unchanged** — a missing key is null. A mouse stroke, and any
  document that predates this, serializes byte-identically; the
  `Assert.DoesNotContain("\"tiltX\"", json)` guard family ships in the same
  commit as the fields (*CLAUDE.md → "Optional" has two halves*).
- Tilt is stored as delivered — degrees, −90..90 per axis, rounded to one
  decimal so the JSON does not carry float noise. Altitude and azimuth are
  *derived* at render (`atan2`/magnitude on the stored pair), not stored: two
  numbers, one convention, no chance for the pair and its derivation to
  disagree in the file.
- Parallel arrays on `Stroke` were rejected for the alignment invariant they
  would put on every operation that edits `Points` (the `RestPoints`
  count-mismatch trap, multiplied); recompute-at-render was never viable
  (invariants 1–2: speed cannot be recomputed without timestamps, so a reload
  would draw a different image). Q112 has the table.

**Everything that maps points maps these too, or deliberately drops them.**
The checklist, walked at implementation time: transforms (values are
position-independent — carried), the stabilizer (smooths positions — axes
carried through resampling by interpolation), `StrokeRecordCleaner`,
`CurveFitter`/path re-flattening (a reshaped stroke keeps pressure today; tilt
and speed are dropped on conversion to a path, recorded as such in the manual —
reshaping is authoring, and the pen is no longer the author), the deterministic
inbetweener (interpolates them like pressure), and `Stroke.Clone`
(`MemberwiseClone` + list copy — free).

## Capture (Q113, gated by Q126)

**What decides whether any of this is stored: the brush, per axis** (Q126). A
brush with `TiltCurves` or `AngleFollowsTilt` records tilt; one with
`SpeedCurves` records speed; a plain brush records neither and its documents
are byte-identical to what they would have been. `PenAxisUse` answers that in
one place, `StrokeBuilder.Begin` asks it once per stroke, and the
`AlwaysRecordPenAxes` preference overrides it for the artist who wants the
numbers kept against a later change of mind. Measured stakes: all three axes
cost 113 bytes a point and take a saved document to 1.70× its size.

Note the split this implies — **the control measures unconditionally and the
builder filters**. Reading `XTilt`/`YTilt` and running an EMA is free at 200 Hz;
writing them into every point is the 113 bytes. It also keeps the diagnostics
readout honest whatever brush is in hand.

In `CanvasControl`'s pointer path, beside `PressureOf`:

- **Tilt** is read off `PointerPoint.Properties` per (intermediate) point. A
  device that reports none gives `0,0` — indistinguishable from a vertical
  pen, so tilt is recorded only once a stroke has seen a nonzero value, the
  same shape as `_penHasReportedPressure`. Mouse strokes never record it.
- **Speed** is computed at capture, not stored raw: screen-space velocity from
  event timestamps and positions (per-point timestamps do not exist —
  verified, above), smoothed with an exponential moving average, normalized
  against a reference speed, clamped 0..1, and written into each point.
  **The stored value is the truth forever** (Q113): reload, undo and
  inbetweens replay identically, and a later, better estimator changes only
  new strokes — invariant 4's rule applied to time. Starting constants to
  tune by hand: reference ≈ 1.5 px/ms at 1.0, EMA half-life ≈ 30 ms.
- `PointerSample` gains the same optional axes to carry them from control to
  view model to stroke.
- The pen-settings diagnostics readout ("Pen detected — pressure 0.87") grows
  tilt and speed, so whether the driver delivers them is visible in one hover.

## Render (Q114)

Two more nullable curve dictionaries on `BrushSettings`, absent unless used,
plus one rotation mode:

```csharp
public Dictionary<BrushDynamic, ResponseCurve>? SpeedCurves { get; set; }
public Dictionary<BrushDynamic, ResponseCurve>? TiltCurves { get; set; }  // input: altitude, 0=flat 1=vertical... inverted to taste in the curve
public bool? AngleFollowsTilt { get; set; }                                // azimuth steers the dab
```

**These three landed in phase 1, unused by the renderer**, because Q126 made
them the capture gate: a brush cannot be asked what it needs until it can say
so. Phase 2 wires them to the dab walk and nothing about their shape changes.

`AngleFollowsTilt` is `bool?` rather than `bool`, unlike its neighbour
`AngleFollowsDirection`: the serializer omits nulls and nothing else, so a plain
flag writes `"angleFollowsTilt": false` into the brush block of every stroke of
every document. That neighbour predates the rule and is the reason for it.

- **Tilt is two things and only one is a curve.** Altitude (lean magnitude,
  0..1) drives targets through `TiltCurves` exactly as pressure does — lay a
  pencil over, the mark widens and softens. Azimuth (lean direction) is a
  direction, not a multiplier: `AngleFollowsTilt` steers the dab the way
  `AngleFollowsDirection` already does from the heading. Twist (barrel
  rotation) is out of scope until a device that reports it shows up.
- **Factors multiply**, as curve-times-base already does: each enabled input
  contributes 0..1, an input nobody enabled contributes exactly 1, and
  `IsDriven` stays honest per axis so the engine skips all work for a brush
  using none of this.
- The dab walk interpolates tilt and speed between stroke points alongside
  pressure; `Dab` carries them to `StampDab`. A point with null axes
  contributes the neutral value (vertical pen, zero speed) — a mouse stroke
  through a speed-driven brush renders as if drawn slowly, which is the
  predictable reading.
- **Determinism is untouched by construction:** pure arithmetic on stored
  values, no clock anywhere in the render path, `Hash01` seeding exactly as
  is (invariant 2). Speed-driven size interacts with dab spacing (`StepAt`);
  spacing follows the *final* radius the same way it does for pressure, so
  there is no new mechanism to get wrong.
- **The full matrix redesign was rejected, not deferred by accident** (Q114):
  this shape grows into it — a third axis is a third dictionary — rather than
  being replaced by it.

## Surfaces (the land-the-places-it-shows-up list)

| Surface | What lands there |
| --- | --- |
| Brush dynamics UI | The curve editor gains an input dimension (pressure / tilt / speed tabs per target); costed options badge via `BrushCostOf` if any of this turns out expensive (it should not — it is arithmetic per dab) |
| Preset record | The two dictionaries and the toggle ride `BrushSettings`, so they survive save and reuse; `.abr`/`.kpp` importers map the source formats' tilt/velocity dynamics where they exist |
| Configure window, pen page | Diagnostics readout shows live tilt/speed beside pressure |
| Manual | `docs/manual/` brush-dynamics section, same commit as each phase |
| Deterministic inbetweener | Interpolates the new axes between matched points like pressure |
| MCP / AI wire format | **Deliberately unchanged.** Points are ~95% of a request's tokens (`docs/DESIGN-ai-payload.md`); Q18 chose flat arrays to survive exactly this pressure, and widening every point is a payload decision for the ai-engineer/art-director pair under gate G12 — its own question, raised when the inbetweener work actually wants the axes on the wire. Until then generated strokes carry null axes and render at neutral, which is correct and cheap. |

## Testing, with the saturation trap in view

Per-axis tests must measure **below saturation or across the stroke** —
stroke width, cross-profiles, or the ratio between two places on one stroke —
never alpha down the middle (*CLAUDE.md → Measuring a brush*). Every
comparison prints both numbers, and every "faint is fainter" assertion also
asserts the faint mark exists. The serialization guards (absent-unless-used,
old-file byte-identity) are phase 1's definition of done, not an afterthought.

## Phasing — each phase one branch, in sequence

1. **Record + capture + diagnostics** (`feature/brush/pen-tilt-speed-record`):
   widen `StrokePoint` and `PointerSample`, capture tilt and speed, carry
   through stabilizer and cleaner, serialization guards, diagnostics readout.
   Invisible to rendering; ships alone so the record is proven before anything
   reads it.
2. **Render dynamics** (`feature/brush/pen-tilt-speed-dynamics`):
   `SpeedCurves`, `TiltCurves`, `AngleFollowsTilt`, dab-walk interpolation,
   engine tests. G12 does not trigger (no AI surface moves), leak-hunter and
   perf-warden do (brush engine).
3. **UI + presets + importers** (`feature/brush/pen-tilt-speed-ui`): the
   input dimension in the curve editor, preset round-trip, importer mapping,
   manual.

The neighbouring Huion work (B126/B254 input trace, Q115) shares phase 1's
pointer-path familiarity but is **its own branch with its own objective**
(`fix/canvas/B126-hover-input-trace`) — one objective per branch, and a trace
the owner can run does not wait on a record migration.

**That trace is now built, and phase 1 inherits two things from it.**
`Services/InputTrace.cs` already reads `XTilt`/`YTilt` off every traced pointer
event and reports, per device, whether tilt arrived at all — so the first
question phase 1 would otherwise have to ask ("does this pen report tilt on this
machine?") is answered by a diagnostic that already exists, before a line of the
record migration is written. Its `moves/s` counter answers the second one: the
rate the device actually delivers at, which is what the speed estimator's
smoothing constant has to be chosen against. Read a trace from the target
machine before tuning the constants this document leaves open.
