# Q222 · Smaller points in memory, now that undo shares drawings — **answered 2026-10-08**

**Answered: skip it, and go after pictures.** This goes against the
recommendation, which was to trim the lists' spare room only. Points stay as
they are. The next memory work is in the picture caches.

## Why it was asked again

*Smaller points in memory* was one of the four directions the owner picked in
Q221's prompt, at a time when every structural edit kept a whole copy of the
document. Each point byte was then multiplied by up to 64 undo steps. Undo
sharing (#626) removed that multiplier, so the case had to be measured again
before the work was done. These figures are from the lab's owner-shaped
fixture, loaded on main at `ecfa4652`:

| | |
|---|---|
| the document on the heap | 32 MB |
| points as stored (189,004 × 72 B) | 13.0 MB |
| points as allocated | 22.1 MB, because the lists keep about 70% spare capacity from growing while loading and drawing |
| the process at the end of a session (#620) | about 3.1 GB, nearly all of it pictures |

## Options as asked

- **Trim the spare room only (recommended).** About 9 MB, or 28% of the
  document. No value changes and no file changes, in a few places.
- **Trim and repack the point.** About 4 MB more on this document: the optional
  pen axes stored without nullable padding, 72 B → 48 B a point, values
  bit-identical. `StrokePoint` is used across the whole codebase, so it is a
  wide change for a small gain.
- **Skip it and go after pictures (chosen).** The gigabytes are in the picture
  caches. The next lever there is still images stored sparsely, which the owner
  had parked.

## If it is reopened

The trimming is still the cheap half. A long tablet scene has every pen axis
present, so it is where repacking would pay most; measure such a document
before deciding again.
