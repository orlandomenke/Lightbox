# Q179 · Does writing strokes back over MCP get a delta encoding — **open, and deliberately routed to the pair**

Raised by: the third of the three MCP payload questions put on 2026-09-03. The
scope half was answered — *measure it, file it, fix the read side first* — and
the answer to the design half was explicitly that it goes to the
ai-engineer / art-director pair rather than being guessed. This file is that
routing, not a question nobody asked.

What it blocks: B-numbered ceiling in `BUGS.md`; whether an agent can inbetween
a dense drawing over MCP at all.

## The measurement

`insert_inbetweens` and `draw_strokes` take full geometry, so an agent must
**emit** every point of every stroke. Measured through the real path on a
120-stroke, 90-point frame:

| | Size | ≈ output tokens |
| --- | --- | --- |
| one frame written back | 147.4 KB | ~37,700 |
| three inbetweens | 442.2 KB | ~113,200 |

**This is a ceiling, not a bill.** 113k output tokens does not fit in one
response at any provider here, so the task cannot be completed over MCP —
however patient the artist is and however much they are willing to spend. Every
other cost on this surface is something you pay; this one is something you
cannot do. `WritingAFrameBackCostsWhatReadingItDid_WhichIsTheCeiling` prints and
asserts it, so a fix shows up as a change rather than as a claim.

## Why the obvious fix is not obviously right

The cheap answer is a delta: let a frame name key A's strokes by label and carry
only what genuinely changes — a transform per stroke, geometry only where the
line is actually redrawn. It removes the ceiling outright, it is expressible in
the existing wire format, and invariant 1 survives because the app expands it
into real strokes through the same path before anything is recorded.

**The objection is art-director's and it is not a quibble.** A transform-only
inbetween is, precisely, the deterministic answer. `DESIGN-ai-payload.md` says
the AI is wanted exactly where straight interpolation fails — arcs, rotation,
overlap — and the free engine already handles a matched stroke correctly. A wire
format whose cheap path is "translate these strokes" and whose expensive path is
"draw this properly" will get the cheap path, because a model minimises effort
against the shape it is given. That is a payload change that quietly rewrites
what the feature is for, and it would show up as inbetweens that are worse in a
way no schema check catches.

So the two vetoes point at each other: ai-engineer's ceiling is real and
art-director's expression cost is real, and neither is measurable from the other
side. Gate G12 says that goes here rather than to whoever ran last.

## What an answer needs to settle

- Whether a delta is **per stroke** (this one moved, that one is redrawn) or
  **per frame** (everything moved, plus these exceptions) — the first keeps the
  choice at the granularity where the judgement actually is.
- Whether the format can make redrawing the *default* and translation the
  marked-up case, which would invert the effort gradient the objection is about.
- Whether the golden set ([[Q34]]) can score "took the cheap path when it should
  have drawn" at all. If it cannot, this is not decidable by measurement and
  becomes a judgement call that should be recorded as one.

Related: [[Q177]] is the same problem on the read side and was separable;
[[Q18]] is the precedent for a wire-shape change traded against how well a model
uses it, and its adherence claim is still unmeasured.
