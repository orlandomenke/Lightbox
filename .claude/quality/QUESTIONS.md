# Open questions

Decisions the loop could not make for you. Each one blocks something
specific; each can be answered in a line. Answer inline (edit the file) or in
chat — the loop reads this file at the start of every round and treats an
answered question as settled.

Questions are removed once implemented, with the decision recorded in
`LOOP.md`.

---

## Q11 · What a "reusable animation preset" would be that a cycle symbol is not

**Blocks:** the last `[?]` but one in Pillar 3.

The pillar lists *Reusable animation presets* and *Animation templates* as
separate from the Animation library — but the Animation library shipped, and
what it delivers is a multi-frame symbol placed with a frame offset, which is
already a reusable animation. Two placements of one cycle run the same drawings
out of step. Whatever these two items are for, it is not that.

The reading that survives is that they are about **timing rather than
drawings** — the part of frame-by-frame work that a symbol does not carry:

- **(a)** *Strike it.* The Animation library is the reusable animation, and
  these two lines are a pre-implementation guess that the design outgrew. A
  roadmap that keeps items nothing can distinguish from shipped ones is the
  wish list this file's checkbox rules exist to prevent.
- **(b)** *A timing preset* — a saved exposure pattern (on 1s, on 2s, a
  slow-in of 1-1-2-3-4) applied to a selected range of cels, re-exposing the
  drawings that are already there. This is a real animator's tool, it is
  genuinely absent, and it is nothing a symbol can express, because a symbol
  carries drawings and this carries their spacing.
- **(c)** *A motion preset* — keyframed placement transforms, so a symbol can
  be told to arc across the frame over twelve cels. This is the largest of the
  three and it needs a decision about whether placements become animatable at
  all, which is a pillar-4 question wearing a pillar-3 hat.

**Recommend (b), and strike the other line as (a).** One item, specified:
*"Timing presets — save an exposure pattern and apply it to a range of cels."*
It is the only one of the three that is both absent and unambiguous.

## Q12 · Whether an animation template is a document or a project type

**Blocks:** the last `[?]` in Pillar 3.

*Animation templates* — starting a new animation from a skeleton rather than an
empty document — is real and absent. What is undecided is where it lives, and
the app already has two mechanisms that overlap it: `NewDocumentSettings`
(size, fps, frame count) and project types (which decide the workspace).

- **(a)** *A document in the project marked as a template.* Copy it, rename it,
  start drawing. Costs nothing new — a template is an ordinary animation with a
  flag — and an artist can make one out of work they have already done, which
  is where real templates come from.
- **(b)** *A built-in list* (walk cycle 8 on 2s, run cycle 6, blink 4, take 12).
  Better on day one, worthless on day two: every studio times its own walk
  differently, and a list nobody can add to becomes a list nobody uses.
- **(c)** *Both* — built-ins that are seeded as project documents on first use,
  so they are editable from the moment they appear.

**Recommend (a).** It is the smallest thing that is not a guess about how other
people animate, and (c) is (a) plus a starter pack, which can be added later
without changing the mechanism.

---

## Q10 · Does wet paint survive between strokes — **answered (c), not yet buildable**

**Answered 2026-08-02: (c), a bounded wet window, with the size of the window a
brush setting.** `0` means the paint is dry the moment the pen lifts — exactly
today's behaviour — and `N` means the next `N` strokes can still pick it up.

Kept here rather than moved to `LOOP.md` because the decision is settled and
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

## Q13 · What counts as the same sheet of paper, for wet paint

**Blocks:** the fluid pass. `docs/DESIGN-fluid-media.md` names it; Q10's answer
does not settle it.

Q10 gives wet paint a window of N strokes. It does not say what a "stroke
before this one" means when the drawing changed in between.

- **Frames.** A cel is a separate drawing. Paint frame 1, move to frame 2, and
  frame 2's first mark almost certainly should not pick up frame 1's wet paint
  — they are different sheets. But a **held cel** is one drawing exposed at
  several indices, and scrubbing away and back returns you to paint you did
  leave wet.
- **Layers.** Wetness is a property of paper and a layer is not paper. Q6
  already decided the analogous thing for smudge — `ThisLayer` is the default —
  and the same reasoning applies.
- **Generated strokes.** The inbetweener writes strokes into frames nobody was
  painting on. If those participate in a window, an inbetween's appearance
  depends on how many strokes the generator happened to emit before it.

- **(a)** *The window is per frame and per layer.* Wet paint never crosses
  either. Simplest, matches Q6, and makes the replay trivially bounded: the
  strokes before this one on this layer of this frame.
- **(b)** *Per frame, across layers.* Closer to a physical sheet, but it makes a
  layer's render depend on other layers, which is the coupling the frame cache
  is built to avoid — and Q6 already found that expensive.
- **(c)** *Per frame and layer, and generated strokes never carry wetness* —
  (a), plus the inbetweener and the MCP surface always write `WetStrokes = 0`
  regardless of what the source stroke said.

**Recommend (c).** (a) is right about scope; the extra clause is because an
inbetween whose look depends on emission order is a determinism problem wearing
a different hat, and invariant 2's whole point is that generated frames must
not diverge.

## Q14 · What an eraser does to wet paint

**Blocks:** the fluid pass, and only the fluid pass.

Dragging an eraser through a wet mark is physically a different act from
erasing a dry one — you push the paint about as much as you remove it. The
engine has no opinion yet, and three are available:

- **(a)** *An eraser is a stroke like any other.* It spends one of the window's
  N and removes pigment; moisture goes with the pigment it belonged to. Cheap,
  and it keeps "a stroke is a stroke" true, which is worth something.
- **(b)** *An eraser is invisible to the window.* It removes pigment and does
  not count against N, so wet paint either side of an erase stays wet for the
  same number of real marks. Matches the intuition that wiping something out is
  not painting.
- **(c)** *An eraser smears.* It reads the moisture it passes through and drags
  it, like a smudge that also subtracts — the physical answer, and the most
  work by a distance.

**Recommend (a) to start.** It is the one that needs no new machinery, and (c)
is a brush someone could build later on top of the advection loop rather than
a property the eraser has to have from the beginning. Worth asking rather than
assuming, because an artist who erases into a wash and expects it to feather
will find (a) hard-edged and wrong.
