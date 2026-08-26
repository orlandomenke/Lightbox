# Q166 · How should a transform clip to a selection? — **answered 2026-08-26: through the record's own clip, as copy already does**

Raised by the owner, reporting the transform tool against five symptoms:
*"The transform tool should transform anything that is on the layer (or cells
but that isn't implemented yet) but on a selection it should only transform the
selected part."* — with freehand, circle and square appearing not to trigger a
transform at all, polygon and magic wand triggering one that took whatever was
on the canvas, and a later note that *"moving ink reveals erased lines"* and
*"during the transform move action it seems I am moving only parts of the actual
ink"*.

## What it blocks

B318–B320: the region-limited transform. Not the polygon preview work
(B315–B317), which is a separate objective and landed first.

## The diagnosis the question rests on

**A region-limited transform moves whole strokes chosen by a majority vote, and
clips nothing.** `TransformErasures.MovingWithin` takes a stroke when
`inside * 2 >= survivors`; `TransformFrame` then moves all of it.

Measured on a single stroke drawn from (100,200) to (500,200):

| Selection | What happens |
| --- | --- |
| Box over 100–200 | `BeginTransform` returns **false** — *"Nothing to transform in this scope."* |
| Box over 50–320, moved down 100 | Record reads `first=(100,300) last=(500,300)` — **both ends moved** |

That is the whole of the reported variant split, and it is not a variant split:
freehand, box, ellipse, polygon and wand converge on the same mask and the same
filter, with no branch between them. Freehand, circle and square are what an
artist reaches for to grab *part* of a drawing, so the majority test fails and
nothing happens; a generously drawn polygon and a wand click on a connected ink
region enclose whole strokes, so the majority test passes and everything moves.
The tool that looked broken and the tool that looked wrong were one rule seen
from two sides.

**The precedent that decides this.** Copy already solves it, in the same model,
through the same filter: `SelectedStrokesForAnOperation` gives each caught
stroke the selection as a `ClipId`, intersecting through `ClipMeeting` with any
clip the stroke already carried. The manual has documented that behaviour for
copy since it landed — *draw a box across half a line and the paste shows that
half; the line is not cut in two to do it*. Transform borrowed the filter and
never got the clip.

## The answer

| | What it costs |
| --- | --- |
| **Through the record's own clip** (recommended, **chosen**) | One extra stroke per stroke that *crosses* the boundary; nothing for strokes wholly inside, which keep moving whole. Reuses `ClipRegion`, `Stroke.ClipId` and `ClipMeeting` exactly as copy does, so the two surfaces cannot drift about what "the selected part" means. Re-renders deterministically — no dab walk is disturbed. |
| Split the strokes geometrically | Cleanest record afterwards, and it changes the mark. `Hash01` seeds every dab dynamic from position and cutting a polyline restarts the dab walk, so scatter, size, flow, roundness, rotation and all three colour jitters re-roll along both halves. Invariant 2's whole point, and it also breaks stroke identity for undo and for the AI paths. |
| Rasterize and move pixels | Photoshop's answer, and exactly WYSIWYG. The frame gains a raster baseline and the strokes stop being editable — against invariant 1, that the stroke record is the document. |
| Relax the vote and keep whole strokes | Cheapest by far and fixes only the first symptom. A stroke still moves whole, so it never delivers *"only the selected part"* — which is the sentence the request was made in. |

**The shape of the fix as built** (B319, B320):

- A stroke **wholly inside** the region moves whole, with no clip at all. The
  common case stays exactly as cheap and as clean as it is today.
- A stroke **crossing** the boundary is copied. The copy moves carrying the
  selection as its clip, transformed by the same matrix; the original stays put
  carrying the selection *subtracted*. Strokes that already have a clip
  intersect through `ClipMeeting`, for the reason it was written: a copy must
  never show ink the original was not showing.
- The filter becomes **any surviving ink inside**, not a majority. Once the
  inside part is what moves, a stroke with one point in the region has
  something to contribute, and the majority test is exactly what made a small
  selection do nothing.
- **And the filter reads the mark, not the vertices** — the half of the fix
  this question did not anticipate, found the moment the first test was run. A
  stroke records the points the pen reported; the ink between two of them is
  just as much on the canvas. A marquee dropped between two vertices found
  nothing, which is "the transform does not trigger" in its purest form.
  `RegionReading` samples along each segment at about a pixel a step, prepared
  once per frame so the walk does not go quadratic in the stroke count.

## The erasure half, which is not a preference

Not asked, because the doctrine already answers it and the answer is forced:
**erased ink must never come back.** `TransformFrame` handles one direction — a
moving erasure over staying ink leaves a stay copy — and not its mirror. Moving
ink whose erasure stays behind arrives un-carved, and the rubbed-out paint
reappears at the destination, which is the owner's *"moving ink reveals erased
lines"*.

`MovingWithin` makes it near-certain rather than incidental: it judges ink by
its **surviving** points and erasures by their **raw** points, so the two halves
of one rub are tested differently and eventually part company. The rule needed
is the existing one pointed the other way — an erasure that carved moving ink
travels with it as a copy, and the original stays to keep holding down what it
erased in place.

**As built, that needed no separate rule at all**, which is the outcome worth
recording: an erasure is a stroke, the region catches it by the same
mark-reading test as any other, and a crossing one is split by the same clip
pair. The part that travels keeps carving the ink it travels with; the part
left behind is still sitting on the ink it rubbed out there. The old stay-copy
special case survives only for an erasure *wholly* inside the region, which has
nothing left behind to do the holding.

**The first attempt at it was wrong, in the instructive way.** Clipping the
erasure's stayed half to "outside the selection", by symmetry with ink, takes
its carve off everything *inside* the selection that did not travel — and
wholly rubbed-out ink is exactly that. Two of B290's own tests caught it. An
erasure is duplicated, not divided: erasing twice is erasing once, so the
original stays untouched and only the travelling copy is clipped.

**Two things this question did not decide, decided by building it.** A stroke
wholly inside the region still moves whole with no clip and no copy, so the
common case gets no slower. And a gradient was left whole at first — see the
section below, where that was overturned.

## The gradient, asked separately and answered against the recommendation (B323)

Raised the same day, once the build had surfaced it: **should a marquee clip a
gradient the way it now clips a stroke?**

| | What it costs |
| --- | --- |
| Leave it whole (recommended) | Nothing, and it is what B7's special case already said: a gradient has no location — the ramp colours the layer wherever its two axis points sit — so "the part inside the selection" is not something the record can say apart from the ramp itself. Nothing in the five reported symptoms touched gradients. |
| **Clip it like a stroke** (**chosen**) | A marquee has to mean one thing everywhere, and once a stroke moves only what you boxed, a gradient that alone ignored the box is the odd one out. Photoshop moves the pixels you selected whatever laid them down. |

**The cost of the chosen answer, written here so it is not rediscovered as a
surprise:** a region-limited move on a layer carrying a background gradient now
leaves a **visible rectangle of shifted background**. Move a character across a
gradient sky and the sky inside the marquee travels with them. That is the
trade, and the owner took it knowingly.

Mechanically it is one line — `RegionReading.Crosses` judges a gradient by
whether any of the page is left *out* of the region, which is B7's own reading
pointed the other way. `Reaches` is untouched, so B7's promise that a marquee
over a visible gradient finds it holds exactly as before; only the shape of the
answer changed, from one entry to two carrying complementary clips.

Measured on `main` for the arrangement that shows it — a horizontal band across
a line with a vertical eraser through it, neither of the eraser's ends inside
the band — the rubbed-off paint came back at **alpha 255**. The same probe
reads **0** now.
