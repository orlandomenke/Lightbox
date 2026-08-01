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

## Answered

_(nothing yet — answers move here with the date and the commit that
implemented them)_
