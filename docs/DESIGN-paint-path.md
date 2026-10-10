# The paint path: one mark, made once, under the pen

*Design note, 2026-10-10. A direction, not yet a record of what was built — each
phase below lands with its own measurements and this note is corrected as they
arrive. The numbers are the perf lab's (`perf/lab.py`, `paint-stroke`), taken on
`main` at `8b95af98` with the machine under load, so the absolutes are
indicative and the shapes are not.*

## The rule

The owner, 2026-10-10:

> Painting is the primary function of this app. All other tools, functions and
> capabilities are supporting painting and drawing. […] No texture pop-ins, no
> stalls or delays, no slow downs. […] What you draw is what you see. I do not
> know of any drawing program that changes the stroke after pen lift. So why
> should we.

And the licence that goes with it: refactoring, a change of architecture, and
giving up the fluid simulation are all on the table if that is what it takes.

Stated as something a test can hold:

1. **The mark under the pen is the mark.** Every pixel a pointer event puts on
   screen is final. Nothing arrives later to correct it.
2. **Pen-up changes nothing.** Not the pixels, not the geometry.
3. **A brush's cost is its dabs.** No brush pays per stroke, per pass or per
   canvas — the textured and simulated brushes are in the same class as ink.

## What breaks it today

Five things, in the order an artist meets them. Lab, 1080p, ms:

| | ink 300 px | watercolour 300 px | pencil 300 px | oil 300 px |
| --- | --- | --- | --- | --- |
| pen → screen, median | 41–42 | 43 | 42 – **2900** | 40–41 |
| pen → screen, worst | 141–223 | 242–283 | 462–2925 | 142–241 |
| live pass, median | — | **78–93** | 9–13 | 25–26 |
| texture lands this many points late, worst | — | 24–37 | 0–17 | 13–15 |
| pen-up re-raster (`append.raster`) | **58–160** | 113–181 | 305–1008 | 61–140 |
| pen-up, whole (`stroke.commit`) | **202–276** | 220–265 | 421–1123 | 123–207 |

**1. Pen-up renders the stroke a second time.** `EndStroke` calls
`AppendToFrameRender`, which runs `FrameRasterizer.Append` — the whole stroke,
stamped again from its record into the frame's bitmap, on the UI thread. The
live scratch already holds those pixels. This is the stall at the end of every
stroke on **every** brush, ink included: 58–160 ms at 300 px, a second for the
pencil, and it grows with the brush and the length of the stroke.

**2. Pen-up moves the line.** `EndStroke` runs the stabiliser's `PostProcess`
over the points: three of its modes (Laplacian, moving average,
Savitzky–Golay) re-shape the whole stroke after the pen has left it.

**3. Textured and simulated brushes are finished by a pass that runs behind the
pen.** Wet edge, granulation, an ungated paper texture and every medium are
whole-mark operations (`PostProcessRegion`) queued on a worker, single-flight.
The artist sees the raw dabs, then the finish, 24–37 points later on a
watercolour — the pop-in.

**4. The raw tip is a different mark from the finished one**, which is why two
kinds of brush do not show it at all (Q168) and wait for the pass instead. That
route is what put the first pressure-gated pencil 2.9 s behind the pen at
300 px (B437).

**5. The fluid lattice solves the whole stroke again on every pass**, coarsened
to at most 320 cells a side, and once more at pen-up. Its output is not a
function of the dabs laid so far that later dabs leave alone: flow moves
pigment that was already on screen. It cannot satisfy rule 1 at any speed —
this is a property of what it computes, not of how fast.

And one that is not the brush's: **pen → screen sits at 40–43 ms median on every
brush**, ink included — two and a half refreshes of a 60 Hz display, with the
newest point 33 ms from the glass. That is publish pacing and compose, the
ground the compositing work covers (`docs/DESIGN-gpu-compositing.md`), and it is
the floor under everything here.

## Where to go

### One mark, made once

The live scratch is the stroke. At pen-up it is **promoted** — its region
copied into the frame's bitmap and tiles — and nothing is stamped. Invariant 1
does not bend: the stroke record is still the document and a reload still
renders it through `BrushEngine.StampStroke`. What changes is that the live
path and the replay are held **bit-identical**, by a test that draws every
shipped preset both ways and compares bytes, where today they are held to a
tolerance of one to six levels (`LiveMatchesCommittedTests`). That is only
reachable because of the next section: a deferred whole-mark pass is exactly
what makes live and replay differ.

Smoothing becomes causal. A windowed filter changes only the last half-window
of points, and the engine already keeps a provisional tail it can take back
and re-stamp; the filter runs there, as the pen moves, and the line that is on
screen when the pen lifts is the line.

### Every effect is dab-final

An effect may be exactly one of three things, all evaluated in place on the
band a pointer event changed:

- **What a dab deposits.** Colour (jitter, the second colour, colour mixing
  with the ground — Q232), amount (flow, load, pressure).
- **A ceiling.** A per-pixel running maximum accumulated beside the dabs, which
  the mark is capped to: the soft brush's edge (Q157, B349), the pencil's tooth
  (B437), a paper texture, a flat grain, the hairs of a bristle brush. A
  ceiling is what survives the dozen dabs that overlap every pixel; a
  multiplier applied to a dab does not.
- **A local stencil on the mark's own pixels**, a few pixels across: the light
  on thick paint (`Impasto`), a darker rim at the mark's edge. Its value at a
  pixel settles as soon as the brush has passed.

What is not on the list is anything iterative, global or solved over the
stroke. The engine already has the route for all three — the cap applied in
place per event (`CapIsTheWholePass`) — and today only the plain soft brush and
the pencil take it. The work is moving every other effect onto it and then
deleting the worker pass, the raw tip and the two-route plumbing that exists
to reconcile them.

### The media, without the lattice

Clip Studio Paint's wet media have no fluid in them
(`docs/DESIGN-colour-mixing.md`): they are a textured tip, colour mixing with
the ground, a paper, and an edge. Each of those is on the list above.

| | built from |
| --- | --- |
| **Watercolour** | colour mixing; a wet-edge rim as a local stencil; paper tooth as a ceiling; granulation as a ceiling that favours the paper's valleys; paint load that runs out (B434) |
| **Gouache** | colour mixing; a chisel tip; body and its light as a stencil |
| **Oil** | colour mixing; hairs as a ceiling along the stroke (`docs/DESIGN-oil-hairs.md`); body and its light; paint load |
| **Ink wash** | colour mixing; wet-edge rim; a soft ceiling |

What is given up is what only a solver does: pigment that travels after it is
laid, pooling that depends on the whole wet region, a bloom where two wet
strokes meet. Whether the lattice stays in the codebase at all is a decision
recorded with this note's pull request (Q-number to follow); what is settled is
that it leaves the path between the pen and the screen.

## How it is built and judged

One branch, `feat/brush/paint-path`, against a **frozen baseline**: `main` as of
2026-10-10 (`70d8a644`, tag `baseline/paint-path-2026-10-10`), built once and
kept. The whole change is measured against that build with the lab — every
shipped brush, 60 / 150 / 300 px, 900 and 3000 px/s, 1080p and 4K — not each
step against the one before, because a step can gain while the whole stands
still. Steps still run the lab; they are judged at the end.

What "flawless" means as numbers the lab reports, for **every** shipped brush:

| | target | today, worst brush |
| --- | --- | --- |
| deferred passes during a stroke (`live.pass` count) | 0 | one per ~3 events |
| pixels changed by pen-up | 0 | every textured or simulated mark |
| pen-up (`stroke.commit`), 300 px | under one refresh, 16 ms | 1123 ms |
| pointer event on the UI thread (`stroke.move`), worst, 300 px | under half a refresh, 8 ms | 255 ms |
| longest stall during a stroke | under two refreshes, 33 ms | 1150 ms |
| pen → screen, median | 25 ms — needs the pacing work | 2900 ms; 40 for ink |

In the suite, where a lab cannot run: a byte-exact live-against-replay test
for every shipped preset; a test that the frame after `EndStroke` is the frame
before it; and a guard that no shipped preset queues a worker pass.

## Order

1. **Promote at pen-up**, for the brushes that are already dab-final (ink, soft
   round, airbrush, pencil). Proves the mechanism and removes the stall the
   best brush has.
2. **The textured effects become ceilings and stencils**: flat grain, ungated
   paper, wet edge. The worker pass loses its non-medium callers.
3. **The media recipes**, with the sheets to judge them by eye: oil first (the
   hairs are written), then watercolour, gouache, ink wash.
4. **Delete** the worker pass, the raw tip, Q168's two routes and — per the
   decision — the lattice's place on the paint path.
5. **Causal smoothing.**

The pen → screen floor runs beside this, not inside it.
