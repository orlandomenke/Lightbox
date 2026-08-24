# Performance: the ratchet and the map

Two artefacts, two jobs. Conflating them is the mistake this document exists to
prevent.

| | **The ratchet** | **The map** |
| --- | --- | --- |
| What | `Category=Performance` tests | `tools/Lightbox.Bench` |
| Runs | every commit, inside `dotnet test` | deliberately, minutes |
| Asks | *did this diff break a path we know about* | *where are the cliffs on paths nobody has walked* |
| Shape | fixed scenario, pass/fail | a curve over a dimension that grows |
| Statistic | fastest of N | p50, **p95**, max |
| Output | green | a ranked, attributable table |

The ratchet came first and it works. What it cannot do is answer "what should
we fix next", because a pass/fail on a fixed workload says nothing about where
that workload stops being viable — and the unit of work here is a *sequence*,
which no existing budget grows.

## Three questions, three outputs

**What grows, and how fast — the exponent.** Every sweep fits `cost ∝ n^k` on
the log-log points and reports `k`. This is the durable result and the
milliseconds are not: every budget in `CHARTER.md` carries a "measured on the
slow dev container, 4× headroom" caveat because a time is a property of the
machine. A slope is a property of the code. Linear here is linear on a desktop,
and a jump from 1.0 to 2.0 is a defect on any hardware — which also means a
regression can be detected without a reference machine.

**When it stops being usable — the cliff.** The first swept value whose p95
misses the budget. *"Onion skin holds under 16 ms to 6 ghosts a side at 1080p;
at 8 it doesn't"* is the sentence an artist can act on, and no pass/fail can
produce it.

**What to fix first — the pressure.** `p95 ÷ budget`, ranked.

### Why p95 and not the fastest run

The budgets take the fastest of several runs, deliberately, and `CHARTER.md`
argues that case well: a median measures the scheduler as much as the code, and
for catching an order-of-magnitude regression the floor moves as surely as the
middle. It also names what that gives up — *"blind to a path that is usually
fast and sometimes terrible"*.

For a cliff that blind spot is the whole subject. A repaint that is 8 ms
nineteen times and 40 ms once is a visible hitch while drawing, and the fastest
run cannot see it. So the map takes p95 and the ratchet keeps the minimum. They
are measuring different things on purpose.

### Why the budget is the frequency

A pointer event gets 16 ms because it happens sixty times a second. An export
gets two seconds because it happens once. So ranking by `p95 ÷ budget` is
already a cost-times-frequency ranking, and a separate frequency weight would
say the same thing twice while giving two numbers to argue about.

This matters because the naive ranking is actively misleading. The simulated
media are the largest raw numbers in the codebase — a watercolour commit is
~30 ms against 4 ms for a flat brush — and they are also paid once per stroke,
on a brush the picker badges as expensive. Ranked by milliseconds they come
first. Ranked by pressure they come nowhere near a 12 ms repaint that fires on
every pointer event, which is the correct answer.

| Cadence | Budget | Why |
| --- | --- | --- |
| Every pointer event | 16 ms | one frame at 60 Hz |
| Every frame of playback | 83 ms | one period at 12 fps |
| Once per stroke or edit | 100 ms | felt as weight, not as stutter |
| Once per session | 2000 ms | open, save, export |

## Measurement discipline

Two traps, both hit for real in this codebase, both now designed into the
harness rather than left to whoever writes the next sweep.

**Two benchmarks in one process are not two measurements.** An A/B on the fluid
lattice read as a 25% regression and was entirely an earlier scenario having
grown a shared buffer; run in isolation the same change was an 11% *win*. The
harness tears down and collects between every value, and scenarios hold their
state in closures that die with them.

**A deterministic workload, or two reports are incomparable.** The synthetic
drawings are placed by a hash of the index, never an RNG. A benchmark whose
input differs run to run cannot detect a regression smaller than its own
variance.

**Calibration, not absolute times.** Each report records a fixed arithmetic
loop. Two reports from different machines compare by the ratio of that figure;
their raw milliseconds do not compare at all.

**A per-dab optimisation measured in Debug is not measured.** Debug inflates
exactly what an inner-loop change removes — unrolled float arithmetic, and a
P/Invoke per Skia call — so it flatters the change roughly twofold and does it
silently. B292 was built, reported as a 27% win end to end, and then withdrawn:
in Release the same pair read 1.876 against 1.861 ms per pointer event, which is
inside the noise of three repeats. The isolated stamp genuinely was 1.65x faster
in both configurations; what Debug hid was that the stamp is a small share of the
event, so making it faster changed almost nothing.

Two habits follow, and the second one is the cheaper of the two:

- **Release for anything below the frame,** because a local `dotnet test` is
  Debug and CI is Release — so a Debug-only conclusion is a conclusion the build
  that ships never had. B298 is the same seam pointed the other way: a
  performance test that passes in Debug and measures a *negative* cost in
  Release, failing only on CI where it reads as somebody else's flake.
- **Price the whole operation before optimising a part of it.** A 1.65x win on
  something that is a tenth of the cost is a 6% win, and finding that out after
  building it costs a branch. B292 and B296 are the same stroke measured twice:
  the outline looked like the bottleneck and the fill was.

## Dimensions

The list is data, not code, so the vector work adds rows rather than a rewrite.

| Area | Dimensions |
| --- | --- |
| **Animation** *(swept)* | layers, onion depth, strokes per frame, frames per scene, playback, scrubbing |
| **Drawing / painting** *(swept)* | canvas area, brush diameter, dabs per stroke, medium flow steps |
| Drawing / painting *(later)* | tip resolution, tip-set blending, smudge sample radius |
| Document | strokes per document, undo depth, clip regions, symbol placements |
| Vector *(later)* | control points per path, paths per frame, boolean ops, hit-testing |

Animation went first because it had **no coverage at all**: every existing
budget measures one stroke on one frame, which is drawing. The application's
unit of work is a sequence, and nothing had ever grown one.

## Two things the first run taught, kept because they will recur

**Group findings by cause, not by symptom.** The first map came back with six
red rows and they were three problems. A full recomposite costing ~20 ms a
1080p layer shows up as four separate failures — scrubbing, playback, onion
depth, layer count — and filing four bugs would have put four people in the
same blend loop. The ranked table is by symptom because that is what an artist
feels; the ledger is by cause because that is what somebody fixes.

**The harness will be wrong before the code is.** Four measurements in the
first sitting were confidently wrong, and each looked like a finding:

| What it said | What it was |
| --- | --- |
| compositing one cached layer misses 16 ms | a fresh 1080p surface allocated inside the timed region |
| a full recomposite blows the frame budget | true, but scored against a budget the app never pays — `ComposeRing` repaints the dirty region |
| onion skin costs 8000% of budget | its scene sat exactly on the cache ceiling; the curve belonged to eviction |
| a cliff moved from 8 to 4 | the same measurement, one rung apart on a geometric ladder |

The pattern is the same each time: **the number was real and the attribution
was not.** So the order of questions when a sweep reports something dramatic is
*what else is in this measurement*, and only then *what is wrong with the code*.
The first three are now designed out; the fourth is why `check` needs a factor
of two before it reports a cliff.

## What happens to a cliff that moves

**It becomes a bug in `BUGS.md`, with the two measurements as its evidence.**

Not a gate, and not a report-only note. A gate would put a minutes-long sweep
in the commit loop and produce false failures on a noisy runner; report-only
gets read once and then ignored. Filing into the same ledger as everything else
means a regressed cliff is prioritised against real work, which is the only
mechanism here that has ever changed what gets built.

## Not in scope

Absolute cross-machine comparison beyond the calibration ratio; profiling
individual functions, which is a profiler's job and not a harness's; and
anything that requires a UI toolkit — the harness drives the rendering and
document layers directly, so it needs no display and no Avalonia lifetime.
