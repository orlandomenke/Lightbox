# Q10 · Does wet paint survive between strokes — **answered (c), not yet buildable**

**Answered 2026-08-02: (c), a bounded wet window, with the size of the window a
brush setting.** `0` means the paint is dry the moment the pen lifts — exactly
today's behaviour — and `N` means the next `N` strokes can still pick it up.

Kept here rather than moved to `DECISIONS.md` because the decision is settled and
the *implementation is not startable*: `MediumSimulator` is a static pure
function of (coverage, existing pixels, paper, settings) that builds its
lattice per stroke and discards it. There is no state between strokes for a
window to bound. Adding the setting now would put a control in the brush
options that changes nothing, which charter **O7** exists to stop.

What the answer already constrains, so the fluid pass does not have to
re-litigate it:

- **The window size is stored per stroke** (invariant 4), not read from the
  tool at render time. Changing your brush must never re-wet a painting you
  finished last month.
- **Default 0 keeps every existing document byte-identical.** Absent by
  default, the camera's rule again.
- **A stroke's render depends on the previous N strokes**, which is the real
  cost. Re-rendering a frame already replays in order, so that part is free —
  but editing or undoing a stroke in the middle now invalidates the *next* N
  as well, and the frame cache and invariant 6 have to know it. Bounded by N
  rather than by the whole history is precisely why (c) was chosen over (b).

---
