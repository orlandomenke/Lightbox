# Q60 · How does a painting stay cheap to reopen? — **answered: an in-document checkpoint, taken on save, off-thread**

**Answered 2026-08-08**, five decisions in two prompted pairs, after B30 was
measured against a painting rather than a frame of line art and turned out to be
27× over budget at the *smallest* sample anyone had tried. Design:
`docs/DESIGN-raster-checkpoint.md`.

The question only became askable once the numbers existed. B30 had sat at P3 for
weeks on the reasoning that *"a miss is currently rare on a scene that fits the
cache"* — true of one frame in a sequence, false for a single painting, where
there is one cel and the document *is* the frame you keep missing on. The
assumption was excluding one of the two first-class purposes in `CLAUDE.md`, and
nothing measured it because the sweep stopped at 800 strokes with a 9 px line-art
brush: the animation half's shape on both axes.

| | Decided | Instead of |
| --- | --- | --- |
| Where the pixels live | **In the document**, a new nullable field beside the strokes | a sidecar cache file — which was the recommendation until the prior art was read |
| When one is taken | **On save, rendered on a background thread** | before the save completes (would stall Ctrl+S for a minute), or on idle (needs an idle notion that does not exist) |
| What invalidates it | **Any edit it covers drops it**; next save makes a fresh one | several checkpoints at different depths — more of the fast path, much subtler invalidation |
| The undo limit | **A memory budget with a step ceiling, not a flat count** | a flat 250 or 500 |
| The clone stall found while measuring | **Filed as B142**, fixed on its own branch | folding it into B30, or fixing it here |

### The owner's constraints, which decided three of the five

> *"The goal is to not let anything block the artist's options in a reasonable
> sense (10000 undo steps is unreasonable) and to not interrupt the artist where
> it shouldn't or isn't expected (saving a document should not stall for too
> long)."*

Saving off-thread follows directly. So does refusing the multi-depth checkpoint:
it buys fast-path coverage with a failure mode that shows **stale art**, and being
slow is a lesser harm than being quietly wrong. And the undo answer was not a
pick at all —

> *"This should be tested what is the limit of fast and between usable. I would
> now say somewhere between 250 and 500."*

— so it was measured. **Depth turned out not to be the cost.** 500 delta steps
push in 1 ms and hold 433 KB; a brush stroke goes through `PerformDelta`, not
`Perform`. 500 *snapshots* would hold 1.4 GB. A step count therefore prices a
cost that is not there while missing the one that is, which is why the answer is
bytes with a step ceiling rather than a number in the middle of the range asked
about.

### Where the recommendation was wrong, and what changed it

The first recommendation was a **sidecar** cache, reasoning by analogy from Q55:
*"with nothing committed there is nothing to drift, nothing to merge and nothing
to verify"*. That argument is sound about a repository and weaker about a
document, and the prior art is close to unanimous the other way — a `.psd` has
carried a pre-composited flattened image beside its layer data since 1990, and a
`.kra` is a zip of per-layer images. Both chose portability over size. A document
that arrives on another machine without its checkpoint opens in 106 seconds, and
"it is slow on my colleague's laptop" is a worse bug than "the file is large".

The general shape, which is the part worth keeping: **every application that
offers geometry-as-truth restricts mark quality on those layers** — Krita's vector
layers do not get its brush engine, Illustrator rasterizes its expensive effects —
**and every application that keeps full mark quality stores the pixels.** Nobody
makes replay fast; they stop replaying. Lightbox is unusual only in not having
stored them yet.

### The one thing that was not a preference

Whichever way the questions went, `Frame.PngBase64` could not be the field. It is
read by `Materialize`, which draws it and *then* replays the strokes (the art would
paint twice); by `CanTileFrame`, which requires `!HasBaseline` (tiling would switch
itself off on exactly the documents needing it); and by `UnseenByTheModel`, which
reads it as *"imported pixels the model cannot see"* (a checkpoint is a rendering
of strokes the model reads fine). Derived state and content-with-provenance are the
same bytes with opposite meanings, and conflating them was a category error the
merge warning written that same morning is what made visible.
