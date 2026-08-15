# Q93 · Adjustable guides: the way in, how "persistent" is expressed, and where a ray count lives — **answered 2026-08-14, three as recommended and one against**

Prompted before building, from the owner's report that the grid, the character
height scale and the vanishing point could all be placed and none of them
adjusted — and that the request itself offered two possible routes.

1. **Both routes, because they are one feature** (recommended, accepted). Hover
   and click select a guide with the Move tool, and the selected guide's numbers
   land in the tool options — which the Move tool had left empty. The second
   route falls out of the first rather than competing with it: while the Move
   tool is in hand every guide lights faintly, so "there is something here to
   grab" is answered before the pointer finds it. Three levels of emphasis
   because they answer three questions — *can I grab anything*, *would this one
   come up*, *which one am I changing* — and a grid and an isometric rig also
   grow an anchor handle, since they are grabbed at a single invisible point and
   the affordance was otherwise a sentence in the manual. Cost accepted: a
   per-guide tint pass on the paint path, and a rule that the grid's lattice
   does not brighten at the ambient level, because a whole-canvas wash is not a
   hint.
2. **Edits are per guide; one button makes them the default** (recommended,
   accepted). Every field changes the selected guide on this document, undoably,
   and nothing reaches a preference until "Set as default" is pressed. The
   rejected alternatives were a pin on every field — three times the controls in
   an already dense bar, for a decision made rarely — and writing the default on
   every edit, which makes the default meaningless. This is the existing rule
   for a grid's pitch, generalised rather than invented.
3. **A vanishing point's ray count rides `Guide.Divisions`** (recommended,
   accepted). The field is already nullable and already means "how many of
   them"; a second key would widen the record to say the same thing twice. Cost
   accepted: one field means two things by kind, which the doc comment states
   and `OnlyAGuideThatCountsSomethingWritesADivisionsKey` pins — including that
   a vanishing point nobody has dialled still writes nothing.
4. **A height scale is sized from the canvas at creation only** — *against* the
   recommendation, which was a live "fit to canvas" toggle that re-derived the
   head unit on a document resize. What that costs: resize a document and a
   chart already on it keeps its pixel height, so it no longer reads as a figure
   against the new canvas and has to be re-dragged or retyped. What it buys is
   the simpler record — no per-guide fit flag, no resize hook, and no question
   about which of a hand-drag and an automatic re-derive wins. The default is
   still stored as a *proportion* of the canvas height rather than a pixel
   count, so the choice only affects charts already placed, never new ones.
