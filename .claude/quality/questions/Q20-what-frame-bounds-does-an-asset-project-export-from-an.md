# Q20 · What frame bounds does an Asset project export from an unbounded canvas? — **answered (b), and the question was half wrong** — *superseded by Q71: the infinite canvas was removed 2026-08-12*

**Answered 2026-08-04.** Two corrections, and the second one dissolves most of it.

**The premise was too narrow.** This was framed as a game-animation problem, and
the tool is a drawing, painting *and* animation application. An infinite canvas
belongs to the **Shot** target — a world the camera frames, delivered as video —
not to the Asset one, where the canvas *is* the output. The game export pipeline
is already built and is not what this feature serves.

**And the conflict is not a project-type gate.** An unbounded canvas and a fixed
frame-bounds sprite export are **mutually exclusive by construction**, in every
project type — that is a fact about the two features, not about a manifest. So
nothing is gated: the pair is declared incompatible, the refusal names the fix,
and authoring an export region resolves it. Reach survives untouched, and
*Making reach unconditional* stands as written. Recorded as its own roadmap item.

So the answer is **(b), an authored export region** — arrived at from the other
direction than expected. It is not the rule for deriving bounds from an
unbounded canvas; it is the thing an artist authors to *make* the canvas bounded
where a bounded answer is required. (a) is still the right starting value for
that region — bounds-of-ink as a first guess, then draggable — but it cannot be
the mechanism, because a derived bound changes silently when a stray mark lands
in frame 40, and a game build cannot take that.

**Blocks:** nothing now. `docs/DESIGN-infinite-canvas.md` can be built against
this.

*The analysis below is what the answer was reached from, kept for the reasoning.
Its framing is the one the answer corrects: it treats the asset case as central.*

`CLAUDE.md` makes both of these first-class, and this is the one place they meet
head-on. **Assets** — "the canvas *is* the output. There is no camera, frame
bounds must stay consistent, and every frame is a deliverable." An **infinite
canvas** is defined by not having bounds. A sprite sheet is defined by having
consistent ones. So "the asset workflow loses" is not an available answer.

It cannot be answered from the code, because the code has never had to say what
the edge of a drawing is — `Scene.Width`/`Scene.Height` have always answered it
and an unbounded canvas removes the answer rather than changing it.

**(a) Bounds of ink, per scene.** Export the rectangle that encloses every
stroke in the sequence, identical for every frame. Needs nothing authored and it
is what an artist means by "the drawing". The risk is that it is *derived*: add
one stray mark in frame 40 and every previously exported frame changes size,
silently, which is exactly the kind of thing that breaks a game build.

**(b) An authored export region.** A rectangle the artist places once, saved with
the project. Stable by construction — the property assets need — and it makes
the bounds a thing you can see and drag rather than a consequence. Costs a UI
surface and one more thing to set up before the first export.

**(c) The camera, when one exists; ink otherwise.** Reuses machinery that is
already built, keyframed and exported. But `CLAUDE.md` says a camera is
shot-level machinery that must stay absent from asset work — this would make the
asset target depend on the one thing it was defined as not having.

**Recommend (b)**, on the grounds that consistency is the requirement rather
than convenience, and only (b) gives it by construction. (a) is the better
default *inside* (b) — an authored region that starts at the bounds of ink is
one click rather than a blank rectangle.
