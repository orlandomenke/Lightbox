# Open questions

Decisions the loop could not make for you. Each one blocks something
specific; each can be answered in a line. Answer inline (edit the file) or in
chat — the loop reads this file at the start of every round and treats an
answered question as settled.

Questions are removed once implemented, with the decision recorded in
`LOOP.md`.

---

## Q1 · Smudge with no colour of its own

**Blocks:** the smudge/blur brush family (M15d).

A pure smudge pushes existing pixels around and deposits no paint. At the
very start of a stroke there is nothing picked up yet.

- **(a)** First dab picks up the colour under it, then drags — the stroke
  begins by smearing what is already there. *(recommended: matches Krita and
  Photoshop; predictable when starting on empty canvas — nothing happens)*
- **(b)** First dab does nothing visible until the pointer moves.

**Recommend (a).**

---

## Q2 · What a locked layer blocks

**Blocks:** layer lock (M15f).

- **(a)** Locking blocks every operation that changes pixels or geometry
  (paint, fill, transform, delete, blank) but still allows visibility,
  opacity, blend mode and reordering. *(recommended: matches Photoshop's
  "lock all"; keeps locking useful for reference layers)*
- **(b)** Locking freezes the layer entirely, including its position in the
  stack.

**Recommend (a).** Also assumed unless you say otherwise: locking a **group**
locks every layer inside it, and the brush cursor shows a blocked state over
a locked layer rather than silently doing nothing.

---

## Q3 · The default background layer

**Blocks:** default background layer + checkerboard (M15g).

When a new document is created with a paper colour rather than a transparent
background:

- **(a)** Add a locked `Background` layer filled with the paper colour, which
  can be unlocked and painted like any other layer. *(recommended: what an
  artist expects from Photoshop; makes "flatten onto white" obvious)*
- **(b)** Keep the paper colour as a scene property only, and add the locked
  layer solely for transparent documents.

**Recommend (a).** Follow-up assumed: the checkerboard shows wherever the
composed image is transparent, whether or not a background layer exists.

---

## Q4 · Cursor ring under pen pressure

**Blocks:** the true-thickness cursor (M16a).

Mouse input now paints at 100%, so the ring matches the stroke exactly. With
a pen, thickness varies during the stroke.

- **(a)** Ring shows maximum size while hovering, then tracks live pressure
  while the pen is down, with a setting to turn the live tracking off.
  *(recommended: you asked for "optionally", this is the option)*
- **(b)** Ring always shows maximum size; thickness is discovered by drawing.

**Recommend (a), defaulting the live tracking ON.**

---

## Q5 · What "animate on 2s" does to existing drawings

**Blocks:** range exposure editing (M16c).

Selecting a range of cels and re-exposing on 2s has two possible meanings:

- **(a)** Stretch: each existing drawing is held for 2 frames, so the range
  gets longer and no drawing is lost. *(recommended: this is what "animating
  on 2s" means to an animator)*
- **(b)** Thin: keep every second drawing and discard the rest, so the range
  keeps its length.

**Recommend (a)**, with (b) available later as a separate "reduce" command.

---

## Q6 · What a sampled smudge re-reads on reload

**Blocks:** Smudge (all layers) and Blur (all layers) — everything else
about them is plumbing.

A layer's rendered bitmap is currently a function of that layer alone, which
is what makes the frame cache simple and per-layer. A brush that samples the
whole composite breaks that: the result depends on the layers underneath.

- **(a)** Live. A sampled stroke re-samples whatever is beneath it at render
  time, so editing a lower layer updates the smudge above it. The cache key
  gains the backdrop's identity and invalidation cascades up the stack.
  *(recommended: it is what "sample all layers" means, it keeps the stroke
  record the single source of truth, and a reload always agrees with what is
  on screen)*
- **(b)** Baked. The sampled pixels are captured into the stroke when it is
  committed, so the mark never changes afterwards. Simple caching, but the
  document now carries pixel data that the record cannot regenerate — which
  cuts against "the stroke record is the document".

**Recommend (a).** It costs cache work; (b) costs an invariant.

---

## Q7 · How much of the Photoshop brush panel to take

**Blocks:** the brush-settings expansion (see `docs/design/brush-family.md`).

Tier 1 is five per-dab modulations — size jitter, angle/roundness jitter,
direction-following angle, dual brush, flow jitter. All are seeded from dab
position, so determinism is unaffected.

- **(a)** Tier 1 only, then reassess. *(recommended: it is the set that
  changes how a mark reads, and it is what `.abr` files most often carry
  that we currently drop)*
- **(b)** Tier 1 + Texture and Colour Dynamics from tier 2.
- **(c)** Something narrower — name the two or three you actually want.

**Recommend (a).**

---

## Q8 · Whether to rename the brush pages to match Photoshop

**Blocks:** nothing — but doing it later means moving controls twice.

Photoshop groups brush controls by dynamic (Shape Dynamics, Scattering,
Texture, Dual Brush, Transfer) rather than by parameter. Our Effects page
groups by parameter.

- **(a)** Adopt Photoshop's section names inside the Effects page.
  *(recommended: it is the point of aligning — an imported preset should be
  recognisable)*
- **(b)** Keep our own grouping and just add the new controls.

**Recommend (a).**

---

## Answered

_(nothing yet — answers move here with the date and the commit that
implemented them)_
