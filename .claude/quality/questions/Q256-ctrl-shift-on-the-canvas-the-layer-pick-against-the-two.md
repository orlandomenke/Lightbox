# Q256 · Ctrl+Shift on the canvas: the layer pick against the two drags that already used it — **answered 2026-10-13: a click picks, a drag moves**

Raised by: the layer pick the owner asked for on 2026-10-12 — *"a quick find from
canvas to layer; ctrl + shift + click to select the layer from the canvas"* —
and the adversary's review of its first cut, which found the keys were not free.

What it blocked: where the pick may take a press. The first cut took every
Ctrl+Shift press, and two existing gestures went with it, silently:

- **The Move tool.** Ctrl drags the whole layer and Shift holds the drag to one
  axis, so Ctrl+Shift+drag is an axis-locked whole-layer move.
- **Inside a marquee, with any tool.** Ctrl takes hold of what the selection
  holds; Shift holds that to one axis as well.

Neither combination is in the manual or under a test, which is how it was
missed: each key is documented on its own.

Asked with `AskUserQuestion`.

| | What it costs |
| --- | --- |
| **Keep both old gestures** (recommended) | The pick works with every tool but Move, and not inside a marquee. Nothing stops working; finding a layer with the Move tool in hand means the docker. |
| **The pick wins everywhere** | One rule. Both axis-locked moves can no longer be started with both keys down: Ctrl, start dragging, then Shift. |
| **A click picks, a drag moves** (**chosen**) | Both survive. The pick is decided on release in those two places, and the line between a click and a drag has to be drawn somewhere. |

**Decided against the recommendation.** The owner wants the pick with the Move
tool as well, and the drags kept.

## How the line is drawn

**Three screen pixels**, the same threshold that already tells a line's click
from its drag (`LineDragMinimumPixels`). Under it the press was a click: the
layer is picked and **the move it began is taken back, not committed** — a click
that also nudged the drawing by a pixel would be the worst of both. At or past
it the press was a drag and nothing is picked.

Everywhere else the pick happens on the press, as first built: with a brush in
hand there is no drag for Ctrl+Shift to mean.

The pointer follows the same line. Where the press picks, it is the plain arrow
— not the tool's, because the tool will not act, and not refused by a lock on
the active layer, because the active layer is what is about to change. Where
the press starts a move, it is the move pointer, which is what a drag from
there will do.

## What this did not answer

**The registry.** No mouse-and-modifier gesture on the canvas is in
`ShortcutMap` — not the held eyedropper, not Shift for a straight segment, not
Alt to erase, and not this one — so none can be seen or rebound in Configure.
That is one gap across all of them and wants its own answer rather than a
fourth exception.

**The moment between the keys.** Pressing Ctrl before Shift borrows the
eyedropper for the instant between them, in the tool rail and the pointer, and
gives it back. Correct by the rule that a combination is not the modifier on its
own, and visible. Left as it is.
