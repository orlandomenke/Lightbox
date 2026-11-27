# Q292 · Line cleaner: preview or apply directly? — **answered 2026-11-19: preview, then apply**

Raised by: the owner's request for a line cleaner — "it does not change what is
on canvas in a drastic measure … it cleans up the line", per frame or guided by
the hand-drawn frames around it, to repair what the inbetweener leaves behind
(kinks at knees, orphan marks, doubled sketch lines converging).

What it blocks: whether the cleaner needs a preview surface at all.

| | What it costs |
| --- | --- |
| **Preview, then apply** (recommended, **chosen**) | The cleaned lines are shown over the drawing with what changed marked and counted; Apply writes one undo step, Cancel leaves the record untouched. Needs a preview overlay. |
| Apply directly, undo to revert | Cheapest; the artist judges it by flipping undo and redo. |

The record is never changed until Apply — the cleaner computes a proposal, and
only the proposal is shown.
