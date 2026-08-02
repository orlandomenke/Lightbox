# Quality charter

What "good" means here, concretely enough that an agent can check it and a
reviewer can disagree with it. The `/improve` loop treats section 1 as gates
(a round is rejected if any fails) and section 2 as the backlog it works from.

## 1. Gates — every round must leave these true

| # | Gate | How it is checked |
| --- | --- | --- |
| G1 | Solution builds with no errors | `dotnet build Lightbox.sln` |
| G2 | Every test passes | `dotnet test` |
| G3 | No promise disappeared | every entry in `.claude/codemap/FEATURES.md` from the previous round still has a passing test, or the removal is justified in `LOOP.md` |
| G4 | Performance budgets hold | the `Category=Performance` tests pass unweakened; a budget may only be raised with a measurement and a reason |
| G5 | Determinism intact | no new randomness in a render path; settings that affect pixels are stored per stroke |
| G6 | Claims are verified, not asserted | anything reported as fixed has a test that fails without the fix, or an explicit note that it could not be tested and why |
| G7 | No new performance leak in the diff | **leak-hunter** reviews the round's diff against the known-wrong shapes and returns CLEAR, or the finding is fixed or explicitly accepted with a measurement |
| G8 | The roadmap matches the code | `python3 scripts/roadmap.py check` passes — a landed feature has its evidence anchors in the same commit |
| G9 | The UI did not drift | **ui-critic** reviews any diff touching XAML, a docker, a dialog or a row template against `DESIGN.md` and returns CLEAN, or the finding is fixed. A BLOCKING verdict — a control or docker that cannot be used — always fails the gate |
| G10 | The loop learned | if the round fixed something the gates should have caught, the gate is sharpened in the same commit and named on the round's `Sharpened:` line |
| G11 | The bug ledger is honest | `python3 scripts/bugs.py check` passes. A bug fixed this round is closed in `BUGS.md` in the same commit, with its regression test as the evidence — a bug is never closed by claim |

**Why G7 exists as a separate gate from G4.** Budgets only cover paths that
have a budget test. Every serious stall found in this project so far was in a
path that had none: a full-canvas rescale per frame, an `O(area × radius)`
erode, per-pixel Perlin, per-pixel Kubelka–Munk, an idle compositing buffer
accumulating a union of every dirty region. G4 asks "did the measured things
get slower"; G7 asks "does this diff contain a shape we already know is
wrong". They fail on different days.

A round that cannot satisfy a gate is reverted or narrowed, never merged with
the gate marked "expected to fail".

## 2. Standing objectives — the backlog the loop mines

**O1 · Cover the hotspots.** `HOTSPOTS.md` ranks files by change risk. A file
in the top ten with no test reference is the highest-value test to write.

**O2 · Guard behaviour, not implementation.** A regression test should fail
for the reason the user reported, and keep passing through a refactor. Tests
that assert internal call order are a liability.

**O3 · Keep painting interactive.** Pointer-to-pixels must stay far inside a
60 Hz frame on the largest supported canvas (4K). Costs that scale with
canvas area per event are defects; costs that scale with the region a stroke
touched are fine. This includes the render thread: presenting a frame is a
per-frame cost and is usually the larger of the two on a big document.

**O4 · Keep memory bounded.** Caches are sized in bytes, never in item count —
96 frames is 100 MB at 960×540 and 3 GB at 4K.

**O5 · Prefer deleting to adding.** Two ways to do the same thing is a bug
report waiting to happen.

**O6 · Ask rather than assume.** A request that admits more than one sensible
reading goes to `QUESTIONS.md` with the options and a recommendation. The
loop keeps working on the unambiguous parts meanwhile.

**O7 · Test what a setting does, not that it changed.** A setting is only real
where it reaches an output. `CanvasReliefTests` covered every branch of the
decision to drop to half quality — when it fires, that it fires once, that it
never overrides a chosen default, that it always says so — and not one test
read the published image. Deleting the wiring from `CanvasQuality` to the
composite would have left the whole suite green and the feature reduced to a
status message. The assertion that closes this shape is always downstream:
the rendered pixels, the written file, the exported bytes.

**O8 · Draw a curve, not just a line.** Every brush pixel test in this
repository drew a straight stroke, and the arc artifact of M16b lived
undetected in the dab walk the whole time because a straight path is the one
case where cutting the corner costs nothing. A render test that only ever goes
in a straight line cannot see a whole class of defect. Bends, and the corners
between them, belong in the fixtures.

## 3. Performance budgets

Measured on the dev container, which is slow; a mid-range desktop is several
times quicker. Budgets are set roughly 4× above the observed cost so a noisy
shared runner does not produce false alarms.

**How a budget is measured, and why it is not the median.** Every budget
compares the **fastest** of several runs, through `Bench.FastestMs`. A median
measures the machine as much as the code: when the scheduler takes cores away,
half the runs are slow and the median goes with them. That is not theory —
under six busy threads on four cores the flood-fill median moved from 114 ms to
179 ms against a 250 ms budget, and budgets in this file had already been
raised twice, one number at a time, to stop exactly that. The fastest run is
the least contaminated estimate of what the code costs, and an
order-of-magnitude regression — the only kind these tests are for — raises the
floor as surely as it raises the middle.

The trade is real and worth naming: this is blind to a path that is *usually*
fast and *sometimes* terrible. That belongs to a latency test with percentiles,
not to a regression budget, and a median contaminated by the scheduler never
covered it either.

The timing tests also take the `Performance` collection, so they do not run
beside the rest of their own assembly. A budget measuring wall-clock time while
three other threads rasterise is measuring the wrong thing.

| Path | Budget | Observed |
| --- | --- | --- |
| Pointer event during a stroke, 4K, 180 px brush | 20 ms | ~2 ms |
| Whole stroke + commit, 4K | 400 ms | ~78 ms |
| Stroke + undo, 4K | 1500 ms | ~520 ms |
| Live effect-brush segment, 960×540 | 12 ms | — |
| Flood fill, 960×540 | 250 ms | — |
| Frame cache ceiling | 512 MB (cache only) | 601 MB total, incl. ~90 MB of compose buffers |
| Presenting a frame, 4K zoomed to fit | 20 ms | ~11 ms at display resolution (~29 ms before) |
| Textured (paper) commit, 4K, 500 px | 1200 ms | ~225 ms (3002 ms with per-pixel SetPixel) |
| GC pause during a 4K stroke | — | 0 ms, 0 collections, 0.3 MB over 60 events |
| First event of a stroke, 4K, after a stroke crossing the canvas | ≤ 2× the steady-state repaint | 41 k px² / ~2 ms (6.0 M px² / 129 ms before the compose-ring catch-up) |
| Pointer event on an alpha-locked layer, 4K | 20 ms | ~2.6 ms (~1.8 ms unmasked — the live mask's SaveLayer costs ~0.8 ms) |
| Whole wet-media stroke + commit, 4K, 90 px, 20 events | 12000 ms | ~2250 ms over 20 live passes |

**The wet-media pass no longer grows with the stroke.** It used to re-render
through `BrushEngine.StampStroke`, re-stamping every dab each time;
`PostProcessDabs` reuses the dabs the preview already has and runs only the
effects. Measured at 2400×1200 with a 90 px watercolour brush:

| stroke | `StampStroke` | `PostProcessDabs` |
| --- | --- | --- |
| 6 segments | 134.8 ms | 129.4 ms |
| 60 segments | 109.7 ms | **52.4 ms** |

The win is what it was meant to be — flat instead of climbing — and it arrives
where it matters, on the long strokes where a lagging preview is most
annoying. On short ones it is a wash.

Two things that measurement corrected, both worth keeping written down. The
dabs were **not** the dominant cost at ordinary sizes: at 20 segments they
were 32 ms of a 76 ms pass, and the medium simulation was the rest — an
earlier note here asserted otherwise on no evidence. And a *shorter* stroke
can cost *more*: `MediumSimulator` caps the lattice by the longest side, so a
long thin stroke gets coarser cells and fewer of them. The remaining lever is
the simulation itself, not the stamping.

Raising a budget requires a measurement in the commit message explaining what
got slower and why that is acceptable.

### The budgets are the ratchet. The map is somewhere else.

Everything above answers one question — *did this diff make a path we already
know about slower* — and it answers it on every commit, which is why it is
cheap and fixed. It cannot answer *where does this stop being usable* or *what
should we fix first*, and it never grows a sequence: every budget in the table
measures one stroke on one frame, which is drawing rather than animating.

`tools/Lightbox.Bench` is the other artefact. It sweeps a dimension, fits the
exponent, and reports the **cliff** — the first value whose p95 misses the
budget for that cadence. It takes minutes, so it is run deliberately and never
from the loop; `docs/DESIGN-performance.md` argues the split, and
`.claude/quality/PERFORMANCE.md` is its output.

Two differences from the budgets are deliberate rather than accidental:

- **p95, not the fastest run.** The argument above for the minimum is right for
  a ratchet and names what it gives up — *blind to a path that is usually fast
  and sometimes terrible*. For a cliff that blind spot is the subject: a
  repaint that is 8 ms nineteen times and 40 ms once is a visible hitch.
- **Ranked by pressure, not by milliseconds.** `p95 ÷ budget`, where the budget
  already encodes how often the thing is paid. Ranked by raw cost the simulated
  media come first — they are the largest numbers here — and they are paid once
  per stroke on a brush the picker badges as expensive, so that ranking is
  wrong.

**A cliff that moves the wrong way is a bug in `BUGS.md`**, with the two
measurements as its evidence — not a gate, and not a note in a report. A gate
would put a minutes-long sweep in the commit loop and fail falsely on a noisy
runner; a report with its own private backlog gets read once. The ledger is the
only mechanism here that has ever changed what gets built.

**The garbage collector is not in the drawing path.** Measured on a 4K canvas
with a 500 px brush over 60 pointer events: zero collections in any generation,
zero total pause, 0.3 MB allocated. Every stall found so far has been
algorithmic or an API-usage mistake, and three of the four were inside Skia's
native code. Before proposing a native rewrite, re-run that measurement — it is
the only evidence that would change the answer.

## 4. Definition of done for a user-visible change

1. It works when driven the way a user drives it (headless pixel test where
   the UI is involved, not only a view-model assertion).
2. A test fails without the change.
3. The behaviour inventory gains a line describing the new promise.
4. Performance gates still pass on the largest canvas.
5. Anything ambiguous that was decided by guesswork is written down in
   `QUESTIONS.md` so the guess can be corrected cheaply.
