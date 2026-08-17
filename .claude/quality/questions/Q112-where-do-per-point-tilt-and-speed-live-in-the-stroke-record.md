# Q112 · Where do per-point tilt and speed live in the stroke record? — **answered 2026-08-17: widen `StrokePoint` with nullable fields**

Raised by the roadmap's *Tilt & velocity recording* item, whose one named
blocker was "StrokePoint migration". `docs/DESIGN-pen-dynamics.md` carries the
design this decision unblocks.

`StrokePoint` is `(X, Y, Pressure)` and its own doc comment promised this day
would come: *"Pressure is part of the model from day one so tablet support is a
data-source change, not a model change."* Tilt and speed are the rest of that
sentence.

| | What it costs |
| --- | --- |
| **Widen `StrokePoint`** — nullable `TiltX`, `TiltY`, `Speed`, omitted from JSON when null (recommended, **chosen**) | ~3 extra camelCase keys per point, only on pen strokes that recorded them; gzip (Q65's container) absorbs most of it. Old files load unchanged — a missing key is null. |
| **Parallel optional arrays on `Stroke`** | Smaller JSON (flat arrays), bought with an alignment invariant against `Points` in every operation that edits points — the `RestPoints` count-mismatch trap, multiplied across transforms, path re-flattening, the record cleaner and the inbetweener. |
| **Do not persist; recompute at render** | Not viable at all: speed cannot be recomputed without timestamps, so a reload would render a different image — invariants 1 and 2 both fail. Listed because it is the shape a "keep the file small" argument would reach for. |

**Why widening wins over the vector precedent.** Stroke path reshaping
deliberately avoided widening `StrokePoint`, putting handles on an optional
`Stroke.Path` instead — but a path is *structure over* the points, while tilt
and speed are *properties of* each sample, exactly like pressure. A value that
exists per sample belongs in the sample; a copy of `Points` is a copy of the
axes with no bookkeeping to forget.

**The optional-has-two-halves guard ships in the same commit as the fields:**
a mouse stroke, and any document saved before this change, must serialize
byte-identically — `Assert.DoesNotContain("\"tiltX\"", json)` and friends,
per *CLAUDE.md → "Optional" has two halves*.
