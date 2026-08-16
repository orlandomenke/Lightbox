# Q102 · When is an erasure worth recording? — **answered 2026-08-16: when it changed a pixel**

Asked at the start of **B236**, and answered by the owner in the same exchange:

> *"Erasing something that wasn't there to begin with should not be kept. Not as
> a stroke, not in the undo history. As it did nothing to nothing."*

That settles the *rule*. Three things it does not settle came with it, and all
three change what gets built rather than merely how, so they were prompted
together and are recorded here with what the rejected options would have cost.

## 1. How exactly is "it did nothing" measured?

| | What it costs |
| --- | --- |
| **Compare the pixels before and after** (recommended, **chosen**) | One copy of the region the stroke can reach, per erasing pen lift. |
| Scan for ink and stop at the first pixel | Free in the common case, and keeps a useless stroke whenever the bounding box merely clips a line the eraser never touched. |
| Ask whether the eraser's path crosses an earlier stroke | Cheapest, and blind to a frame's baseline. |

**The record-geometry option is the one to explain**, because it looks the most
in keeping with invariant 1 and is the only one that can lose work. A frame can
carry **baseline pixels** — an imported or flattened drawing that no stroke
accounts for — so an eraser that visibly rubbed out an imported line crosses no
stroke geometry at all. That test would have thrown the erasure away and left
the artist looking at paint they had removed reappearing on the next reload.
Wrong in the direction that destroys work, which is the one direction this must
never be wrong in.

**Between the two pixel options, exactness won on how people actually erase.**
The cheap scan only answers "was this whole area already blank", and cleaning up
between two lines is not that: there is ink inside the bounding box and the
eraser touches none of it. That is a common gesture, so the cheap version would
have left the ledger of stray strokes it was written to stop.

**The cost is bounded and narrow.** The copied region is the one
`CommitBounds` already repaints, so invariant 6 already sanctions work of that
size; it is paid once at the end of a gesture rather than per pointer event; and
no tool but an erasure pays it, because everything else *adds* paint and the
answer is known in advance.

`StrokeChangeProbe` returning null means *cannot tell* — no surface, or a region
too large to hold — and a null probe keeps the stroke. Deleting an artist's edit
on the strength of a measurement that did not happen is not a trade worth making.

## 2. Does a no-op erase take back the cel it keyed?

**Yes.** Erasing on a hold keys the cel *before* the stroke lands, as its own
undo step, so an erase that turns out to have done nothing would otherwise leave
a new drawing on the exposure sheet — the hold broken, the timing changed, by a
gesture that changed no pixels. That is a worse surprise than the stray stroke
this whole bug is about, and much harder to notice: a stray stroke is invisible,
a broken hold changes how the animation plays.

This needed a new primitive. `DocumentEditor.DiscardStep(revision)` rolls a step
back and removes it from the undo stack **without pushing a redo** — it is not an
undo, which is an artist's decision, but a caller admitting the step should never
have existed. It is guarded by revision rather than taking whatever is on top,
because anything that ran in between may have pushed a step of its own and
discarding that would be data loss.

## 3. Does the rule apply to clearing an empty selection?

**Yes — the rule is the act, not the tool.** Clearing a selection that held
nothing is erasing nothing with a box instead of a brush. Keeping it to the
Eraser tool would have been a smaller diff and would have left a
`ClearRegion` stroke that the Arrow cannot select (B232) and undo cannot
explain — two rules to remember where one will do.

## What this deliberately does not do

**A stroke that was fully erased is still in the record.** "Erased strokes do not
exist as far as the application is concerned" is about reach and visibility — no
tool can touch them (B232), no AI request describes them (B233) — not about
deleting them from the document. The pixels are derived from the record, and
undo has to be able to bring an erasure back, which is the one exception the
owner named. Compacting the record is a different question with a different risk
profile and nobody has asked it yet.

**No other tool is checked for having done nothing.** A brush stroke at zero
opacity, or one entirely outside its clip, also changes no pixels — and it is an
*addition*, so the artist meant to put something there and the empty result is
information rather than an accident. Erasures are singled out because "erase
nothing" is a gesture people make constantly while cleaning up, and because it is
the only one whose leftover record then behaves like an object.
