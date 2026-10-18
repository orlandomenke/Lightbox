# Q286 · How do a referenced document's frames line up when the frame rates differ? — **answered 2026-10-16: frame for frame**

Raised alongside Q284. The run is 24 fps and the jump 11 fps, in the same
character folder; referencing one from the other has to say which drawing
lands under which frame.

| | What it costs |
| --- | --- |
| **Frame for frame** (recommended, **chosen**) | Source drawing 1 under frame 1, drawing 2 under frame 2 — the rule every animation reference already follows. Predictable; ignores the difference in timing. |
| **By time** | Whichever source drawing is on screen at the same moment. Correct in seconds, but at 11 fps against 24 it skips more than half the run's drawings — the ones you wanted to draw against. |

**Why:** a reference is something to draw *against*, one drawing at a time, and
the Reference panel's grid, scale and per-frame alignment already assume one
cell per frame. Matching a shot in seconds is the job of *Follow timeline
edits* off, not of the import.
