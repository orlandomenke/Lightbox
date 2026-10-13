# Q260 · What a held drawing becomes after a save and reopen, or an undo — **answered 2026-10-13**

Raised 2026-10-13 from the sensitivity review of B463, and asked with the
prompt once a test had shown the failure on main: an effect baked on 2s stores
one drawing shared by two cels — a hold. The file writes that drawing out at
each cel, and the reader, like undo's copy of a document, handed back one
drawing per cel, all with the same id. The app finds a drawing by its id and
takes the first, so a stroke drawn on the second cel landed on the first
(`AStrokeDrawnOnTheSecondCelOfAReopenedHoldLandsOnTheHold`, red on main:
counts (2, 1) where (1, 2) was drawn).

What it blocks: B467.

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **What a reopened hold becomes** | **One shared hold again** (recommended): on every read, cels holding drawings with the same id and identical content are joined into one drawing; a copy of the document keeps a shared drawing shared | **Separate drawings**: a repeated id is replaced with a fresh one on load and after undo, so the cels become independent keys — simplest to reason about, but a baked hold stops being a hold the first time it is saved, and undo would hand out new ids that undo's own later steps refer to. **Only fix the lookups**: find a drawing by its cel rather than its id in the places that take the first match — the smallest change to the file and undo, at four or more call sites, and the next lookup added by id has the same bug. |

**Recommendation:** as marked, and the owner took it.

## What follows from it, not up for preference

- **Nothing is joined that was not already identical, and already one
  layer's hold.** Two drawings that share an id and differ, the same drawing
  on two different layers, and a layer's mask that matches a drawing — which
  only a bug or a hand-edited file makes — are separate drawings: the later
  one is kept and given an id of its own. Joining by id alone would throw one
  away; joining across layers would tie them together. (Narrowed after the
  sensitivity review: the first version joined across layers and masks.)
- **A hold that has to be renamed is renamed as one.** If a hold collides
  with an earlier drawing's id, every cel of it gets the same new id and stays
  one drawing.
- **It stands on B463.** A resize used to scale a drawing once for every cel
  holding it; with holds now surviving a save, that would shrink every saved
  hold twice over. B467 merges after B463, never before
  (`ResizingAReopenedHoldScalesItOnce`).
- **A stroke drawn on a hold shows on every cel of it**, as it already did in
  the session that baked it. That is what a hold is.
- **A document with no holds reads back exactly as it was written**
  (`ADocumentWithNoHoldsComesBackExactlyAsItWent`).
