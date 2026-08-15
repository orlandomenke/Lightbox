# Q26 · When a textured line is re-shaped, may its texture change? — **answered (a)**

**Answered 2026-08-07: (a), accept it. The grain belongs to the canvas.** A mark
is a function of where it is, the way a real pencil's grain is a function of the
paper's tooth under it.

**This closes the question rather than deferring it, and that is worth the
sentence:** (b), (c) and (d) are now *rejected*, not "later". Nothing needs a
seed origin on the stroke, nothing needs arc-length seeding, and — the one that
matters most — **no tunable radius enters the render path**. Invariant 4's
suspicion of hidden knobs is upheld for free, and invariant 2 stays exactly as
written, with no second costume to check for.

**What it obliges.** Pillar 0's re-shaping item ships with a manual line saying
that moving a textured line changes its grain, and saying *why* — not as an
apology but as the same fact as the paper's tooth. An artist who wants the mark
preserved exactly moves the layer rather than the line, which is a real answer
and should be the one the manual gives.

**Blocks:** nothing. Re-shaping is unblocked.


Invariant 2 seeds every dab dynamic — scatter, size, roundness, rotation, all
three colour jitters — from the dab's position via `Hash01`. That is what makes
a mark reproducible on reload, on undo and through the inbetweener, and it is
not negotiable.

The consequence nobody has had to face yet is that **it also means moving a line
changes what the line is made of.** Drag a control point and the dabs near it
re-seed: the grain shifts, the scatter lands elsewhere, a bristle that was
splitting now does not. Correct by the invariant, and wrong to the artist, who
expects to nudge a line and see *the same line, somewhere else*. Pillar 0's
re-shaping item cannot ship without an answer, and the answer changes the
record, so it cannot be retrofitted.

**(a) Accept it, and say so in the manual.** The mark is a function of where it
is, the way a real pencil's grain is a function of where the paper's tooth was.
Free, honest, and it makes re-shaping feel unreliable for exactly the brushes
people would most want to re-shape.

**(b) A seed origin stored per stroke.** Hash from `position − origin` rather
than from position, and carry the origin through an edit. The texture then
travels with the line. Cheap, and it changes the meaning of every existing
stroke unless the origin defaults to zero — which it can, so old documents
render identically. The catch: two strokes drawn in different places with the
same shape now have the *same* texture, which is the flicker invariant 2 exists
to prevent, in a new costume.

**(c) Seed from arc length along the stroke instead of from position.** The
grain belongs to the stroke rather than to the canvas, so it survives any edit
including a wholesale move. Also kills (b)'s duplication problem, because two
strokes still differ if anything else about them differs. The cost is real: a
dab's seed now depends on every point before it, so an edit near the *start*
re-seeds everything after it — the opposite failure, and arguably worse.

**(d) Re-seed only what moved, and blend.** Keep position seeding, but let dabs
within some radius of an untouched point keep their old values. Preserves the
line away from the edit at the price of a rule with a tunable in it, and a
tunable in the render path is the thing invariant 4 is suspicious of.

Not answerable by measurement, which is why it is here: every option renders
*something* defensible, and the question is which one an animator would call
the same line. **art-director holds the veto on that**, and **ai-engineer holds
it on whether the chosen seeding still reproduces exactly** — an inbetween of a
re-shaped stroke has to land where the record says. (c) is the one I would open
the argument with, on the grounds that grain belonging to the stroke is what it
belongs to on paper, but the start-of-stroke cascade may sink it.
