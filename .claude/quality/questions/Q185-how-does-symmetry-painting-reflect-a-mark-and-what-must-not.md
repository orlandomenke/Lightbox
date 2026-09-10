# Q185 · How does symmetry painting reflect a mark, and what must not get slower? — **answered 2026-09-10**

**Answered:** one record covering mirror, radial and kaleidoscope; the copies
are made by **reflecting the canvas, not the geometry**; the first landing
reaches every tool that already goes through `BrushEngine.StampStroke`; and
"nothing may influence drawing negatively" is held as **two** claims rather than
one, because only one of them is literally achievable.

Raised by the request *"a symmetry painting tool — select the amount of axis,
rotate the axis, painting adds the same lines across the same points; performance
of drawing is the most important thing and nothing may influence that
negatively."*

`Q15` settled the part that could not be deferred — a mirrored mark is **one
stroke while drawing**, with an explicit "break symmetry" that expands it to two,
so `Mirror` lives on the stroke rather than on the scene. What Q15 did not settle
is everything below, and three of the four could not be guessed: two of them are
not interchangeable later, and one of them is the whole performance constraint.

## 1 · The record: one shape for all three behaviours

`Symmetry { CenterX, CenterY, AngleDeg, Order, Mirror }`.

| Order / Mirror | What the artist gets |
| --- | --- |
| `Order 1`, `Mirror` | the plain left/right mirror — the one character design actually needs most, and the one Q15 was asked about |
| `Order 6`, no `Mirror` | six rotated copies, cyclic |
| `Order 6` + `Mirror` | twelve copies, dihedral — a kaleidoscope |

`AngleDeg` rotates the whole arrangement, which is the "rotate the axis" half of
the request. Rejected: *rotational copies only* (cannot express the plain mirror,
so it fails the case Q15 was raised about) and *mirror axes only, radial later*
(smaller, and defers the axis count that was asked for by name).

## 2 · Reflect the canvas, not the geometry — and this is the load-bearing one

**`Hash01` seeds every dab dynamic from the IEEE-754 bits of a dab's position.**
Scatter, size, flow, roundness, rotation and all three colour jitters come out of
`DeterministicHash.Unit(x, y, salt)`. So a copy stamped at a *reflected*
coordinate re-rolls every one of them: the reflection of a scattered mark is not
a mirrored mark, it is **a different mark in a mirrored place**. That is the
"design question around dab re-seeding" the roadmap flagged as unresolved, and
it is why this could not be picked by whoever wrote the code first.

The answer is invariant 7's own argument, which turns out to generalise:

> Render bigger by scaling the surface, never the geometry.

Symmetry is the same shape of problem, so it takes the same shape of answer.
**Stamp the stroke once per copy at its authored coordinates, onto a canvas
carrying a reflection or rotation matrix.** `Hash01` then sees the coordinates
the artist drew, every time, so every copy is a true mirror *by construction* —
identical grain, and scatter that lands mirrored rather than re-rolled. It also
adds nothing whatever to the per-dab loop, which is the one place the
performance constraint forbids new work. `InDocumentSpace`
(`src/Lightbox.Raster/BrushEngine.cs:835`) already does exactly this
save/translate/scale/stamp dance for output scale, and is where the matrix belongs.

Rejected:

- *Carry the source dab's seed explicitly* — same picture, but it puts a branch
  and extra per-dab state inside the hot loop, which is the cost this whole
  question exists to avoid.
- *Let each copy re-roll its own dynamics* — cheapest, and indistinguishable
  from a true mirror for a hard round brush with no dynamics. For any brush with
  scatter or jitter the halves visibly disagree, and the artist asked for *the
  same lines across the same points*. A mirror that produces a different mark is
  not the feature.

Note that determinism (invariant 2) holds under **all three** options, since each
is a pure function of the record. This is an *expression* question, not a
determinism one, which is why it belongs to the art-director's half of the veto.

## 3 · Reach: everything already on one pixel path

Brush, eraser and smudge — they share `StampStroke`, so one change gives all
three and they cannot drift apart.

Fill and selections are **deliberately excluded from the first landing**, and not
because they are unimportant: a mirrored fill reflects *contours*, not dabs
(invariant 3 — a fill is a `ToolKind.Fill` stroke with contours), which is a
second and unrelated reflection implementation. Landing it in the same branch
would be two objectives in one diff. **When fill symmetry is picked up it needs
its own question**, and it is recorded here as unasked rather than filed as an
unprompted one.

## 4 · "Nothing may influence drawing negatively" is two claims

Symmetry switched **on** necessarily costs more: N copies means stamping N
marks. A budget that denied this would be one nobody could keep, so the
constraint is held as a pair:

| Claim | How it is guarded |
| --- | --- |
| With symmetry **off**, drawing costs exactly what it costs today | `DrawingCostBaselineTests` — a paired A/B on canvas area, a linearity check, and a render fingerprint recorded at `ca00483d` before any symmetry code existed |
| With symmetry **on**, cost is proportional to the copies and to the region they touch — never to the canvas | the same file's `CostGrowsLinearlyInTheNumberOfCopies` and `TheDirtyRegionIsTheSegmentsOwnReach`, plus a `Lightbox.Bench` sweep over `Order` per charter O9 |

Rejected: *guard only that symmetry-off is free* (nothing then stops order-12
stuttering until an artist finds it) and *pin absolute millisecond budgets*
(this runner is noisy enough that an absolute would be decoration — the charter
already says why budgets take the fastest run rather than the median).

## The consequence the next branch must not get wrong

**The dirty region is N rectangles, never their union.** N copies of a mark land
in N places; for a radial order of six they are spread right around the centre of
the canvas, and one rectangle enclosing all of them is very nearly the whole
page. Every pointer event would then repaint the canvas — invariant 6 and charter
O3 broken, and the exact shape G7 exists to catch. Measured on two ordinary marks
at opposite ends of a 960×540 page, the union is **9.8× the paint** of the two
regions separately; at order six it is far worse.

Recorded with it, from the baseline run: a live segment's region is **1312 px²
against an analytic 1312** — 0.25% of the page — and cost follows the segment
rather than the page at **1.00–1.01× for four times the area**. Those are the
numbers symmetry has to leave alone.

## What happened when it was built, 2026-09-10

The record, the stamping and the live preview all landed the same day, and two
things came out differently from what is written above.

**The dirty region is marked per copy and then unioned by the publish, which is
half of what this asked for.** `FlushLivePreview` marks one region per copy —
the right call site, and the one the publish layer will want when it can hold a
region list — but `PublishState.MarkDirty` unions everything marked since the
last publish into a single rectangle. So an event repaints the box enclosing the
copies rather than the copies.

It is milder than the paragraph above implies, and the reason is worth writing
down: the box is bounded by the **distance from the axis, not by the canvas** —
roughly `(2d + 2·reach) × (2·reach)` for a mirror at distance `d`. An axis near
the subject, which is what character design does, stays cheap; the cost grows
only as the artist works away from it. Measured at 6.4% of a 400×540 page
against 0.21% for an un-mirrored event. Carrying disjoint regions through route
selection, the compose ring and the tiled path is its own piece of work;
`PassSpec` already carries an `SKMatrix?`, so drawing the live overlay once per
copy at compose time is the likely shape of it.

**The live copies stamp settled dabs only, which was not foreseen here and is
better than what was.** The tail machinery in the live scratch exists because
provisional dabs move between pointer events. A settled dab never moves again,
so a copy of one needs no backup and no rollback — no per-copy state at all,
which is what leaves the hottest code in the application unchanged rather than
merely equivalent. The cost is one event of lag on the copies, and the commit
renders every copy exactly.

**And decision 3's reach turned out narrower than "everything through
`StampStroke`".** Brush and eraser have it. Fill reflects contours rather than
dabs; smudge and blur *read* pixels, which a canvas transform does not carry —
see the coordinate-space note on `ToSurface`. Those were excluded on the
reasoning above, but the reasoning was that they came free, and they do not.
Each needs its own pass and its own evidence.

**And the decision has a cost nobody named when it was made.** Reflecting the
canvas rather than the geometry is right, and it means every pass that draws
through that canvas using a rectangle computed in document space now gets its
rectangle transformed **twice** — once when it was computed for the copy, once by
the canvas. Granulation was exactly that and shipped broken: the mask landed
outside the copy's own scratch and Skia clipped it away without a word, so a
reflected copy kept the alpha the paper should have carved out of it. Texture and
wet edge escaped only because they happen to run after the transform is undone.

The lesson worth carrying: **reflecting the canvas moves the burden onto anything
that mixes canvas drawing with precomputed document coordinates.** When the next
pass joins that block — or when fill, smudge and blur are picked up — the
question to ask of each is which space its geometry is in and how many times the
canvas will transform it. `GranulationReachesAReflectedCopy` is the guard, and it
exists because every other symmetry test had set `Granulation` to 0.

**Blocks:** nothing. Symmetry can be built.
