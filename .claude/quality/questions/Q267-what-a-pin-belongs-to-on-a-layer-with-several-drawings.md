# Q267 · What a pin belongs to on a layer with several drawings — **answered 2026-10-13**

Raised 2026-10-13 by the mesh-stability spike in `docs/DESIGN-puppet-pins.md`
(open risk 1 of the puppet-pins plan), and asked with the prompt.

The plan had pins on the rig, which belongs to a layer, with a mesh derived
for each drawing and a pin snapping to the nearest triangle of whichever
drawing is showing. The spike measured that on generated drawings: in a walk
cycle a pin dropped on a hand is no longer on the hand on 18 of 23 later
drawings, and on 8 to 10 of 23 even when the figure moves an eighth as much
per drawing. A pin that has left its part either snaps to the nearest ink or
is quietly standing on a different part.

What it blocks: the pin render path (step 3) and the tool (step 4).

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **What a pin belongs to** | **One drawing** (recommended) — pins and their mesh belong to the drawing they were dropped on and to its holds; other drawings on the layer are untouched until given their own | **A frozen mesh, as After Effects does**: no change to the record; works while the drawings stay close to the one the mesh was cut from and fails without saying so as they move away — a pulled hand drags whatever now stands where the hand was. **Layer-wide with a mesh per drawing, the plan as written**: no change at all, and the measured result above; on line art drawn as open lines the mesh also differs between redraws by more than the hand's own wobble. |

**Recommendation:** as marked, and the owner took it.

## What follows from it, not up for preference

- **The record changes before anything can author a pin.** A pin has to say
  which drawing it is on, absent when unused like the rest of the pin record;
  a hold is one drawing. Step 2c in the plan, tests first.
- **Copying pins to another drawing** is a convenience for later and gets its
  own scope call; nothing here promises it.
- **Bones are untouched by this.** A rig still belongs to its layer. A pin
  that rides a bone is still on one drawing.
