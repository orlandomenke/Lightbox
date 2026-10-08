# Q223 · Paused canvas on tiles (phase 1): keep and fix, or close — **answered 2026-10-08**

**Answered: closed, and then find the 2.5 GB.** The owner was asked twice.

1. **The first time,** the owner chose to fix the jump and keep phase 1. That was
   against the recommendation, which was to hold it and measure where the 2.5 GB
   lives.
2. **The second time,** with the fix measured, the owner chose to close #632 and
   then measure where the process memory actually is. That was a variant of the
   recommendation, which was simply to close.

Raised by the lab (`perf/lab.py`), the first measurement of phase 1 of
`docs/DESIGN-one-picture-cache.md` in the real app. The local probes had
promised faster arrival at a frame with no cost at pen-down. The real app said
otherwise.

## What was measured

Both runs used the owner-shaped document, three interleaved runs per build, with
main at `1922aca2`.

**As first built:**

| | main | phase 1 |
|---|---|---|
| still cache at the end of a session | 612–620 MB | 252 MB |
| process memory (private) | 2.49–2.59 GB | 2.51–2.65 GB |
| jump to a frame, median response | 290 ms | 4,282 ms |
| total time the UI did not answer | 10.8 s (jump) | 32.1 s |

**With the fix:** a frame with no tiles yet takes the still route.

| | main | phase 1 |
|---|---|---|
| process memory (private) | 2.53–2.63 GB | 2.38–2.50 GB |
| pressing Play, median response | 200 ms | 2,652 ms |
| pressing Stop, median response | 3.6 s | 7.7 s |
| total time the UI did not answer | 14.3 s | 37.0 s |

## Why it did not hold

- **The probes were not the app.** In the probes the frame arrived at already had
  playback tiles. In the jump scenario it often did not, so the tiled route rendered
  them on the UI thread, beside a warm rendering the same frame's stills.
- **Dropping the stills of frames off screen** is what saved the memory, and it is
  also most likely what made Play, Stop and jumps re-render pictures that main had
  kept. That is a best explanation, not one isolated line by line.
- **The process figure barely moved** even when the still cache fell by 360 MB. It
  is not known how much of that the native allocator keeps, or what else grows into
  the space. Answering that is the next piece of work (the owner's second answer).

## Options as asked (second time)

- **Close #632 (recommended).** Keep main's behaviour; the overall limit (#621)
  already bounds the still cache.
- **Close it, then find the 2.5 GB (chosen).** As above, then measure the ~1.3 GB
  the memory report does not explain before any further memory work.
- **Keep iterating on phase 1.** Each round costs about 30 minutes of the owner's
  machine, for an upside measured at about 100 MB.

## If it is reopened

Start from the lab, not from a probe. A probe must reproduce the jump, Play and Stop
scenarios on a cold cache before it is trusted. Sharing one picture cache between
the paused canvas and playback is still the end state the playback note describes.
What failed was getting there by dropping stills.
