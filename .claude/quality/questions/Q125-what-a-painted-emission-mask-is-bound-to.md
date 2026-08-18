# Q125 · What a painted emission mask is bound to — **answered 2026-08-18**

Q124 settled that emission is painted and alpha-locked. Building it needed four
finer decisions, and the first of them **corrects Q124's reading** rather than
extending it.

**The correction, in the owner's words:** *"The ink emitter itself should not be
blocked by the alpha lock, just the surface painter … the emitters themselves are
only bound by their origins."*

Q124 recorded alpha lock as a property of *emission* — the mask intersected with
the target layer's ink, so fire could not leave the garment. That is not what it
is. **Alpha lock belongs to the painter, not to the bake**: it is an authoring aid
that keeps the brush on the costume while a mask is painted, and it is optional.
Once painted, the mask is the emission, whole, with no per-frame intersection
against anything. The emitter is placed by its **origin** and nothing else clips
it.

The difference matters, and it is a simplification rather than a loss. An
intersection at bake time would mean the emission silently changes whenever the
drawing under it changes — a hem swinging away would extinguish the fire on it,
with no mark in the record saying why. Painting is authoring; the bake reads what
was authored.

**1. The mask follows by its origin, and can be redrawn by hand where that is not
enough** (owner: *"both … so we can adjust the mask if need be"*). The base is a
rigid stamp placed by a keyable, anchor-bindable origin: it translates with the
character without deforming. Where that slides — a billowing cloak deforms and a
stamp does not — the mask is a drawing on its own timeline and can simply be
redrawn on the frames that need it.

The two compose rather than competing, and only because of decision 2: a mask
that is a layer has cels, so "the mask on frame N" is whatever is exposed there,
while the origin moves the whole stamp. Neither mechanism has to know about the
other.

Two options were declined and the reasons are worth keeping. **Rig binding** is
the strongest answer for a rigged character and helps nothing on a cloak drawn
frame by frame, which is a great deal of 2D work; it stays available for free,
since a mask layer binds like any other. **Shrink-wrapping onto the ink each
frame** was declined as a heuristic with no notion of correspondence: it does the
wrong thing the moment the shape changes topology, and *"the fire jumped to the
wrong part of the cloak"* is a maddening bug — the same correspondence problem
Q116 already declined to solve for contours.

**2. The mask lives on its own layer, marked omit-from-export** (recommended,
accepted). Every existing tool then works on it — brush, holds, the inbetweener,
rig binding, onion skin — and none of it is rebuilt. `Layer.OmitFromExport`
already exists and keeps it out of renders and sprite sheets. The cost accepted:
it shows in the layer stack, so it can be hidden, deleted or painted on by
accident, and an element now owns two layers — the one it bakes into and the one
it reads — which needs saying rather than discovering.

**3. The obstacle is a separately named layer** (recommended, accepted). What
blocks the fluid is the figure and her costume; what emits is a painted patch.
They are different questions, and keeping them apart is what lets fire leave a
sleeve while the whole torso still occludes it. Folding them together would make
flames pass straight through the character, which is the thing that reads as
pasted on. The cost is two references to set up instead of one.

**4. The painter's alpha lock defaults on** (recommended, accepted). The common
case — painting emission onto a garment — then works with no setup step, and
stray marks in empty space are impossible until it is deliberately switched off.
The cost accepted: an artist wanting emission just *off* the silhouette, for a
flame licking past an edge, meets a brush that will not paint there and has to
find out why. That is a tooltip and a manual line, not a design fault.

**Scope.** Emission from a painted mask is one branch; the obstacle is another.
The obstacle's cost is not the rasterisation, which is shared — it is putting
solid boundaries *inside* the grid, where `FluidSolver` has only walls at its
edge. The pressure solve needs interior Neumann boundaries, the flux transport
must not carry mass into an obstacle cell, and the conservation tests have to
learn that mass may be held against an obstacle and not only against a wall.
