# Q17 · Does an inking pass replace the pencils or land on its own layer? — **answered (c)**

**Answered 2026-08-07: (c), one Ink layer for the whole sequence**, its cels
lined up with the pencils'. Non-destructive without the two-hundred-layer
problem, and it uses the layer model as it already stands.

**It carries a UI commitment, and that is the half worth writing down:** an
inking pass runs over a **range**, not a frame. A per-frame gesture would make
one layer per frame by accident, which is the option this answer rejected. So
the surface that starts an inking pass takes a range the way the exposure-sheet
operations already do.

**Blocks:** nothing. Inking is unblocked — it was the last thing waiting on this.


**(a) Its own layer, pencils untouched and hidden.** What an inker does on
paper, non-destructive, and the artist can re-run with a different style
without losing anything. Costs a layer per inked frame, which over two hundred
frames is a layer count nobody wants to scroll.

**(b) Replaces the strokes in place, one undo step.** Matches "the stroke record
is the document" — the inked lines simply *are* the frame now. Cheap, tidy, and
the artist keeps the pencils by duplicating the layer first if they want them,
which is a thing they already know how to do.

**(c) Its own layer, but one layer for the whole sequence** — an "Ink" layer
whose cels line up with the pencils'. This is what the layer model already
supports and it is probably the answer, but it assumes an inking pass is run
over a range rather than a frame, which is a UI decision as much as a record
one.

The reason it cannot be deferred: (a) and (b) produce different documents from
the same gesture, and a file written under one cannot be reinterpreted as the
other. Pick before the first pass ships, not after.
