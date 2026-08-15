# Q80 · Animation-aware brushes: is authored line boil in scope — **answered 2026-08-13**

Scoping the Pillar 4 `[?]` item *Animation-aware brushes* (defined in the
roadmap entry: grain anchoring, inbetweenable dynamics, frame-context
response, sequence-scale cost review) surfaced one genuine decision: whether
**deliberate, deterministic line boil** — per-frame variation so a hold can
"breathe" on 2s, the TVPaint aesthetic — belongs in scope at all, given that
geometry-seeded determinism makes holds dead-still by construction.

**In scope, opt-in** (recommended, accepted). A per-stroke, off-by-default
effect with an authored per-frame phase stored in the record: deterministic,
so invariant 2 holds and re-renders are identical; absent from the file
unless used, so the optional-means-absent rule holds too. Costs accepted:
the stroke record grows a per-frame dimension, and this is the first effect
whose seed varies by frame — a real extension of the `Hash01` seeding story
that needs its own re-render and hold-stability tests when it is built.

The alternatives both had a named price. *Out of scope* keeps the aesthetic
impossible, and artists fake it with redrawn holds the exposure sheet can no
longer represent as holds. *Post effect over finished frames* is cheaper but
cannot respect per-stroke intent (ink boils, fill does not) and breaks the
rule that pixels derive from the stroke record alone.
