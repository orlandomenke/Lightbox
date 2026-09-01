# Q175 · What does "any brush with any effect" mean for the brush model? — **answered 2026-09-02: Krita parity, exclusive engines with deeper options**

Raised by: the owner, 2026-09-02, setting the bar for all brush work — *"brushes
and their effects need to be interchangeable. At least a user should be able to
change any brush to any effect and vice versa … the extensibility of Krita's
brush engine and its flexibility"* — alongside *"this cannot be anything else
less than perfect"* on responsiveness.

What it blocks: the shape of every brush-engine change from here on, including
where B349's redefinition of the footprint ceiling lives.

## Where the model stands

One record, `BrushSettings`, already carries tip, dynamics and most effects as
toggles any preset can enable together: scatter, jitters, texture, wet edge,
granulation, pressure/tilt/speed curves. Two things are exclusive: **`Kind`**
(paint, smudge, blur — one at a time) and **`Medium`** (one at a time, and not
on a smudge or blur, which take the copy-based path and have no post-process).
That is where "any brush with any effect" stops today.

Krita's engines are exclusive too — a preset has one paint op — and it earns
its reputation for flexibility from the depth of each engine's options and the
sensors/curves shared across all of them, not from stacking engines.

| | What it costs |
| --- | --- |
| Composable stages: any brush carries a medium *and* a smudge/blur op, in a fixed ordered pipeline (recommended) | L. More interchangeable than Krita; invariant 2 holds because each stage is seeded from geometry. B349's ceiling becomes a named stage. |
| **Krita parity: exclusive engines, deeper options and dynamics per engine (chosen)** | Each gap is S–M on its own: tilt input, velocity dynamics, bristle variants, per-device pressure calibration (the gap analysis's raster list). Less interchangeable than the words asked for; honest if "flexibility" meant richness. |
| User-authored engines (node graph or script) | XL, and the only option where determinism is at risk — user code has to be barred from randomness and clocks. Needs its own question before any estimate. |

**Recommendation was the first row.** The owner chose the second, and the
reasoning worth keeping is that it matches the reference apps named in the
same breath: none of Photoshop, Krita or Clip Studio lets a smudge carry a
medium either. What "interchangeable" therefore means here is **every tip,
every dynamic and every non-exclusive effect on every engine** — which the
record already delivers — plus the option depth the gap analysis lists.

## What follows from it

- The roadmap's brush-engine work is the gap analysis's raster list, taken as
  its own items, not a pipeline refactor.
- B349's ceiling redefinition stays inside `NeedsFootprintCap`'s domain — the
  soft-falloff paint engine — rather than becoming a stage every engine must
  survive.
- Kind and Medium stay exclusive, and the picker should say so rather than
  letting a smudge preset carry a medium it silently ignores (a small,
  separate item).
