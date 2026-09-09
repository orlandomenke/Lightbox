# Q184 · How band scaling works inside the transform box

Raised by: the owner, 2026-09-10, from a real problem on a real drawing — *"I
drew the legs too short and want to scale the legs upwards but the general size
of the sketch is fine."*

What it blocks: the shape of the interaction, before any of it is built.

**Recommendation:** divider-drag, adjacent band absorbs, both axes, one-off.
All four were taken.

## Why it belongs at all, which was never in doubt

Two first-class purposes, and this serves both: fixing the proportions of a
single drawing worth finishing, and fixing a character's proportions across a
cycle — the latter for free, because the transform tool already carries scopes
for *this drawing*, *a cel range* and *the entire animation*.

It is also free in the record. Dividers exist only inside a transform session;
committing moves stroke points and writes **no new keys at all**. "Optional
means absent, not disabled" costs nothing to honour here, so there is no
`Assert.DoesNotContain` to write — there is no key to look for.

`TransformOps` already produces a `PointMap`, and `Perspective` proves the
pipeline carries non-affine maps, so a band scale is one more `PointMap`:
piecewise-linear along one axis.

## The four decisions

### 1. Drag the divider — not select-a-band-then-scale

**Answered: drag the divider.** Drag the hip line up and the torso shortens as
the legs lengthen; the outer box never moves. One gesture, no selection state,
no second set of handles inside the box.

The request described *both* this and a band-selection gesture (*"I select the
lower section"*), which is why it was asked rather than guessed. The cost of not
taking selection: there is no way to say "scale this band and let the far side
give up the space" in one action — with the box fixed and one divider moving,
only the two adjacent bands can change, which is what the next decision makes
explicit.

### 2. Only the adjacent band gives up the space

**Answered: the adjacent band.** For a single divider with the box fixed this is
what the geometry forces — bands further out are held by dividers that did not
move — so the answer is really about what happens with several dividers, and it
is: nothing you did not touch moves.

**What the alternatives cost.** *Proportionally across that side* feels smoother
with many bands and means one drag disturbs parts of the drawing you did not
touch, which is the opposite of the reason to have bands. *The outer box grows*
would lengthen the legs without compressing the torso — but it changes the
proportions of the whole sketch, which the request explicitly wanted preserved.

### 3. Both axes

**Answered: both.** The map is per-axis either way, so a vertical divider is the
same code with the coordinates swapped. Widening a shoulder is the same need as
lengthening a leg, and a tool that only works one way round is a tool somebody
has to work around.

### 4. A one-off edit, not a saved deformer

**Answered: one-off.** Commit moves the points and the dividers are gone,
exactly like every other transform. Nothing new has to survive a reload, an undo
or the inbetweener.

**What the alternative cost, and it is the one worth re-reading later:** a saved,
re-editable band layout would let you come back a week later and nudge the hip
line again. It was refused because it is a new document-level record that has to
survive reload, undo *and* the inbetweener — and because it starts overlapping
what rigs already do. If the need for re-editability turns up, **the honest
answer is a rig, not a second deformer system.**

## The part that was never a preference

**A point has to be inserted where a stroke crosses a divider.** The map is
piecewise-linear, so a diagonal crossing a divider gains a kink — and without an
inserted point the kink lands at the nearest existing sample instead of at the
divider, which reads as the line bending in the wrong place. Perspective has the
same latent issue and never made it visible; this does.

## The one thing that made the UI cheap

`ScenePassBuilder.PassSpec` already carries both `Source` (a source sub-rect of
the moving bitmap) and `Matrix`. Within a single band the map *is* affine, so the
live preview is **one pass per band** — each mapping its source rows to its
destination rows — and needs no change to the rendering code. One band is the
behaviour that exists today, which is the good sign that the generalisation is
the right one.
