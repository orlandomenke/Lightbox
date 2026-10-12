# Q248 · Refresh-paced playback clock: land alone or with prepared frames — **answered: with prepared frames**

**Answered 2026-10-12: hold it, and land it with prepared frames.** The
refresh-paced clock stays on its branch; prepared composites (playback phase 4b)
are built on top of it, and the two land together once the large document's
first lap is no worse than with the timer.

Raised 2026-10-12 by the lab (play-steady with play.pacing, 3 pairs each,
against main with the 1 ms timer):

| Dropped frames per play | timer clock | refresh-paced |
|---|---|---|
| owner-shape, second play (6 s) | 27–30 | 1–10 |
| owner-shape, first play (15 s) | 75–79 | 34–41 |
| large 30-layer, first play (15 s) | 65–95 | **79–145** |
| large 30-layer, second play (6 s) | 15–19 | 9–20 |

Mean lateness of a tick fell from about 23 ms to 5–10 ms. Why the large document's
first lap got worse: the refresh-paced clock assembles the frame inside the
screen's refresh, so a slow frame — drawings still rendering on the first lap —
holds the screen itself, and fewer refreshes happen. The timer kept that work
beside the render loop. Paced by the refresh, a cheap tick is far steadier and an
expensive one is worse; prepared frames make every tick cheap.

What it blocks: nothing merged; this branch waits for phase 4b.

**Recommendation was the chosen one.** The alternatives:
- Merge now: owner-shape-sized documents play far steadier at once; the 30-layer
  document's first lap stutters more until prepared frames land.
- A hybrid that paces by the refresh only when the due frame is ready and by the
  timer otherwise: both wins now, at the cost of two clock paths to keep correct,
  and probably unnecessary once frames are prepared ahead.
