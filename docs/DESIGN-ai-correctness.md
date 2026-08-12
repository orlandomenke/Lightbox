# Making the AI trustworthy

Status: **Phase 0 built** (2026-08-12) — `InbetweenVerifier` in
`Lightbox.Core/Inbetween`, the per-frame refusal path in `AiInbetweenAsync`,
and `AiProvenance` on the frame, each with the tests the *Verification*
section below asked for. The connection tester now judges with the same
verifier rather than its own embryo. Phases 1–4 — the matcher upgrades, the
golden set, repair, and adaptive shaping — remain design. The taxonomy half of
the subject reading (`DESIGN-subject-reading.md`) predates all of this.

One thing Phase 0 taught that the tier table did not state: **drag must hang
off the thing it follows.** A first cut licensed any ink behind a mover's
travel, which quietly re-licensed everything disocclusion's continuation rule
exists to refuse — anything in the wake trails the mover by construction. The
shipped check requires proximity to the mover's current geometry as well as a
backwards deviation, and `RevealedInkThatContinuesNothingIsRefused` is the
test that caught it.

The AI features are meant to be the flagship — artistry × time, the artist in
control, the machine taking the tedious and managerial work. That only holds if
the output is **reliable**. An inbetweener that is right four times in five is
not a time-saver; it is five frames to check instead of four to draw.

Four constraints, and they push in the same direction:

1. **Reliable or worthless.** Correctness is the feature, not a quality bar on it.
2. **Bring your own model** — open, artist-supplied, and *not* by making them
   supply an agent. Step in and start.
3. **No naming conventions.** Artists draw many subjects in many styles and will
   not label strokes to make the AI's job easier.
4. **Models differ**, especially the small local ones a BYO world will see.

## The insight it rests on

**Lightbox's AI output is strokes, not pixels — so it can be checked.** An
image generator's output can only be judged by eye. An inbetween is geometry
between two known drawings, and most of what makes it wrong is computable.

And there is already a deterministic inbetweener that always produces a usable,
if unimaginative, answer.

> **The model proposes; Lightbox disposes.** Reliability is a property of the
> harness, not of the model. A weak model produces *fewer* frames — never worse
> ones.

That is what makes constraint 2 affordable: any model is safe to plug in
*because none of them is trusted*. Note the shape of the guarantee, settled by
Q32: a weak model does not silently become the cheap engine, it **declines**.
The deterministic answer is always available; it is simply never served under an
AI request without being asked for.

## What already exists and gets reused

| Existing | Path | Used for |
| --- | --- | --- |
| `Inbetweener.Inbetween` / `InbetweenSeries` | `src/Lightbox.Core/Inbetween/Inbetweener.cs` | The reference every check measures against — and the artist's own separate command |
| `StrokeMatcher.Match` | `src/Lightbox.Core/Inbetween/StrokeMatcher.cs` | Correspondence without labels — constraint 3, already solved |
| `StrokeInterpolator.Interpolate` | `src/Lightbox.Core/Inbetween/StrokeInterpolator.cs` | Expected position per `t`, so "between" is computable |
| `StrokeRecordCleaner.EffectiveStrokes` | `src/Lightbox.Core/Inbetween/StrokeRecordCleaner.cs` | The view sent to the model; the verifier must judge the same one |
| `AiConnectionTester.BadInbetween` | `src/Lightbox.Ai/AiConnectionTester.cs` | The verifier in embryo — generalise, do not duplicate |
| `FrameRole.Breakdown` | `src/Lightbox.Core/Documents/Frame.cs` | Artist-supplied path constraint at no workflow cost |
| `SubjectTaxonomy` | `src/Lightbox.Core/Projects/SubjectTaxonomy.cs` | The semantics the artist never typed — and see *reveal*, below |

## The pipeline

| # | Stage | Status |
| --- | --- | --- |
| 1 | **Schema** — structural validity | Exists |
| 2 | **Clamp** — value validity, bounds, colour | Exists |
| 3 | **Verify** — is it a correct inbetween | **Built** — `InbetweenVerifier` |
| 4 | **Repair** — bounded re-ask naming the fault | New (Phase 3) |
| 5 | **Refuse** — no frame, and a sentence naming which `t` and why | **Built** — per frame, in `AiInbetweenAsync` |
| 6 | **Report** — the artist knows which frames to look at | **Built** — the status line names each refused `t` |

Stage 5 is what makes this a guarantee rather than an effort — and per Q32 it
guarantees by **refusing**, not by substituting: **the AI never produces a frame
it cannot defend.** The deterministic engine stays one click away for an artist
who wants it, but it is never quietly served under an AI request.

## The hard part: the AI is *supposed* to invent

The obvious check — *a stroke matching nothing in either key is a
hallucination* — forbids the most valuable thing the feature does. All of these
are inventions with no counterpart in either key:

- A curtain swings aside and the window behind it appears.
- An arm swings away from the torso and the body it covered appears.
- A torso rotates with the arms in a run, where neither key states the rotation.
- An animal jumps: it squashes and stretches, its fur drags behind the arc, and
  wind ruffles it mid-flight.

So the verifier cannot ask *"is this new?"* It must ask **"is this new ink
licensed?"** Four tiers:

| Tier | What it covers | How it is judged |
| --- | --- | --- |
| **Forbidden** | New ink explained by nothing | Rejected |
| **Disocclusion** | Something moved off and revealed what was behind | Must lie in a vacated region, and must **continue** the stroke it belongs to |
| **Interpretation** | Motion the keys do not state — arc, rotation, overlap | Bounded by distance from the deterministic reference, under a dial |
| **Drag** | Secondary action: fur, cloth, tails following the motion | Deviation must point **backwards** along the direction of travel |

### Reveal is checkable because the taxonomy has depth

`SubjectPart.Depth` was built to keep an inbetween on-model. It turns out to be
what makes disocclusion computable: a higher-depth part moving off a lower-depth
one **vacates a region**, and new ink inside that region is expected while new
ink outside it is not. The reading was the prerequisite for *checking* reveal,
not only for prompting it.

**Continuation is what makes reveal safe.** Revealed ink is almost never
arbitrary — the torso outline behind the arm continues the torso outline either
side of it. A revealed stroke should join the visible ends of the stroke it
extends, within tolerance. That separates "drew the body" from "drew something
in the hole".

### Volume is area, not length

The naive volume check — path length near the interpolated length — is violated
by squash and stretch *on purpose*; that is the principle. The principle is that
volume is **conserved**: a body squashing to 70% height widens to about 140%.

So the check is on **enclosed area**. It gets more useful rather than less: a
stretch that also thins is a real error, and length cannot tell the two apart.

### Wind must be authored once, or the sequence boils

The hazard, and the reason this section exists. If a model ruffles fur
independently on each of twenty-four frames, the result is the classic boil —
exactly what `CLAUDE.md` forbids: *"anything stochastic must be seeded from
geometry, not from an index or a clock"*, and *"an effect that varies subtly
between similar strokes looks fine on one image and boils at 12 fps."*

**Invariant 2 arrives here through a door nobody was watching.** That rule is
written about *rendering*, and this randomness enters during *authoring*. Each
frame is deterministic strokes and replays identically forever; every existing
test passes; the sequence still boils.

The resolution is the shape the taxonomy already established:

> **The model authors the parameter once; the deterministic path applies it to
> every frame.**

Wind is a small authored record — direction, strength, gust period — proposed
for a *sequence*, editable by the artist, applied deterministically. Not a
per-frame invention. It also hands the artist the dial they would most want to
tune, which is the point of the feature rather than a concession to it.

**Interpretation is a dial, not a rule.** *"If the art style is relatively
simple that might not be needed."* One control per document, defaulting low,
from "only what the keys state" to "add the overlap and follow-through an
animator would".

### The verifier sees a sequence, not a frame

Temporal coherence — a secondary-motion offset must be a smooth function of
time — is the only check that catches boiling, and no single-frame check can
make it. Per-frame independent noise fails a second-difference test trivially.

This is cheap to decide now and an expensive retrofit once the verifier has a
one-frame signature, so the pipeline verifies **a run of inbetweens** rather
than each one alone.

## No naming conventions

Largely already satisfied. `StrokeMatcher.Match` tries labels first and falls
back to geometry, so unlabelled art already pairs. Three upgrades, none of which
asks the artist for anything:

- **Optimal assignment** instead of greedy. Filed and fixed as **B113** — a box
  moved by more than half its height crossed its own edges over and collapsed
  mid-motion. Greedy scored 120 where the optimum scored 80.
- **Shape in the cost**, not only position and length: turning direction and
  endpoint alignment separate a left arm from a right arm.
- **`FrameRole.Breakdown` as a hard constraint** — the arc must pass through it.
  The artist already drew it; using it costs them nothing.

## Strengthening weak models

**Measure before trusting.** A committed *golden set* of keyframe pairs with
known-good answers, scored by the verifier, produces a **capability profile**
per provider: schema adherence, betweenness, arc-following, and how many strokes
before it degrades — the number that matters most and that nobody measures.

**Then adapt the request to the profile.** It is a plan, not a report card:

| Weakness | Adaptation |
| --- | --- |
| Degrades past N strokes | Send only the strokes needing judgement, N measured |
| Answers each `t` independently | One `t` per call, feeding the previous answer forward |
| Drops strokes | Send matched pairs explicitly; verify correspondence hard |
| Weak on arcs | Use it only where the deterministic engine is weak |

**Repair with the fault, not a blind retry.** *"The near arm sits at y=20,
outside the keys at 20 and 100"* is information a model can act on; a blind
retry is a second roll of the same dice. Bounded, then fall back.

**Best-of-N** is opt-in and cost-badged, following the brush rule: available,
deliberate, never the default.

**The deterministic answer is a free two-sided signal.** Too far is suspicious;
*too close* means the model added nothing and should not have been paid for.

## Open, BYO, no agent required

The built-in providers stay the front door — a key and a model name. MCP stays
the escape hatch for a team with their own agent and is never a prerequisite;
requiring one would be exactly the step-in-and-start failure to avoid. The
verifier is what makes a local model genuinely *usable* rather than merely
connectable: weaker means falls back more often, which costs time rather than
risking the work.

## Phasing

| Phase | What | Why here |
| --- | --- | --- |
| **0** | Verifier, refusal path, provenance — **done** | Biggest win; no new model calls |
| **1** | Matcher: shape cost, breakdowns as constraints | Deterministic; improves the non-AI path too |
| **2** | Golden set + capability profile | Makes reliability a number |
| **3** | Repair loop | Needs specific findings first |
| **4** | Adaptive shaping, best-of-N, authored wind | Needs the profile |

**Phases 0 and 1 need no provider**, so they are testable in CI.

## Not in this cut

No new provider, no new model, no hosted service. No agent requirement. Not the
placement half of the subject reading — still gated on its measurement. Not
inking or normal maps; they inherit this pipeline when they arrive.

## What was decided — Q31–Q34, answered 2026-08-07

| | Decision |
| --- | --- |
| **Provenance** | Stored on the frame, **absent unless AI touched it**. A document that never used the AI is byte-identical to one from before the feature existed |
| **Unrepairable frame** | **Insert nothing and say why.** Not the deterministic fallback under an AI request |
| **Too close to deterministic** | **Report, never reject.** A cost signal and a diagnostic, never a veto |
| **Golden set** | **Ships**, so an artist can grade their own model |

**The second one changes stage 5, and it defeats the recommendation rather than
overruling it.** The recommendation rested on treating the deterministic engine
as a *floor*. The owner's objection: *"for complex subjects — a human, a dog or
something else complex and organic — I believe the deterministic inbetweener is
prone to make mistakes. I'd rather have nothing than a frustration."*

**That is correct, and B113 proves it.** Four straight lines making a box, and
the matcher crossed the top and bottom edges over so the shape collapsed
mid-motion. On a box the error is obvious the moment you look. On a quadruped's
legs it is a subtly wrong walk that costs an afternoon to find.

So the deterministic engine is a floor **for simple subjects and not for
complex organic ones**, and substituting it under a failed AI request swaps one
unreliable answer for another while calling the swap safety.

So the floor is not "the AI silently becomes the cheap engine". It is **"the AI
never produces a frame it cannot defend"**, with the deterministic engine one
click away. The guarantee is intact — the AI still cannot make a scene worse —
but it is delivered by refusal rather than by substitution.

The cost, recorded so nobody rediscovers it as a bug: a request for four
inbetweens can return three. The status must name which `t` was refused and
why — *"frame 3 of 4 was refused: the near arm did not stay between the keys"* —
or the gap is a puzzle instead of a decision. Refusal is **per frame**: the ones
that passed are inserted.

**Q32 and Q33 interact, and it constrains both.** If the deterministic engine is
untrustworthy on complex organic subjects, then *distance from the deterministic
answer* — Q33's free signal — is weakest exactly where correctness matters most.
The rule that falls out: **distance from the deterministic answer is evidence
about cost, never about correctness.** Far from it on a dog means very little;
close to it on a dog might mean both answers are wrong the same way.

**And it tells the golden set what to contain.** A set of boxes and swinging
arms would certify a model on the cases where the reference is reliable and say
nothing about the cases the owner actually worries about. It needs complex
organic subjects — a quadruped's gait, a figure turning — with *hand-drawn*
known-good answers rather than deterministic ones.

## Verification

The verifier is tested against **known-bad** frames, not known-good ones: hand-
built candidates that fail each check exactly once, so a check that can never
fire is caught. That is the `BadStrokes` off-canvas lesson — a check that cannot
fire is worse than no check.

Then, per Q32: with a stub artist returning rubbish, **no frame is inserted**,
the document is unchanged, and the status names which `t` was refused and why —
a silent no-op and a refusal are different outcomes and the test must tell them
apart. For every golden pair, an accepted frame must score at least as well as
the deterministic answer, which is the guarantee stated as a test. And a
document that never calls the AI must serialize and render byte-identically —
the provenance key absent, not empty.

Gate **G12** applies — `ai-engineer` and `art-director` both review, and this is
precisely their disagreement: the engineer will want the verifier strict and
cheap, the director will object that a strict verifier rejects the interesting
answers. Where they cannot measure it, `QUESTIONS.md`.
