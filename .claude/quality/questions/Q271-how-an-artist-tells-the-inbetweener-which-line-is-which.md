# Q271 · How an artist tells the inbetweener which line is which — **answered 2026-10-14**

**Answered: on the canvas, click a line of key A, then the line or lines of key B
it becomes; the strokes are cut at junctions for matching only; the matches are
kept with the key pair; a part hidden in one key fades, for now.** Asked with
`AskUserQuestion`. The interaction was answered as recommended; the other three
were answered "no preference", and the recommendation stands for each.

Raised by the owner on 2026-10-14, after Q270 settled that the artist matches
and the engine draws, and after this was set as the goal of the whole feature:

> "It was always meant as helpers. But one of the mainline feature was
> inbetweening; saving the tedium while keeping the art style and expression.
> With limited manual work to improve or change the inbetweens or breakdowns"

The order chosen at the same time: **matching first**, then arcs (each part turns
about its joint instead of sliding in a straight line), then breakdowns that
steer the frames around them, then AI helpers. Arcs are to be judged on the
owner's hand-drawn legs ("legs and feet sketch", "AnimSketch" in the AI test
file).

What it blocks: the correction tool for an inbetween the automatic matcher gets
wrong — the "limited manual work" half of the goal.

## What was asked, and what was answered

**1. How lines are matched.** *On the canvas, click A then B* (recommended):
**chosen**. A Match lines mode on the inbetween: key A and key B onion-skinned in
two colours with the current inbetween faint; click a line of A, then the line or
lines of B it becomes; the pair takes a shared colour and the inbetweens redraw
at once; click a matched line to unmatch; Esc leaves. Against: *a side-by-side
window* (clearer when the keys overlap, but off the canvas and a window to
maintain) and *drag from A to B* (one gesture per pair, but a line that is two in
B needs two drags, and a drag misses more easily than a click on a crowded
drawing).

**2. Picking one part of a stroke that runs across several.** *Cut at junctions
automatically* (recommended): **no preference, so chosen**. In the mode each
stroke is shown cut where another line ends against it and at sharp corners; the
pieces exist for matching only and the strokes are not changed. Against: *cut by
hand first*, which changes the drawing and adds steps on every traced key.

**3. Where matches are kept.** *Saved with the key pair* (recommended): **no
preference, so chosen**. On key A for the gap to the next key, like a timing
chart; reused when a key is tweaked and the gap re-inbetweened; absent from the
file until a match is made. Against: *only for this inbetween*, redone every time.

**4. A part hidden in one key.** *Fades, for now* (recommended): **no preference,
so chosen**. Travelling with its neighbours and going out of view needs to know
what is in front, and is a follow-up. Against: *the artist marks where it goes*,
one more interaction now.

## What it costs

- The canvas mode, its picking and its colours; the record on the key; the
  inbetween commands reading it; the manual; ShortcutMap for the mode.
- A cut is a property of the drawing, not of the match: if a key is redrawn the
  cuts move, and a match naming a piece that no longer exists is dropped rather
  than guessed at.

## Solid objects — asked the same day, after seeing the result

Shown the matched traced legs, the owner: "the legs are fine. Good enough to
finish or improve where needed. But the cuffs and shoes are just aweful", and
then: "shouldn't items be grouped an be considered as one object? like shoes.
That could be an artist role". Two questions followed with `AskUserQuestion`:

**5. How a match is marked as one solid object.** *Automatic, override by hand*:
**chosen**, against the recommendation (a toggle on the match). A match of many
small lines in a small area is solid unless the artist says otherwise; the
artist can flip any match either way. Against: *a toggle on the match* (nothing
solid until marked) and *a role on the strokes* (set once per layer, but it
belongs to the drawing rather than to the match).

**6. Which drawing a solid object shows.** *The nearest key's* (recommended):
**chosen**, after the first wording was not understood and it was asked again
plainly. Inbetweens near key A move key A's shoe into place, those near key B
key B's; the switch is at halfway, where the two are most alike. Against:
*always key A's*, which can look unlike key B's shoe on the frame before it.
