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

## What building it added, after the answers

Four things the design did not anticipate, kept here because each is a decision
somebody will otherwise re-open.

**It is a third gizmo mode, not something the ordinary box also does.** Bands are
axis-aligned in document space, so a rotated box has no meaningful horizontal.
Composing a band map with a rotation is possible and would hand the artist two
gestures whose interaction nobody can predict — so band mode turns perspective
off and vice versa, enforced in both setters so neither entry point can leave
both on. The corner handles, the edge handles and the pivot are all inert in band
mode, and **no pivot is drawn**: a handle that does nothing is worse than no
handle, because it invites the drag it will ignore.

**Imported raster pixels do not move, and the status line says so.** A band scale
cannot be written as one affine matrix, so a raster baseline has nothing to be
resampled through. `CommitTransformCore` now skips the resample when the matrix is
identity — which also spares an ordinary identity commit a pointless PNG
re-encode — and `CommitTransformBands` counts the drawings in scope that carry
imported pixels and names them. Doing it silently would leave half a frame
stretched and half of it not, with nothing to explain why.

**Ctrl+Shift+B was already the reference board**, and
`Defaults_CoverTheCoreCommands_WithoutDuplicates` caught it on the first run. The
binding is `Ctrl+Shift+T` — "the other transform". That is the shortcut registry
earning its place exactly as `CLAUDE.md` claims it does; the collision would have
shipped as a dead key otherwise.

**The size ratchets shaped the code, and for the better.** `CanvasControl.cs` and
`MainWindow.axaml` are both at their budgets, and the first cut of this went 83
lines over on the former. The fix was the one the ratchet asks for: every band
gesture, the overlay record and the divider painting moved into
`CanvasControl.TransformBands.cs`, leaving one-line calls behind — a press hook,
a move hook, a release hook, an identity arm and a paint hook. Both files now sit
*under* their budgets. The XAML's one added line was paid for by formatting the
sibling Perspective toggle the same way, which also makes the pair consistent.

## Deliberately not done

**The MCP surface does not offer this.** A band scale is a document-level
capability, so `CLAUDE.md`'s registry table says an agent should probably be able
to reach it — but the MCP surface is charter gate G12 territory and needs the
ai-engineer / art-director pair on the diff. That is a separate objective with a
separate review, not a line to bolt onto this branch. Recorded here so the gap is
a decision rather than an oversight.
