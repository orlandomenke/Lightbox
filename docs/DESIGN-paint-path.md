# The paint path: one mark, made once, under the pen

*Design note, 2026-10-10. A direction, and a record of the phases as they
land — each with its own measurements. The numbers are the perf lab's
(`perf/lab.py`, `paint-stroke`), from the frozen baseline: `main` at `70d8a644`,
1080p, three runs on a quiet machine.*

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

Five things, in the order an artist meets them. Lab, 300 px brush, 900 to
3000 px/s, ms:

| | ink | soft round | pencil | watercolour | gouache | oil |
| --- | --- | --- | --- | --- | --- | --- |
| pen → screen, median | 32–40 | 42–45 | 39–43 | 34–42 | 31–40 | 39–41 |
| pen → screen, worst | 67–124 | 128–240 | 223–490 | 136–174 | 64–134 | 61–64 |
| live pass, median | — | — | 4 | **47–52** | 25–28 | 21–23 |
| finish lands this many points late, worst | — | — | 6–8 | 15–20 | 12–14 | 12–13 |
| pen-up re-raster (`append.raster`) | **24–63** | 62–170 | 146–400 | 67–107 | 45–84 | 54–117 |
| pen-up, whole (`stroke.commit`) | **77–114** | 113–218 | 197–454 | 133–166 | 106–140 | 107–171 |

Under load the same build is several times worse and the pencil collapses
altogether (pen → screen 2.9 s at 300 px, B437); a quiet machine is the kind
case.

**1. Pen-up renders the stroke a second time.** `EndStroke` calls
`AppendToFrameRender`, which runs `FrameRasterizer.Append` — the whole stroke,
stamped again from its record into the frame's bitmap, on the UI thread. The
live scratch already holds those pixels. This is the stall at the end of every
stroke on **every** brush, ink included: 24–63 ms at 300 px, up to 400 for the
pencil, and it grows with the brush and the length of the stroke. The rest of
pen-up — about 45 ms that does not vary — is the undo snapshot, the tiles, the
edit and its thumbnails.

**2. Pen-up moves the line.** `EndStroke` runs the stabiliser's `PostProcess`
over the points: three of its modes (Laplacian, moving average,
Savitzky–Golay) re-shape the whole stroke after the pen has left it.

**3. Textured and simulated brushes are finished by a pass that runs behind the
pen.** Wet edge, granulation, an ungated paper texture and every medium are
whole-mark operations (`PostProcessRegion`) queued on a worker, single-flight.
The artist sees the raw dabs, then the finish, 15–20 points later on a
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

And one that is not the brush's: **pen → screen sits at 31–45 ms median on every
brush**, ink included — two to two and a half refreshes of a 60 Hz display. That is publish pacing and compose, the
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
strokes meet. The owner took that knowingly (**Q240**, 2026-10-10): the lattice
leaves the codebase, not only the path between the pen and the screen, and
strokes already saved with a simulated medium re-render with the recipes.

## How it is built and judged

One branch, `feat/brush/paint-path`, against a **frozen baseline**: `main` as of
2026-10-10 (`70d8a644`, tag `baseline/paint-path-2026-10-10`), built once and
kept. The whole change is measured against that build with the lab — every
shipped brush, 60 / 150 / 300 px, 900 and 3000 px/s, 1080p and 4K — not each
step against the one before, because a step can gain while the whole stands
still. Steps still run the lab; they are judged at the end.

What "flawless" means as numbers the lab reports, for **every** shipped brush:

| | target | baseline, worst brush |
| --- | --- | --- |
| deferred passes during a stroke (`live.pass` count) | 0 | one per ~3 events |
| pixels changed by pen-up | 0 | every textured or simulated mark |
| pen-up (`stroke.commit`), 300 px | under one refresh, 16 ms | 454 ms |
| pointer event on the UI thread (`stroke.move`), worst, 300 px | under half a refresh, 8 ms | 22 ms |
| longest stall during a stroke | under two refreshes, 33 ms | 445 ms |
| pen → screen, median | 25 ms — needs the pacing work | 45 ms |

In the suite, where a lab cannot run: a byte-exact live-against-replay test
for every shipped preset; a test that the frame after `EndStroke` is the frame
before it; and a guard that no shipped preset queues a worker pass.

## Phase 1 as built: live is the replay, and pen-up promotes it

`PenUpChangesNothingTests` measures, for every shipped preset at two sizes,
the live mark at pen-up against a fresh render of the stroke's record. Before
any change every plain brush was already within a level on a few dozen pixels
— close, and not the same, and the reasons turned out to be three:

- **The replay stamped in a different frame of reference.** `StampPaint`
  drew its dabs into a stroke-sized scratch under a translation; the live path
  draws them into a document-sized one under an identity matrix. Skia's
  gradient arithmetic is not translation-invariant, so the same dab came out a
  level different on a scattering of edge pixels. Stamped in document
  coordinates the replay equalled the live mark exactly, which is the fix: a
  document-scale render now stamps its dabs through a pooled document-sized
  plane (`DocPlane`) and copies the stroke's window out. **Document
  coordinates are the canonical frame.** A render that is not in them — a
  grown paper's surface, a tile, another output scale — agrees to a level on
  rare edge pixels and exactly elsewhere, and `DocumentOriginTests` says so
  rather than demanding bytes.
- **Ink's silhouette was two different sums.** Live accumulated coverage into
  a buffer and turned it to ink; the replay did it inside a layer at the
  layer's offset and through a different blit. The replay now takes the live
  path's own two steps in the plane.
- **A dab's coverage radius depended on where the call began.** The edge
  shader was reused while the radius was within a hundredth of a pixel of the
  last one *built*; live calls once per pointer event, the replay once per
  stroke. It is snapped per dab now. This was most of a fine ink line: 1,618
  of 3,971 pixels.

One thing looked like a cause and was not: the quarter-pixel settle tolerance
(B189). Removing it changed nothing, as `TheStrictSettleRuleIsOneEventLateTests`
had already measured, and it is still there.

With those, ink, soft round, airbrush and the pencil are the replay to the
byte, and `EndStroke` **promotes** them: the stroke's region of the live mark
is copied out before the live state is cleared and laid onto the frame with
the stroke's opacity and blend (`BrushEngine.PromoteMark`), where
`FrameRasterizer.Append` stamped the record again. The guard is narrow on
purpose — `PromotesAtPenUp` lists what is proved (dabs, a silhouette, a
ceiling; no symmetry copies, selection or alpha lock yet), the live dab list
must equal a fresh walk of the committed record (which is what catches pen-up
smoothing and an unposed rig), and a soft brush's ceiling is now always kept
at document scale so that it is the ceiling the record replays with. Anything
else keeps the replay. The test holds the promoted presets to zero differing
pixels, asserts they were in fact promoted, and asserts for **every** preset
that the committed frame is what its record renders.

Every render fingerprint moved with the frame of reference and was re-recorded
with the reason beside it: an existing drawing changes by a level on a few
dozen edge pixels in tens of thousands.

### What the first lab comparison said, and what it changed

The first cut of the above passed every test and was **not a gain on the real
app**. Measured against the frozen baseline (four brushes, three sizes, two
speeds):

- **Pen-up was unchanged**, because promotion almost never fired. The tests
  draw with smoothing off; the app's default smooths the stroke at pen-up
  (`StrokeStabilizer.PostProcess`), so the committed record was never the
  line that had been drawn, and the guard rightly fell back to the replay.
  That was the second rule being broken in the default configuration: the
  line moved as the pen left it.
- **Soft brushes got slower while drawing**: 3.1 → 7.0 ms a pointer event at
  300 px, with pen-to-screen spikes past a second. Keeping the ceiling at
  document scale so that it would be the replay's meant a swept ceiling over a
  600 px window on every event.

Both were fixed by making the thing the same in both places rather than by
picking a side:

- **Smoothing is live.** The stroke in hand holds the smoothing of every raw
  point so far (`StrokeBuilder.Smoothing`); a new point changes only the last
  few, which the provisional tail and `IncrementalDensify` were already built
  for. At pen-up the stroke is what the pen-up pass used to produce, so the
  committed record is unchanged — it is now also what was on screen.
- **The ceiling's resolution belongs to the brush** (`BrushEngine.CeilingScale`):
  sixteen samples across the soft edge, a whole reciprocal of the document's
  scale, the same number live and on replay. A 300 px soft round keeps its
  ceiling at a sixth; the swept ceiling's window shrinks by the square.

Two costs the lab found in pen-up that were nobody's design:

- **The tile update copied the whole frame once per tile.** `DrawBitmap` on
  the frame's own bitmap wraps all of it in an image each time — 8 MB at
  1080p, a dozen times a stroke: the 12 to 24 ms `append.tiles` cost at every
  pen-up whatever the brush. It copies from one view of the stroke's region
  now.
- **Thumbnails were rendered inside pen-up**, 15 to 16 ms. They wait for the
  pen to rest (`RefreshThumbnailsWhenThePenRests`), behind pointer input.

### The second comparison: pen-up is under one refresh

Same lab, same frozen baseline, after the changes above. Median ms, the range
across 60, 150 and 300 px at 900 and 3000 px/s:

| | ink | soft round | airbrush | pencil |
| --- | --- | --- | --- | --- |
| pen-up (`stroke.commit`), baseline | 43–106 | 60–221 | 57–210 | 86–442 |
| pen-up, branch | **10–12** | **9–13** | **9–13** | **9–13** |
| of which the stroke rendered again | 6–58 → 0 | 24–175 → 0 | 23–163 → 0 | 51–393 → 0 |
| of which the tile update | 12–21 → 1–2 | 10–21 → 1–2 | 10–21 → 1–2 | 9–21 → 1–2 |
| longest stall in a stroke, baseline | 0–109 | 74–213 | 56–224 | 109–427 |
| longest stall, branch | 0 | 0–58 | 0–52 | 0–61 |
| pen → screen, worst, baseline | 65–143 | 88–265 | 91–231 | 73–309 |
| pen → screen, worst, branch | 58–66 | 57–68 | 56–74 | 62–216 |

Every stroke was promoted. Pen-up no longer depends on the brush, its size or
the length of the stroke: what is left is the edit step (6 ms, unchanged), the
promotion (0.2–1.2 ms), the tiles (1–2 ms) and the publish.

Not everything moved the right way. The **pencil's pointer event** costs more
at large sizes (3.5 → 6.0 ms median at 300 px and 3000 px/s) — its tooth is
stamped through an image shader and a colour table where a soft brush stamps a
gradient — and one variant's worst pen → screen rose (73 → 216 ms). The soft
brushes' per-event cost is at or below the baseline (4.1 → 3.0–3.2 ms at
300 px) now that their ceiling is coarse. Pen → screen's median did not move,
as expected: 31–45 ms on both sides, the pacing floor.

## Phase 2 as built: wet edge, grain and paper land under the pen

The three "(flat)" presets — Watercolor (flat), Gouache (flat), Oil (flat) —
were the brushes whose mark was finished by a pass after the dabs: a wet edge
darkened from a blur of the whole stroke, grain and ungated paper multiplied
over it. They now finish in place, on the band each pointer event touched, by
the same call the replay makes (`BrushEngine.FinishBand`), and their pen-up is
a promotion like the plain brushes'.

- **One finish, in a fixed order**: the ceiling and the wet edge, then grain,
  then paper. `NeedsBandFinish` says which brushes take it; a brush with a
  simulated medium still does not, and still replays (phase 3).
- **The wet edge is read off the ceiling, not off a blur.** The swept ceiling
  already computes an exact distance transform of the mark's support, so the
  rim is a function of how far a pixel is from the edge of the paint
  (`Rim(Strength, WidthPx)`, measured from a quarter of the falloff in, and
  weighted by how much paint is there). A distance to the edge of the support
  is settled as soon as the dabs around it are, which a blur of the stroke
  never was. **This is a new formulation of the look, not a port of the old
  one** — the rim sits where it did but is not the same pixels. Rendered side
  by side with the commit before it — the three flats at their own size and
  at three times it, rising pressure, a loop crossing itself — the two sheets
  could not be told apart by eye. The fingerprints for a wet edge of 0.7 did
  move, and are re-recorded with this as the reason.
- **The cap is always exact, in chunks.** The coarse step above a size
  threshold is gone: the ceiling is already kept at the brush's own scale
  (`CeilingScale`), and the cap works through `CapChunk`-sized pieces of the
  band, so its cost follows the band and not the stroke.
- **Grain and paper are stencils in document space**, seeded as they always
  were, applied to the band after the cap.

One thing stood between this and byte-exactness, and it is worth keeping
written down because it did not look like its cause:

- **A clip changes how Skia fills a rotated oval — even a clip the oval lies
  wholly inside.** The replay stamped its dabs into the document plane under a
  clip to the stroke's window; the live scratch has none. Round dabs and
  unrotated ovals came out identical; a dab that follows the stroke's heading
  is an oval under a rotation, filled as a path, and its edge differed by one
  level on one to seven pixels of a 35,000-pixel stroke. Oil (flat) was the
  only shipped brush that showed it. The window is now cleared under the clip
  and drawn into without one. (Ruled out on the way, by measurement: a dab
  settling at one heading and replaying at another. Settling on the whole dab
  rather than its position changed no pixel here and moved the blur tool's
  live fidelity outside its bound, so the rule stays as it was.)

### What review found, after the suite was green

The first version of this phase passed every test and was wrong in three ways.
Each was found by a reviewer arguing from the code, then reproduced as a
failing test before it was fixed.

- **A stroke that comes back alongside itself.** A ceiling and a wet edge are
  functions of how far a pixel sits inside the edge of everything the stroke
  has covered, so a later dab changes pixels it never touches: the second leg
  of a U-turn swallows the first leg's facing edge. The band a pointer event
  re-finished reached only as far as its new dabs, so the screen kept a soft
  edge and a rim the record did not have, and promotion wrote them into the
  frame — 1,300 pixels and 104 levels on a zigzag of the soft round, which
  means phase 1 had it too. The finish band is now grown by
  `BrushEngine.FinishReachPx`: one falloff width for the ceiling, a quarter of
  one plus the rim's width for a wet edge.
- **A tip that turns where it stands.** With smoothing on, a dab that follows
  the stroke keeps its place while its heading changes. The live path settled
  dabs on position alone, kept each at the angle it landed with, and the
  record replayed the angle it ended with: 7,500 pixels and 28 levels on a
  zigzag of Oil (flat). The paint path now settles on the whole dab
  (`BrushEngine.SettledCount`), and a stroke in which a settled dab changes
  anyway is drawn but not promoted. The blur tool keeps the position rule,
  because its draft re-stamps without taking back and a stricter rule there
  blurs the tail twice.
- **A document too large for the plane.** Above 64 million pixels a replay has
  no document plane and stamps under a translation, so a live mark there is
  not the replay. Promotion is refused there
  (`BrushEngine.ReplaysInDocumentSpace`); such a document pays the old pen-up.

And two about cost. The cap's tiles each carry a halo of the brush's reach, so
a fixed 384 px tile under a 250 px reach was eight parts halo in nine; tiles
are now four reaches a side at least. Its two arrays, hundreds of kilobytes
for a large brush, were allocated per tile per pointer event and are now
rented.

**What is still not guarded.** The promotion guard compares dab lists, not
pixels, so its correctness rests on the tests in `PenUpChangesNothingTests`
covering the shapes that matter: a rising sine and three self-overlapping
strokes, six presets, smoothing on and off. An ungated paper texture is
filled per band per event from the paper's noise rather than from a tile, and
no shipped preset uses one. Neither has a lab scenario yet.

`PenUpChangesNothingTests` holds the result: every Paint preset at four
size-and-smoothing combinations must commit a frame equal to its record's
replay (invariant 1), on a rising sine and on strokes that meet themselves, and the seven presets without a simulated medium must
also be promoted with zero differing pixels.

### What the lab said about phase 2

Same lab, same frozen baseline, seven brushes at 60, 150 and 300 px and at 900
and 3000 px/s. Two runs: the first as phase 2 was committed, the second after
the fixes it prompted. Median ms, the range across the six combinations.

| | Watercolor (flat) | Gouache (flat) | Oil (flat) |
| --- | --- | --- | --- |
| pen-up (`stroke.commit`), baseline | 89–359 | 77–303 | 60–241 |
| pen-up, second run | 11–58 | 10–63 | 9–14 |
| a pointer event (`stroke.move`), baseline | 0.5–2.1 | 0.4–1.8 | 0.4–1.6 |
| a pointer event, first run | 3.9–15.9 | 2.1–15.6 | 1.2–5.8 |
| a pointer event, second run | 1.6–8.6 | 1.2–8.6 | 1.0–4.5 |
| longest stall in a stroke, baseline | 78–354 | 72–311 | 59–250 |
| longest stall, first run | 456–569 | 409–437 | 59–141 |
| longest stall, second run | 58–170 | 52–154 | 56–85 |
| finish behind the pen, in events (`live.behind`, worst), baseline | 44–47 | 45–48 | 2–4 |
| finish behind the pen, branch | 0 | 0 | 0 |

**What it bought.** The finish is never behind the pen — it was 44 to 48
pointer events behind on the two wet-edged flats — and pen-up no longer scales
with the stroke. The four plain brushes kept what phase 1 gave them: pen-up of
8 to 19 ms, at or near their baseline per event.

**What it cost, and what the first run found.** The finish used to be paid on
a worker, behind the pen; now it is paid in the pointer event. The first run
showed that honestly and badly: 10 to 16 ms an event at 300 px against an
interval of 8.3, and the screen 800 ms behind the pen. Profiled, the event
was the cap's pixel loop and the grain in equal parts, and four things were
not work at all — a four-megabyte copy of the grain tile per band, the tile
itself built by the first event of the first grained stroke (the 400 ms
stall, at every size), a distance transform reaching a whole radius where a
falloff is read, and a floor and four clamps per bilinear read. The second
run is after those.

**What is still wrong after the second run**, and is not hidden by the table:

- **300 px at 3000 px/s is over budget on the two wet-edged flats**: 8.6 ms an
  event. The screen then trails by 250 to 350 ms at the median. Everything
  slower or smaller keeps up (32 to 46 ms, the pacing floor). Since then the
  cap runs in strips side by side (`CapStrips`, a quarter off the event,
  measured headless and not yet in the lab); the rest of the gap is the
  grain's draw over the band and the band's own size.
- **A screen that goes unrefreshed while the UI thread is not stalled.** One
  frame in a gouache run carried an input 1.4 s old while the pen tip was 54 ms
  fresh and no stall was recorded: publishes were not going out during a
  stroke whose events cost about half their interval. That is the pacing
  layer starving under input, the pen → screen thread's ground; it is recorded
  here because this branch is what provokes it.
- **About one stroke in eight on the flats is still rendered again at pen-up**,
  and the lab could say that it happened but not why. `commit.replayed` now
  carries the reason into the perf log.
- **A baked grain mask was tried and withdrawn.** Working the grain's alpha
  out per texel once, instead of through a colour matrix per pixel, saved
  0.7 ms an event and moved every grained stroke by a level. Not worth
  changing existing art for.

### The replayed strokes, and what ending them cost

The third lab run named the reason for every stroke rendered again at
pen-up: *a settled dab changed*, thirteen times in ninety-six strokes. The
live path judged a dab settled when the previous walk of the stroke agreed
with this one (forgiving a quarter of a pixel on brushes with nothing seeded
from position), and one capture with the detail showed a dab three from the
tip, stamped for good, whose pressure then changed.

It could not be reproduced outside the application. Random strokes built of
long unchanging runs, through the same walk, with the same curve cache and
batched as the lab batches them, never fooled either old rule. So the
replacement was chosen for what it can be shown to do rather than for
explaining the failure: `BrushEngine.SettledPrefix` calls a dab settled when
it comes out the same with the newest points taken away, which is a
comparison between two walks of the stroke *as it is now* and so does not
depend on whether anything happened to change since the last event.

**The fourth run: no stroke was replayed.** Ninety-six strokes on the three
flats and the soft round, pen-up 9 to 17 ms on all of them.

**And it was sent to the lab measured for correctness and not for cost,
which the owner saw before the numbers did**: *"the mark falls behind the
pen, new build."* With three points taken away (four with smoothing) the
unsettled tail was two to three times as many dabs, every one stamped again
on every event. Gouache went from 2.2 to 3.9 ms an event at 60 px and
3000 px/s and from 4.4 to 6.7 at 150 px; the soft round from 1.9 to 3.3.
(Watercolor (flat) read worse still, 12 ms, in a part of the run where the
baseline's own numbers were up by four fifths, so the machine was busy and
that figure is not the rule's.)

The horizon is now one point, the least the property test accepts — it
accepts it because the comparison is itself the test of dependence. Profiled
on and off in the same run, headless, on the lab's stroke shape: the tail is
about 60% longer than under the old rule instead of two to three times, and
an event costs 0.5 to 1.4 ms more at a fast pen.

**The fifth run, on the one-point horizon.** All 192 strokes were kept, on
the three flats and the soft round, and pen-up was 12 to 22 ms (gouache at
300 px: 30 to 44). Per event, median ms, baseline to branch:

| | 60 px | 150 px | 300 px |
| --- | --- | --- | --- |
| Watercolor (flat), 900 / 3000 px/s | 0.7→2.1 / 1.2→4.3 | 0.6→2.9 / 1.4→4.6 | 1.0→6.8 / 2.3→9.0 |
| Gouache (flat) | 0.5→1.9 / 1.1→3.5 | 0.5→3.4 / 1.4→5.7 | 1.3→17.4 / 3.3→19.4 |
| Oil (flat) | 0.8→3.5 / 1.6→5.2 | 0.6→3.1 / 1.2→4.2 | 0.9→3.8 / 2.1→4.6 |
| Soft round | 0.6→1.7 / 1.5→3.1 | 1.7→1.6 / 2.7→2.7 | 3.3→3.2 / 4.7→4.3 |

At 60 and 150 px every brush keeps the screen at the pacing floor (36 to
45 ms pen to screen at the median). **300 px is not solved.** Watercolor
(flat) keeps up at 900 px/s and trails by half a second at 3000; Gouache
(flat) at 300 px costs 17 to 19 ms an event, twice the interval between
events, and the screen runs more than a second behind at either speed. Its
baseline was also half again slower in this run than in the others, so the
machine was not quiet, but no reading of the numbers makes 17 ms acceptable.
Gouache differs from watercolour in keeping its ceiling at a finer scale (a
harder edge), which is where to look first. This is the open item phase 2
leaves. If one point is ever too few, the live mark is drawn and not kept,
so the price is a stall at pen-up and never a wrong pixel.

## Phase 3, so far: the media as recipes

The owner's brief, once the first sheets were on the table (2026-10-11): no
physics, *"but visually I would like to come really close with how pigment and
water/oil interact with the paper/surface"* — pressure that decides how the
paint meets the sheet, on every medium; paint that can be reworked; and, later,
paper that can be wetted first. Each medium is judged on a sheet beside the
simulated brush it replaces before it ships.

| | what it is now | decided in |
| --- | --- | --- |
| **Watercolor** | a crisp thin wash; a wet edge that *gathers* paint at the rim and thins the middle; the scanned Fibre sheet; colour picked up from beneath; paint that runs out | Q244 |
| **Oil** | paint that a press of a third pushes into linen until it is solid (*Fills at*); the canvas showing as the brush empties; a tip with hairs, each with its own contact, so the dry end is streaks | Q246 |
| Gouache, Ink wash | still simulated | — |

Three pieces of engine carry it, all ceilings or stencils laid under the pen:

- **The wet edge gathers.** It used to darken the rim's colour and leave the
  middle alone, which on a pale wash cannot be seen. It replaces the old one
  for every brush.
- **Paint fills; graphite does not.** With a fill pressure the tooth's
  threshold runs down past the deepest valley, and how hard the paint is
  pressed is pressure times the paint left on the brush.
- **A hair is a contact, not a shade.** `BrushSettings.Bristles`, on the tip
  and for any brush. The first version made each hair a level and got a ribbon
  with faint stripes; gating the sheet hair by hair gets streaks.

**A mixing stroke is kept at pen-up.** Both recipes mix colour with what is
under them, and a mixing dab's colour depends on the pixels of every dab before
it — so a level's difference in how one dab is rasterised is carried down the
stroke. The replay stamped a mixing stroke into a stroke-sized scratch under a
translation while the live path stamps in the document's own coordinates. It
now goes through the document plane like every other brush, reading the stroke
so far from the plane and the layer beneath at the same pixels, and
`AMixingStrokeAcrossPaintIsKept` holds Watercolor and Oil to byte-exact and
kept across existing paint. No fingerprint moved. (A brush that mixes with a
composite of the layers beneath is still replayed: the two paths are handed
that composite differently, and it is not yet held to be the same.)

Krita does not have this problem, and it is worth saying why: its document is
the pixels. A dab is painted into the layer once, undo restores saved tiles,
and nothing is ever replayed. Here the record is the document (invariant 1),
which is what gives resolution-independent output and strokes an inbetweener
can read — and the price is that the mark drawn live has to be shown to be the
mark a replay makes. That proof, brush family by brush family, is what this
note has been.

### What the lab said about the two recipes

Oil and Watercolor as they ship now, against the frozen baseline where both
are still simulated; 60, 150 and 300 px at 900 and 3000 px/s, median ms.

| | Oil | Watercolor |
| --- | --- | --- |
| pen-up (`stroke.commit`), baseline | 63–181 | 69–178 |
| pen-up, branch | **11–20** | **12–15** |
| strokes kept at pen-up | 48 of 48 | 48 of 48 |
| finish behind the pen, events, baseline | 7–14 | 9–20 |
| finish behind the pen, branch | 0 | 0 |
| a pointer event at 60 px, 900 / 3000 px/s | 0.4→1.4 / 0.8→3.5 | 0.3→1.4 / 0.7→2.4 |
| a pointer event at 150 px | 0.5→3.1 / 1.2→6.6 | 0.4→3.2 / 0.8→4.5 |
| a pointer event at 300 px | 0.8→6.3 / 1.7→**25.0** | 0.7→8.1 / 1.2→**11.8** |
| pen → screen at 300 px | 40→46 / 41→**1532** | 41→**126** / 43→**1412** |

**What it bought:** the mark under the pen is the finished mark, and pen-up
is one refresh instead of four to eleven. At 60 and 150 px both brushes hold
the pacing floor (34 to 45 ms pen to screen).

**What it did not:** at 300 px a fast pen leaves the screen a second and a half
behind on both, and Watercolor trails at the slower speed as well. It is the
same shape as phase 2's open item and for the same reason: everything the
simulation used to compute on a worker, behind the pen, is now paid in the
pointer event, and at 300 px one dab is seventy thousand pixels through a
ceiling, a rim or a hair-gated sheet. Oil is the worst because its dabs are the
dearest (an image shader, two colour filters and two composes each, plus the
ground it reads to mix) and the unsettled tail stamps each of them again every
event.

**This is the next piece of work, ahead of more recipes.** The rule this
note answers to is "no stalls, no delays", and a recipe that falls a second
behind at a size artists use is not done. Where to look, in order of what the
numbers suggest: the tail - stamping its dabs once into a layer of their own
that is composed, rather than stamped and taken back each event; the cost of
one large dab; and whether the finish of a band can run off the UI thread and
still land before the frame it belongs to.

### Large brushes: the finish is done when the mark is looked at

The first answer to that lab run, and it is a change of shape rather than a
faster loop. Profiled at 300 px on the lab's stroke, a Watercolor event was
mostly its finish (the distance transform under the rim, at the document's
scale, 170,000 cells) and an Oil event was mostly footprint dabs - 1.6 ms
each, and three of every four of them were the unsettled tail, stamped on
every event and taken back on the next.

Both are now done **when the mark is looked at** - a frame being composed, a
worker pass about to read the dabs, the pen lifting - and not on the pointer
event (`PrepareLiveMark`: `StampLiveTail`, then `FinishLiveMark`). An event
stamps its newly settled dabs and notes what it touched. A pen reports about
twice as often as a screen refreshes, so two or more events usually share
one finish; consecutive bands of a broad brush are nearly the same pixels,
so their union costs little more than one; and when the app is behind, the
events that queue share a finish instead of each making the queue longer -
which is the loop that put the screen a second and a half behind.

Per event at 300 px and 25 px a step, headless with no frame composed
between events, so this is what is left *on the event*: Oil 9.5 → 2.2 ms,
Watercolor 7.0 → 0.7, the simulated Gouache 5.6 → 0.6, the soft round
2.4 → 0.4. The finish is still paid, per frame; what that comes to in the
application is the lab's to say and has not been measured.

Nothing about the pixels changes, and three things hold it to that: the band
is copied afresh from the raw dabs at each finish, so finishing a union once
is what finishing each band in turn came to; `LiveFinishIsPerLookTests` draws
one stroke looked at once after thirty events and again looked at after each,
and gets the same bytes; and every pen-up test is unchanged.

**The lab on it** (five brushes against the frozen baseline, 60 / 150 /
300 px at 900 and 3000 px/s, median ms; 240 strokes, all kept at pen-up):

| | Oil | Watercolor | Gouache (flat) | Watercolor (flat) | soft round |
| --- | --- | --- | --- | --- | --- |
| a pointer event at 300 px, slow / fast, before this | 6.3 / 25.0 | 8.1 / 11.8 | 17.4 / 19.4 | 6.8 / 9.0 | 3.2 / 4.3 |
| a pointer event at 300 px, slow / fast, now | 0.4 / 5.2 | 0.2 / 1.3 | 0.3 / 1.0 | 0.6 / 1.2 | 0.2 / 0.8 |
| pen → screen at 300 px, fast pen, before | 1532 | 1412 | 1529 | 536 | 40 |
| pen → screen at 300 px, fast pen, now | 42 | 38 | 40 | 38 | 42 |
| pen → screen, worst frame anywhere, baseline | 63–164 | 98–199 | 124–325 | 69–360 | 92–182 |
| pen → screen, worst frame anywhere, now | 63–114 | 60–73 | 59–65 | 60–79 | 62–66 |
| pen-up, baseline | 67–180 | 74–181 | 80–304 | 81–338 | 64–226 |
| pen-up, now | 12–24 | 13–29 | 13–22 | 12–26 | 13–17 |

Every brush at every size and speed now holds the pacing floor - 34 to 43 ms
pen to screen at the median - including the 300 px fast-pen cases that were a
second and a half behind, and a pointer event costs less than it does on the
baseline for all but the Oil. That closes the open item phase 2 left and the
one phase 3 opened.

What the table does not show, and is still open: the **longest stall in a
stroke** is 91 to 141 ms on the two brushes that mix colour (Oil, Watercolor)
against 58 to 74 on the others. A mixing brush copies the whole frame at
pen-down to have a ground to read (`_live.MixBeneath`), which predates this
work and is paid once per stroke; at 4K it is thirty megabytes. It wants a
copy of what the stroke can reach, made as it reaches it.

Beside it: the cap's strips share one window (each used to build its own,
halo and all, which made the dearest part dearer), and the distance
transform's lines run side by side. A coarser ceiling was measured and not
taken - eight samples across a soft edge instead of sixteen moved a quarter to
a third of a soft brush's pixels by two or three levels, up to forty-six.

**Not done in phase 3:** gouache and ink wash; a tooth and a wet edge on one
brush (so Watercolor does not yet answer pressure against the paper as Oil
does); the rework brushes (Q245); wetting the page; shaping and moving a tip's
hairs. None of phase 3 has been through the perf lab.

## Order

1. **Promote at pen-up**, for the brushes that are already dab-final (ink, soft
   round, airbrush, pencil). Proves the mechanism and removes the stall the
   best brush has.
2. **The textured effects become ceilings and stencils**: flat grain, ungated
   paper, wet edge. The worker pass loses its non-medium callers.
3. **The media recipes**, with the sheets to judge them by eye: oil first (the
   hairs are written), then watercolour, gouache, ink wash.
4. **Delete** the worker pass, the raw tip, Q168's two routes and the lattice
   (Q240).
5. **Causal smoothing.**

The pen → screen floor runs beside this, not inside it.
