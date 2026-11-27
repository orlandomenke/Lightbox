# Q294 · Line cleaner: which frames does it touch? — **answered 2026-11-19: chosen frames, plus an opt-in auto-clean of new inbetweens**

Raised by: the same request as Q292. The invariant at stake is that the
artist's drawing is never changed unasked.

What it blocks: the default target of each mode, and whether anything runs
without a command.

| | What it costs |
| --- | --- |
| Artist chooses; keys never by default (recommended) | Per-frame clean runs on the frames selected; guided clean defaults to Inbetween-marked frames and skips Key/Breakdown frames unless picked. Nothing runs by itself. |
| **Also auto-clean new inbetweens** (**chosen**) | As above, plus an opt-in setting that cleans each generated inbetween right after it is inserted, in the same undo step as the insert. |

The owner went past the recommendation: generated inbetweens are what the
cleaner exists to repair, so cleaning them as they arrive is wanted. The opt-in
reaches only frames the inbetweener just wrote — never a Key or Breakdown
frame, and never a frame the artist drew.
