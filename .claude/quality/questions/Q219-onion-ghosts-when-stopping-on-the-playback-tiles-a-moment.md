# Q219 · Onion ghosts when stopping on the playback tiles: a moment later, or with a short freeze — **answered 2026-10-08**

**Answered: the ghosts appear a moment later**, as recommended. The stopped frame
shows at once from the playback tiles (Q218), and its onion ghosts arrive with
the exact still picture once that is ready. The declined alternative drew them
at once, behind a freeze of about a second on the test document with onion on.

Raised by the lab A/B of phase 3 (2026-10-08). With phase 3 in, Stop still took
1.5–2.5 s in the real app, against 0 ms in the headless tests. The tests had
onion off and no Navigator open, and the owner's app has both:

- **The Navigator panel** composed its small preview of the stopped frame from
  full-size stills, one per layer: ten renders inside the stop publish. It needed
  no decision. It now keeps its picture while the stop holds the tiles, and is
  refreshed at the swap.
- **Onion ghosts** are bitmap passes, tinted per frame, and cannot come from the
  playback tiles. That one changes what is on screen, so it was asked.

## Implementation choices inside the answer

- While the hold lasts, the pass builder leaves the ghosts out
  (`State.GhostsLater`), exactly as playback does.
- The ghosts the still will draw are asked of `ScenePassBuilder.GhostSpecsFor`
  itself, not restated. Their images are warmed after the frame's own drawings,
  which also serves the next stroke. The swap waits for all of them, so it
  renders nothing on the UI thread either (`StopOnTilesTests`).
