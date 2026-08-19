# Q130 · Reaching the top of a sidebar strip when the header owns the pixels — **answered 2026-08-19: a slim band above the header, on the topmost slot**

Raised while building tab rearrangement (the branch that made a header drop able
to name a position in a strip). Not part of that objective, and found by reading
the drop arithmetic against the measured chrome rather than by dragging things
around.

**A panel cannot be dropped at the top of a sidebar strip.** The two bands
overlap and the wrong one wins:

| | Depth |
| --- | --- |
| The insert sliver at a panel's first edge (`DockZones.InsertBand`) | 18px |
| The header, measured on a realised docker | ~27px |

The header is tested first and deliberately wins outright — *"the tab strip is
the loudest join affordance there is"* — so the top sliver of the **first** panel
in a strip is unreachable, and index 0 with it. Every other position is fine: a
panel's bottom sliver gives the position after it, so the second slot down is
reached from the first one's lower edge. Only "above everything" has no route.
Nor does the edge fall through to `NearestEdge`: the panels fill the strip, so
the pointer is always over one of them.

What it blocks: nothing that is built. It is a hole in panel rearrangement that
has been there since the header became one target, and the artist-visible shape
of it is *"I can move a docker down but never up past the first one."*

| | What it costs |
| --- | --- |
| **A slim band above the header, on the topmost slot only** (recommended, **chosen**) | Takes ~6px off the top of one header's join target. Aims exactly where the gesture points — you drop above the first panel by aiming above the first panel — and no other header changes. The cost is a second rule inside `Beside` about which slot is first, which is a fact the arithmetic already has (`slot.Order`). |
| Reach it from the strip's outer edge | Takes no pixels from any header, and reuses the edge machinery that already opens an empty strip. But it is a target with nothing to see and nothing to guess from: an artist who wants a panel at the top has no reason to aim at the window's edge, so it is a route that has to be taught. |
| File it with a cost and decide later | Nothing changes today. The honest version of this needs a `BUGS.md` entry saying the top of a sidebar is unreachable by drag, because the alternative is that it stays unwritten and gets rediscovered. |

## What was decided

- **The band goes above the header, on the first slot of a strip only.** Not on
  every panel: a band above every header would take the pixels back from the
  join target the owner asked for, and every other insert position is already
  reachable from the neighbour above.
- **Its own branch**, after the tab-rearrangement branch lands — this is panel
  rearrangement rather than tab rearrangement, and the one-objective rule holds
  whichever direction the extra work points in.
