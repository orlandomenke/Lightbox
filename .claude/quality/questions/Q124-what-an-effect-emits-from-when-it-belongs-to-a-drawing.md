# Q124 · What an effect emits from when it belongs to a drawing — **answered 2026-08-18**

The owner's use case, and the first one the record could not express: *"a
character … draping large clothes … I do want flames on her clothing moving in
the direction she is going."* Wind and attachment were already settled by Q122;
what was missing is that an emitter is a point, a segment or a disc at fixed cell
coordinates, and a draping hood changes silhouette every frame as the animator
draws it. A fixed emitter cannot follow that, and hand-keying an emitter path per
frame is exactly the drudgery this system exists to remove.

**1. Emission is painted, and alpha-locked to the layer it belongs to.** The
owner's counter-proposal, and better than any of the three options put:
*"perhaps a painting tool where we can specify from which points the emitter
should emit — alpha lock it to the paint on the targeted layer."*

Two things make it right rather than merely convenient. It makes emission a
**drawing problem solved with a drawing tool**, which is the application's whole
thesis. And alpha lock is an existing concept with existing semantics that
artists already know — paint only where there is content — so painting emission
into thin air becomes impossible rather than merely discouraged.

The form that falls out: **the emission mask is just a layer.** It then animates
like any other drawing — holds, the inbetweener, rig binding all apply for free —
and an emitter references it by id exactly as an obstacle references the costume
layer. It also subsumes the option it replaced: if you want fire only along a
hem, paint the hem.

**The gap alpha lock does not close, and it matters for a run cycle.** Locking to
the target keeps a mask *on* the garment; it does not make it *follow* the
garment. Paint emission on the hem at frame 1 and by frame 12 the hem has swung —
the mask is still on the cloak and now sits on her shoulder. Three answers, and
they compose rather than compete:

- **Bind the mask to the rig.** Skinning and bone bindings already exist; a mask
  painted once in a rest pose follows the character. The strongest, and it is
  existing machinery.
- **Animate it like any drawing** — hold it on 4s, let the inbetweener carry it.
  Costly over forty-eight frames and squarely the application's competence.
- **Fall back to an edge band when no mask is painted**, so an element works the
  moment it is made and gets better when somebody paints. The layered default:
  no mask → the shape's edge; mask → exactly what was painted.

**2. Drawn art blocks the fluid as well as feeding it** (recommended, accepted).
Fire hugs the cloth and flows around the body rather than through it, which is
most of what makes an effect look attached rather than pasted on — and it is the
same rasterisation either way, so the marginal cost is the solver's boundary
handling rather than a second pipeline.

The cost accepted is real and lands in `FluidSolver`: **solid boundaries inside
the grid**, where today there are only walls at the edge. The pressure solve
needs interior Neumann boundaries, the flux transport must not carry mass into an
obstacle cell, and the conservation tests have to learn that mass may be held
against an obstacle rather than only against a wall. This is the coupling
`docs/DESIGN-fluid-effects.md` deferred twice under *"fluid that flows around
drawn art"*; the use case is what moved it from someday to next.

**3. Band count and simplification derive from the element's size**
(recommended: ship presets per target; **owner chose derivation**). The numbers
that read at 360 px are not the ones that read at 96 px, and deriving them means
an element is right at any size with nothing to choose.

The invariant-7 tension raised when it was put **dissolves, but only under a rule
that has to be written down**: derive from the element's *authored* size in
document pixels, never from the size it renders at. A 2× export then changes
nothing, because the element is still the same size in the document — while
deriving from output scale would break invariant 7 outright and would be an easy
thing to write by accident.

The remaining cost — *"my effect changed when I resized the box"* — is smaller if
the derivation is a **default applied at authoring time** rather than a
continuous function: creating an element, or asking it to fit, picks the numbers
from its dimensions, and resizing afterwards does not silently redraw. That is
how it will be built unless the owner would rather it tracked continuously.

**4. The character path before the authoring window** (recommended, accepted).
Wind, attachment and the ink emitter before step 5's window. It means driving
the feature from tests and renders a while longer — slower to play with, and
step 4 showed how much a render catches that a test cannot. The window then has
something worth previewing.
