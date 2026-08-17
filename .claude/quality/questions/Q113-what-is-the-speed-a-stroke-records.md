# Q113 · What is the speed a stroke records? — **answered 2026-08-17: a normalized, smoothed 0..1 value, captured once**

Raised by Q112's decision to record speed at all: *speed of what, measured
how, stored as what?* `docs/DESIGN-pen-dynamics.md` §Capture has the mechanism.

The constraint that settles it was checked against the shipped assembly rather
than assumed: **Avalonia 12.1.1 carries no per-point timestamps.**
`PointerEventArgs.Timestamp` exists per *event*, but the coalesced points
`GetIntermediatePoints` returns have position, pressure and tilt only — so a
"store raw timestamps" design would be storing interpolated fakes for half its
values on day one.

| | What it costs |
| --- | --- |
| **Normalized smoothed speed per point**, computed at capture from screen-space velocity (recommended, **chosen**) | The estimator's choices (smoothing constant, reference speed) are frozen into the stored values. Improving the estimator later changes only *new* strokes — which is invariant 4's rule applied to time, and is the point rather than the price. |
| **Per-point timestamps, derive speed at render** | Keeps the raw truth and lets the mapping evolve — but the raw truth is not available per point (above), the key is written even when nothing drives on speed, and every improvement to the derivation repaints existing art, which invariant 4 exists to forbid. |

**Screen-space, not document-space.** The artist's hand is the thing being
measured: at 400% zoom the same flick covers a quarter of the document distance,
and a speed that changed with the zoom would make the same gesture a different
mark — the exact complaint invariant 5 exists to prevent, arriving through a
side door.

**Deterministic forever after capture.** Render, undo, reload and the
inbetweener read the stored number; no clock exists anywhere in the render path
(invariant 2). The inbetweener interpolates speed between matched points the
same way it interpolates pressure.

**Left to implementation, on purpose:** the reference speed that maps to 1.0
and the smoothing constant. They are capture-time constants with no
back-catalogue to honour, so they should be picked by drawing with a pen and
looking, not decided here — the design doc records the starting values to try.
