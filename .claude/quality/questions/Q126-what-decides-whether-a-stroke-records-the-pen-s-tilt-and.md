# Q126 · What decides whether a stroke records the pen's tilt and speed — **answered 2026-08-18: the brush, with a preference that can override it**

Raised by the owner reading phase 1 back: *"Shouldn't tilt and speed be brush
dependent instead of global?"* — against a shipped `RecordPenAxes` preference
that was one blunt on/off for the whole application.

**Half of the objection was already satisfied and the other half was right.**
Two separate things had been run together:

| | Where it lives |
| --- | --- |
| **Response** — how strongly a mark reacts to tilt or speed | Always the brush's. `SpeedCurves`, `TiltCurves` and `AngleFollowsTilt` sit on `BrushSettings`, which every stroke carries a snapshot of, exactly as pressure response does. Never was global. |
| **Capture** — whether the numbers enter the record at all | Was the global preference. This is what the question is about. |

## The measurement

Taken before arguing, on a 400-point stroke — a few seconds of drawing:

| | no axes | with axes | per point | document |
| --- | --- | --- | --- | --- |
| saved file (indented) | 64,863 B | 110,063 B | +113 B | **×1.70** |
| compact (snapshots, AI wire) | 17,831 B | 34,231 B | +41 B | **×1.92** |

Recording axes nobody reads nearly doubles the record. That settles it against
"just always record": this is an application whose unit of work is two hundred
drawings, and the charter's trade — never make one drawing nicer at the expense
of handling a sequence — points straight at the brush deciding.

## The asymmetry that stops the brush deciding *alone*

> Brush settings stay editable forever. The hand's motion happened once.

A stroke carries its own `BrushSettings` snapshot, so it can be retuned months
after it was drawn — but if the brush in hand at capture time read no tilt,
there is no tilt to retune against, and no later edit can recover it. Worse, it
fails **silently**: absent axes contribute the neutral value by design, so
adding a tilt curve to such a stroke changes nothing at all, with no error and
no explanation. A reversible choice would be quietly destroying irreversible
data.

| | What it costs |
| --- | --- |
| **Brush decides, per axis, with an `AlwaysRecordPenAxes` preference as override** (recommended, **chosen**) | One preference to explain, and its meaning has to be got right in the UI — it is "keep these for later", not "turn the feature on". In exchange the default needs no configuration at all and the cliff above is escapable. |
| **Brush decides, no preference** | Simplest, and nothing to misunderstand. Accepts permanently that a stroke drawn with a plain brush can never be given tilt response without redrawing it. |
| **Keep the global preference** | What phase 1 shipped. Data always there when on — and pays ×1.70 on every document for brushes that read none of it, which is the waste the owner objected to. |

## What was decided

- **Per axis, not all-or-nothing.** A brush with only a speed curve records
  speed and no tilt. `PenAxisUse.NeedsTilt` / `NeedsSpeed` is the one place that
  is answered, so capture and render cannot drift apart.
- **`AngleFollowsTilt` counts as needing tilt** even though it drives no curve:
  azimuth is a direction rather than a multiplier, and it still cannot work
  without the stored pair.
- **Decided once, at `StrokeBuilder.Begin`.** Switching brushes mid-drag is a
  real gesture, and half a stroke carrying tilt is worse than none — a curve
  would read the recorded half and the neutral default for the rest, and the
  mark would step.
- **The control measures unconditionally and the builder filters.** Reading two
  properties off a pointer point is free; storing them is the 113 bytes. It also
  means the diagnostics readout can answer "does my tablet report tilt at all"
  whatever brush is in hand, which is the question an artist actually arrives
  with.
- **`AngleFollowsTilt` is `bool?`, not `bool`.** The serializer omits nulls and
  nothing else, so a plain flag writes `"angleFollowsTilt": false` into the
  brush block of every stroke of every document. `AngleFollowsDirection`
  predates that rule and is the reason for it rather than a licence to repeat
  it.

**What this costs, stated plainly:** the default is now silent about a real
trade. An artist drawing with a plain brush records nothing, which is right, and
will not discover that until they try to add a curve later and nothing happens.
The Configure page says so in as many words; whether that is enough is a
question for the first person who hits it, not one to guess at now.

**Phase 2 is unaffected in shape and gains its contract early.** The three
`BrushSettings` fields land here, unused by the renderer, because the capture
gate has to have something to read. Wiring them to the dab walk is still phase
2's job.
