# Q49 · Do shapes become retained objects? — **answered: no, they stay strokes**

**Answered 2026-08-07: a rectangle is still a line painted with your brush** —
now reshapeable like any other line, but the document does not remember it was
ever a rectangle.

This **softens rather than reverses** the shipped manual sentence — *"it is not
re-editable as a shape afterwards"* — which stays true as written: not
re-editable *as a rectangle*, but re-shapeable like everything else. Grabbing its
corners is most of what anyone wanted.

**The reason is Krita, from the other direction.** Retained shapes mean two kinds
of thing in one document and a rule that some tools work on one and not the
other. Krita has that rule and it is the failure: its SVG layers *"don't actually
contain brush strokes, which makes them useless for most line art"*, and the
brush tool is unavailable while one is selected. One `Stroke` record is the
asset, and it is not being spent here.

**What it costs.** No retyping the width of a rectangle you drew last week; you
move its corners instead. Live shapes remain reachable later if an artist asks —
nothing here forecloses them.

**Blocks:** nothing.
