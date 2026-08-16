# Q50 · What does an artist see on entering edit mode on a hand-drawn line? — **answered: fitted, and it says so**

**Answered 2026-08-07: fit a path and report the count** — "412 points → 12
nodes" — with one undo restoring every original point.

A drawn line has a point every few pixels. Showing all of them is technically
lossless and practically unusable: hundreds of nodes a few pixels apart, where
dragging one moves nothing. Fitting is what Illustrator's Image Trace and CSP's
Simplify both do, and Schneider's least-squares cubic fit is the standard.

**What it costs, and it is the reason this was asked rather than assumed:** the
line moves slightly. A fitted curve is not the wobble you drew. That is
acceptable only because it is *said out loud* and is one keystroke from being
undone — a silent fit would be the app quietly redrawing your work.

Rejected: showing every point (unusable), and asking each time (a dialog in front
of a gesture made hundreds of times, answered the same way every time). A detail
slider was offered and not taken; it stays available later as a tool option.

**Blocks:** nothing.
