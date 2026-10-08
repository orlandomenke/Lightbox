# Q216 · Batch transform across frames: what animation-aware adds, holds, and range — **answered 2026-10-08**

Raised by: the owner, asking for "the first animation-aware tool: transforming
all cels in a layer at once". Roadmap Pillar 4's *Batch transform across
frames* had sat at `[?]` since Q98 called it "the quickest win but a leaf".

What it blocks: both branches below — what "animation-aware" means decides
whether this is polish on something that exists or a new operation.

**What was already true, and is the reason the question was needed.** A
constant transform over every drawing on a layer has shipped for a long time:
`TransformScope.ActiveLayerAllFrames`, reachable from the Scope combo and from
the Move tool's Ctrl-drag. The box is the union of every drawing in scope and a
held drawing moves once. It was hard to find (the combo showed raw enum names,
the manual's Scope list left it out) and unreliable (B403: a Ctrl-drag left the
setting on the whole layer). So "build it" was not the request; "make it a tool,
and say what animation-aware adds on top" was.

**1. What the animation-aware version adds — the owner wants several, in order.**
Recommended *a ramp over time* (the transform builds from nothing on the first
drawing to the full amount on the last, eased), as the one thing nothing else in
the app does. The owner agreed it is wanted, and said the request that prompted
this was narrower: **resize a character on every frame at once**, because doing
it one drawing at a time is too slow. For that the right pivot is **one shared
pivot**, not each drawing's own: a shared pivot scales the drawing *and its
motion* together, where per-drawing pivots grow the character while the jump
keeps its old height and the feet slide off the ground line. Per-drawing pivots
were declined for this reason; pegs (non-destructive, keyframed) were declined as
the much larger build they are — they remain their own roadmap item.

So, two branches:

- **First — the constant transform made first-class** (owner picked all four):
  B403 fixed on its own branch; readable scope names and a manual that lists all
  five; its own shortcut and Edit ▸ Transform entry, registered in `ShortcutMap`;
  the status line saying how many drawings it will change; and onion-skin ghosts
  of in-scope frames following the drag, so the whole cycle can be judged before
  Enter (the one item that touches compositing, so it gets a perf review).
- **Second — the ramp.**

**2. Holds under a ramp — keep them** (recommended, accepted). Each drawing is
transformed by its first exposure, so a cycle on 2s still moves on 2s. Declined:
splitting holds into keys (smooth on 1s, but doubles the drawings and changes the
timing the artist chose) and a per-session option (one more control on a full
page).

**3. The range a ramp runs over — the marked cels, else the whole layer**
(recommended, accepted). The gizmo sets the end state; an ease picker shapes the
ramp. Declined: whole layer only, which cannot travel over part of a shot
without splitting the layer.
