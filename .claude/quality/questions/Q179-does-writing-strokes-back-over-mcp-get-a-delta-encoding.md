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

## "Could an agent send a bitmap instead?" — asked 2026-09-04, and the cost half of it is backwards

Worth recording in full, because the intuition behind the question is right and
the number people expect is wrong in the other direction.

**On size, a picture wins and it is not close.** The same 120-stroke frame,
measured through the real path:

| | Size | ≈ output tokens |
| --- | --- | --- |
| as stroke JSON | 147.4 KB | ~37,700 |
| as a 768 px PNG, base64 | 3.0 KB | **~770** |

**~49×**, because pixels do not grow with stroke count and JSON numbers do. (The
fixture is synthetic and repetitive, so it compresses better than real art —
an optimistic bound, and the direction survives any plausible correction.)
`ABitmapOfTheFrameIsCheaperThanItsStrokes_WhichIsNotWhyItIsRefused` keeps the
number, and is named so that the next person to have this idea finds the
reasoning rather than re-deriving it and concluding it was rejected as expensive.

**It is refused for two reasons, neither of them cost.**

1. **A model cannot author a PNG.** The channel is text; a bitmap arrives as
   base64 of a deflate stream, and no language model emits one validly. The 49×
   is real and unreachable from the write side at any price. This is the
   decisive one, and it is worth separating from the second, because it would
   still hold in a codebase with no invariants at all.
2. **Invariant 1.** A frame is a list of strokes and the pixels are derived. A
   raster frame cannot be re-rendered at another size, re-timed, inbetweened
   again, recoloured through a swatch, or exported through a camera. It would be
   a drawing that stops being editable at the moment an agent touches it.

**The precedent that makes this a distinction rather than a prohibition:**
`Frame.Checkpoint` already stores pixels *in the document* (`DESIGN-raster-checkpoint.md`,
276× on reopening a 1 000-stroke painting). What makes that lawful is precisely
what an authored bitmap lacks — a record behind it, and a `CheckpointFingerprint`
recomputed before the pixels are ever used. **Pixels are allowed exactly when
something can prove what they were made from.** So the answer is not "no pixels";
it is "no pixels without a record".

### The version of the idea that does work, and it is option (c)

Raster as an **intermediate**, never as the record: an *image* model — not the
text agent — draws the inbetween as pixels, and Lightbox traces it back into
strokes. The text agent never emits geometry, so the ceiling does not shrink,
it **dissolves**; and invariant 1 holds, because what lands in the document is
strokes.

What it costs, and why it is not obviously better than the delta:

- **Nothing traces today.** There is no vectoriser anywhere in the solution, so
  this is a build rather than a wiring job.
- **A traced stroke loses what inbetweening runs on.** Pressure and tilt are
  gone, so the line is dead in the way `DESIGN-pen-dynamics.md` cares about —
  but the worse loss is **identity**: a trace produces *some* strokes, not
  *these* strokes, so labels and the correspondence to key A and key B are gone.
  The next inbetween of that frame has nothing to match against, and Q34's
  golden set scores label retention precisely because losing it is invisible
  until weeks later.
- It moves the expression risk rather than removing it: instead of a model
  taking the cheap path within a wire format, an image model draws something
  plausible and on-model-ish that no longer corresponds to the artist's lines.

So the three options for the pair are now: **(a)** per-stroke delta, **(b)**
per-frame delta, **(c)** raster intermediate plus a tracer. (c) is the only one
that removes the ceiling outright, and the only one that can lose stroke
identity — which is the same trade the other two make, one level up.

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
