# Q62 · Playback is 7 fps at 1080p and 3 fps at 4K — which half gets fixed? — **answered 2026-08-08: compositing, and ahead of the vector work**

Asked after an artist reported playback as unusable at 1080p and 4K, and a new
bench scenario reproduced it. Filed as **B144**, with the measured split added to
**B29** and **B125**.

### What the measurement found, because it decided the question

`AnimationSweeps.PlaybackCanvasSize` — 3 layers, 24 frames, second pass round the
loop, so no first-pass rasterization is included:

| canvas | playback p50 | of which compositing | of which re-rasterizing | fps |
| --- | ---: | ---: | ---: | ---: |
| 720p | 24.3 ms | 13.4 | ~11 | 41 |
| 1080p | 132.6 ms | 55.7 | **~81** | 7 |
| 4K | 303.0 ms | 215.0 | ~100 | 3 |

The compositing column is `AnimationSweeps.CanvasSize`, which holds one frame and
three layers so every access is a cache hit — compositing with no rasterization in
it. Subtracting gives the rest.

**Two causes, and which dominates flips with resolution.** At 1080p the frame cache
is the majority (B144: a fixed 512 MB budget holds 64 of the 72 bitmaps the scene
needs). At 4K compositing is the majority (B29/B125: full-canvas CPU blits, `n^1.03`
in area, already 2.6× over the playback budget on pure cache hits).

### The answer, which went against the recommendation

**Go at compositing**, and **before the vector phases**.

The recommendation was the cache budget first: it is a literal replaced by a
reading of installed memory, it should take 1080p from 133 ms to about 56 ms — just
inside the 83 ms budget — and it could ship in an afternoon. It was not chosen, and
the reasoning against it is sound: **it buys one resolution.** 56 ms of an 83 ms
budget leaves no room for onion skin (`n^0.84`, and already 885% of budget at one
ghost each side), and 4K is untouched at 215 ms. A fix that makes 1080p *just*
work, while the number that actually scales with the document goes unaddressed,
spends the session and moves the ceiling by one step.

**What the choice costs, recorded because it is real.** Compositing is the largest
piece of work in the performance area — tiling un-gated for bounded documents plus
culling, or GPU compositing through a `GRContext`, and B125 exists because the CPU
path is a ceiling rather than a bug. It is multi-session. Until it lands, **1080p
playback stays at 7 fps** even though a one-line budget change would have made it
usable, and that is the trade being accepted knowingly: no half-fix, and the
interim is worse than it needed to be.

The cache half is not cancelled, only reordered — B144 stays open at P1, and it
gets cheaper to justify once compositing is not the dominant term.

### The measurement gap, which is the part to carry forward

`AnimationSweeps.Playback` has existed all along, times the identical operation,
and reported every row inside budget while the application ran at 7 fps on an
ordinary document. It sweeps **frame count at 720p** — the one axis playback is
nearly flat on (`n^0.83`) — and never varied the one it is quadratic in (`n^2.25`).

**A sweep is evidence about the axis it sweeps and about nothing else.** The bug
was reachable from the existing scenario's own numbers by nobody, because the
scenario asked the wrong question competently. When a report disagrees with a
person using the application, the report is measuring something adjacent.
