# Q218 · Stopping playback: instant on the playback tiles at every zoom, or a brief exact pause zoomed out — **answered 2026-10-08**

**Answered: instant at every zoom.** After Stop, the stopped frame stays on the
playback tiles until the still canvas's own images of it are ready, then swaps.
The owner chose this **against the recommendation**, which was a brief pause
(an estimated 0.3–0.5 s) for exact pixels when zoomed out further than 50%.

Raised by phase 3 of `docs/DESIGN-playback-first-loop.md`. Stop froze the app for
5.7–8.8 s on the owner-shaped document (lab, 2026-10-08), because playback and
the paused canvas keep separate caches.

## What was found before asking

The design note set a gate: the held tiles and the still must be pixel-identical,
or that is a finding to resolve first. The codebase already said why they might
not be. The tiled route composes from the mip level nearest the screen, the still
route scales the full-size bitmap, and `ScrubTileModeTests` records the rule *"a
tiled still must never be left on screen"* for that reason.

Measured on the same frame, onion off:

| zoom | pixel values that differ |
|---|---|
| 100%, 75%, 60%, 50% | **0** (identical) |
| 33% | 2.8%, at most 60/255, at stroke edges |
| 25% | 2.0%, at most 64/255, at stroke edges |

So the question was only about views zoomed out beyond 50%.

## The first ask, and why it was asked again

The first prompt was in implementation terms ("tiled still", "resamples"), and
the owner could not judge it. Their answer, *"at all zoom levels the playback
should be smooth"*, was about playback, which is smooth at every zoom whatever
this decides. It was re-asked in terms of what is on screen at the moment of
Stop, with the measured difference.

## What it costs

- **Zoomed out beyond 50%:** for the moment until the stills arrive (they are
  first in the idle warm's queue), about 2–3% of pixel values at stroke edges
  differ by up to a quarter of their range, then settle into the exact still.
  At that zoom the frame is a third of its size or smaller on screen.
- **It is an exception to "never leave a tiled still on screen"**, and is
  written down as one where the rule is enforced (`MainViewModel.Rendering.cs`,
  `_holdTilesAfterStop`).
- **No hold without a way to deliver the stills** (`ThumbnailWorker.Post`),
  because nothing would ever swap.
