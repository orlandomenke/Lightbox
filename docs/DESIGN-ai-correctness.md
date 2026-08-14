# Making the AI trustworthy

Status: **Phase 0 built** (2026-08-12) — `InbetweenVerifier` in
`Lightbox.Core/Inbetween`, the per-frame refusal path in `AiInbetweenAsync`,
and `AiProvenance` on the frame, each with the tests the *Verification*
section below asked for. The connection tester now judges with the same
verifier rather than its own embryo. **Phase 1 is built**
(2026-08-14) — shape is in the matching cost, and breakdowns now constrain the
arc across a whole run; see the two sections under *No naming conventions*.
Both turned out to describe something other than what was wrong, which is the
most reusable thing the phase produced. **Phase 2's harness is built**
(2026-08-14) — `GoldenSet`, `CapabilityProfiler` and the profile it produces,
graded against `FreeEngineArtist` in CI for nothing; what is missing is the
hand-drawn organic pairs. **Phase 3 is built** (2026-08-14) — `InbetweenRepair`
re-asks a refused frame with the fault named and its own rejected drawing handed
back, bounded at two re-asks (Q85); see *Repair*, below. Phase 4 — adaptive
shaping — remains design. The taxonomy half of the subject reading
(`DESIGN-subject-reading.md`) predates all of this.

Two things Phase 0 taught that the tier table did not state, both about drag:

- **Drag must hang off the thing it follows.** A first cut licensed any ink
  behind a mover's travel, which quietly re-licensed everything disocclusion's
  continuation rule exists to refuse — anything in the wake trails the mover by
  construction. `RevealedInkThatContinuesNothingIsRefused` caught it.
- **Proximity is measured at the ink's nearest point, and licensed ink anchors
  further ink.** The second cut measured the ink's *centroid* against the
  mover, which refuses secondary action in proportion to its own length — a
  tail passed only up to about half the mover's stroke length, and a longer
  hair strand failed at any latitude because the "distance" grew with the
  strand rather than with the invention. That was the art-director's veto in
  the G12 review: the design's own flagship case (fur, cloth, tails) was
  functionally forbidden beyond a stub, silently, because no test drew a
  stroke longer than one. The shipped check samples the ink's own points, and
  a stroke licensed this frame becomes an anchor for the next — a tail of
  three strokes licenses from the attachment outwards.
  `AThreeStrokeTailTrailingTheMotionIsLicensed` and
  `ALongStrandHangingOffTheDrawingIsLicensed` are the tests that pin it.

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
| 4 | **Repair** — bounded re-ask naming the fault | **Built** — `InbetweenRepair` |
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
  endpoint alignment separate a left arm from a right arm. **Built, B196** —
  and the measurement changed what the item is. See below.
- **`FrameRole.Breakdown` as a hard constraint** — the arc must pass through it.
  The artist already drew it; using it costs them nothing. **Built for the
  deterministic path, B197/Q83 — and the item as written was wrong.** See below.

### Breakdowns as a constraint — the item was already satisfied, and something else was broken

This one asked for the arc to pass through a breakdown, and it already did. Not
because anything honoured the role: `ExposureSheet.NextKeyIndex` finds the next
*drawing* whatever its role, so a breakdown was the interval's **endpoint** and
was therefore hit exactly. There was no constraint to add.

The defect underneath was the **curve**, and it was visible only once the
premise was checked. The timeline held two notions of a span:

| | closes a span at |
| --- | --- |
| the inbetween command | the next **drawing** |
| `SpacingChart.Intended` | the next **extreme**, or the end of the sheet |

So the easing restarted at every breakdown: one slow-out/slow-in drawn across a
run came out as two with a stutter in the middle. Measured on a straight
y = 0 → 100 with a breakdown at the midpoint, the inbetweens landed at **25 and
75**, where the same movement with no breakdown gives 12.5 and 87.5. A Q58
timing chart meant different things in the two places for the same reason.

The fix is one shared span (`ExposureSheet.RunAt`, using `SpacingChart`'s
closing rule) and easing evaluated **once across the run**, each gap then
interpolated linearly at the local fraction the global curve reached. The
property that states it best, and the test that pins it: *a breakdown sitting on
the arc changes nothing about the motion* — filling a run through it gives the
identical numbers to filling the same movement with no breakdown at all.

Three things this leaves for later phases:

- **The AI path does not follow yet**, by decision rather than omission (Q83.3).
  A third drawing in the request is ~+50% strokes — the dominant token cost —
  and `InbetweenVerifier` would need piecewise betweenness. So the two producers
  now disagree about the span: the exact disagreement this work removed, moved
  one level up. Bounded, documented in the manual, and the natural thing for
  Phase 3 to close when the verifier is being changed anyway.
- **Piecewise betweenness is the verifier change that unblocks it.** Worth
  designing when repair lands, not before.
- **Check the premise before building the item.** Two of Phase 1's three
  bullets turned out to describe something other than what was wrong — the
  matcher was not mis-scoring but *tied*, and the breakdown was not missed but
  *mis-eased*. Both were found by measuring the current behaviour first, and
  neither would have been found by implementing what the bullet said.

### Shape in the cost — what building it taught

The item above reads like an accuracy improvement, and it is not: the matcher
was not scoring the wrong pairing, it was scoring **no pairing**. On an X
rotated 20° about its own centre, all four entries of the cost matrix measured
**0.0000** — two strokes crossing share a centroid and a length exactly, so
position and length are both silent, and the pairing fell out of the solver's
internal ordering. Listing the same two strokes the other way round swapped the
match. A figure's two arms are the same fault quieter: identity 41.23 against a
crossed 41.37, decided by hand jitter.

Three things worth carrying to the rest of the phase:

- **The tie is structural, so a better solver could never have found it.** When
  two strokes of A share a centroid, every row of the matrix is identical and
  every assignment scores the same total. B113 bought optimal assignment and
  this was still unreachable — the fix had to be information, not search.
- **The new terms add; they cannot multiply.** The existing cost multiplies
  distance by a length penalty, and copying that shape would have failed on the
  exact case being fixed, where distance is zero. Endpoint displacement and
  signed bow are both already in pixels, so they join the sum without a tuned
  weight — which is also why neither needs re-tuning when the canvas changes
  size.
- **The matcher had to be told what the interpolator already knew.**
  `StrokeInterpolator` reverses B when its ends are crossed, so an endpoint term
  that read point order literally would refuse pairings the interpolator handles
  perfectly. It scores the better of the two orientations, and the bow term
  negates alongside it. Two halves of one pipeline disagreeing about whether a
  backwards-drawn stroke is the same mark is the kind of seam that produces
  silent art — the same shape as B195 one layer up.

**And a note on evidence, because a tie does not fail reliably.** Asserting the
right pairing on the X passes on the *broken* build about as often as not. The
test that fails is the one that matches the same drawing twice with its strokes
listed in each order and asserts the two agree: a coin toss cannot be caught by
looking at one flip. Any later check in this document that lands on a
degenerate case — and the golden set will — wants the same treatment.

## Strengthening weak models

**Measure before trusting.** A committed *golden set* of keyframe pairs with
known-good answers, scored by the verifier, produces a **capability profile**
per provider: schema adherence, betweenness, arc-following, and how many strokes
before it degrades — the number that matters most and that nobody measures.

### The harness is built, and two of its categories were vacuous first

`GoldenSet`, `CapabilityProfiler` and `CapabilityProfile` land the measuring
half. The profile carries schema adherence, label retention, a per-category
headline and the degradation rung; it has **no overall pass or fail**, because
the output is a plan for shaping the request and a boolean cannot carry one.

`FreeEngineArtist` is the piece worth stealing for any later set: the
deterministic engine dressed as an artist, so the whole set runs in CI for no
tokens against a subject whose behaviour on constructed geometry is not in
question. It inverts the direction of suspicion — a constructed pair the free
engine cannot clear is a bug in the *pair*. Q32 is untouched: nothing wires it
into the factory and an artist can never reach it.

**Two categories passed everything and measured nothing, and both were caught
by reading the profile the free engine produced about itself.**

- **Arc.** A chord interpolation is *exactly* between the keys, so betweenness
  accepts it and the row read "clean" for a model that added nothing. Fixed by
  carrying **departure from the free engine's answer** in the row — reported,
  never thresholded, per Q33. That is also the first use of the "free two-sided
  signal" this document has always claimed and never spent.
- **Occlusion.** The torso was drawn whole in *both* keys, so nothing was ever
  hidden and no reveal was ever required. Fixed by drawing it in two pieces in
  key A, with the arm across the gap.

The general form, and the reason to expect more of it: **a category that cannot
fail is worse than a missing one**, because it reports a pass. The cheapest way
to find them is to run the set against the free engine and read every row as a
claim about the engine — a row that says "clean" about a subject you know to be
weak in that respect is a row that is not measuring.

**A third thing the free engine caught: the label metric was wrong.** Scored
against key A's labels, it marked a model down for dropping a label on a stroke
that is *supposed* to disappear — the occlusion pair's `torso-lower` has no
counterpart and correctly fades by the midpoint, and the free engine scored 95%
on its own output. The denominator is labels present in **both** keys.

Still open, and both need something this branch could not supply: the surface an
artist presses (with the cost shown before it is spent), and the hand-drawn
`Organic` pairs. The category ships declared and empty, and every profile prints
it as *not measured* — a known gap rather than a silent one.

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

### Repair — built, and the interesting part is what it must not do

`InbetweenRepair` is the loop: ask, verify, re-ask the refused frames with each
fault named and **the model's own rejected drawing handed back**, bounded at two
re-asks (Q85), then refuse for good. Q32 is untouched — nothing here relaxes a
check, and a repaired frame clears exactly the same bar as a first-attempt one.
Repair changes how many frames clear it, never where it is.

Four things worth carrying, three of which only appeared once it existed:

- **The fault is only actionable with the drawing attached.** The refusal names
  a stroke — *"the ‘near-arm’ did not stay between the keys"* — in a drawing the
  model can no longer see. Without its own answer back, the re-ask asks it to
  redraw from the keys with a hint, which is a blind retry wearing a sentence.
  With it, the re-ask is an edit. That is the difference the phase is for, and
  it is **measured rather than assumed**: on the 40-stroke pair
  `DESIGN-ai-payload.md` uses throughout, a first ask is 102.1 KB and a repair
  carrying one rejected frame is 153.3 KB — 1.50×, with a worst case of 4.01×
  a single ask across three calls. `ARepairReAskCostsAboutHalfAgain_NotAWholeSecondRequest`
  keeps it under 2×, because a repair that costs a whole second request has
  stopped being a correction.
- **A repair must never cost a frame that was already accepted.** Accepted
  frames are carried into the next round untouched, so the *only* way one can
  newly fail is the coherence check — a repaired neighbour that makes it jitter.
  Counting accepted frames would happily adopt a round that gains two and loses
  one; the artist experiences that as a frame they were given and then had taken
  away. The rule is set-inclusion, not arithmetic: adopt only when nothing
  accepted is lost *and* something is gained.
- **The whole run is re-verified every round, never just the repaired
  frames.** This falls straight out of the verifier judging a run — coherence is
  a property of neighbours, so a repaired frame can only be checked against the
  frames it will actually sit between. Verifying the repair alone would let the
  loop insert the boil it exists to catch.
- **A failed re-ask is not a failed run.** A rate limit on the second call must
  not throw away the frames the first call earned. They are still defensible;
  the loop stops and reports what it has.

### What the G12 pair found, because both reviewers found the same two things

The first cut passed its tests and shipped two defects that only a reviewer
reading the *prompt* rather than the code would see. Both `ai-engineer` and
`art-director` reported them independently, which is the strongest signal the
pair produces.

- **The re-ask claimed consistency with frames it never sent.** The repair block
  said *"the frames not listed here were accepted — keep your corrections
  consistent with them"* and the payload carried only the keys and the rejected
  drawing. Every provider call is one stateless turn, so the model had nothing
  to be consistent *with*. This is rule 4 backwards: a sentence asserting a fact
  the payload does not support, which asks a model to guess and then measures
  whether it guessed right.

  It matters most for exactly one refusal. `InbetweenFault.Incoherent` — *"it
  jitters against the frames beside it"* — is the only fault **defined against
  other frames**; every other one names a stroke and a distance that the keys
  and the model's own drawing fully explain. So the accepted neighbours now ride
  along on a coherence repair and on nothing else (2.50× a first ask against the
  ordinary 1.50×), and the sentence is only said when the data is there.

  The general form is worth more than the fix: **a fault can only be repaired if
  the re-ask carries what the fault is measured against.** That is a design
  constraint on any future check, not a bug in this one.

- **A discarded round asked the same question again, verbatim.** When a round is
  thrown away the loop's state is unchanged, so the next request rebuilt
  byte-identically — the engineer confirmed it by instrumenting the run. Q85
  bought two re-asks on the argument that the second catches a model which fixes
  the named fault and trips a different check; a verbatim resend cannot catch
  anything, and for a low-temperature model it is a guaranteed repeat. The fault
  text now carries what happened to the last correction — *"discarded because it
  broke the frame(s) at t=0.3, which had already been accepted and cannot
  change"* — so the third call is a different question.

And one the director alone raised, which is the expression veto doing its job:

- **Repair gives a model a reason to flatten onto the deterministic chord.**
  Zero deviation passes every geometric check by construction and cannot trip
  the jitter check either, so the safest way past a repair is to stop having an
  opinion. Q33 says that is a note and never a veto — and the note was reaching
  `RepairedFrame.Notes` and being read by nothing, which makes "corrected" and
  "gave up and copied the free engine" identical from the outside. It is a
  counted value now (`MatchedFreeEngineCount`) and the status line says it.
  Nothing about the bar changed; the instrument did.

**Two things about where the loop is wired, both of which look like omissions
and are not.** Repair lives *above* `IAiArtist` — the interface still has one
inbetweening method, and the fault travels as an optional field on the request
that `Prompts.InbetweenUser` renders — so **every provider got repair for free**
and none of them knows it exists. That was the reason to put it there rather
than adding a `RepairAsync` the six providers would each have to implement, and
it is the same argument the interface's own docstring makes about staying one
method.

And **`CapabilityProfiler` and `AiConnectionTester` deliberately do not use
it.** Both exist to measure a model, and a profile taken through the repair loop
would grade the harness rather than the subject — the degradation rung would
move because Lightbox tried harder, not because the model coped. A connection
test is the same in miniature: certifying a model that only passes on its third
go tells an artist the wrong thing about what they are about to depend on.

**And one thing Phase 3 was expected to close and did not.** Q83.3 left the AI
path asking one gap at a time while the deterministic path fills a whole run,
on the grounds that piecewise betweenness was *"the natural thing for Phase 3 to
close when the verifier is being changed anyway"*. The verifier was **not**
changed: repair is a loop built around it, and it turned out to need nothing
from inside it. So that disagreement is still open and moves to Phase 4 on its
own merits rather than as a free rider — which is the better outcome, because it
now has to justify its own cost rather than being smuggled in beside something
else.

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
| **1** | Matcher: shape cost, breakdowns as constraints — **done** (deterministic path; the AI path keeps per-gap requests, Q83) | Deterministic; improves the non-AI path too |
| **2** | Golden set + capability profile — **done** apart from the hand-drawn organic pairs | Makes reliability a number |
| **3** | Repair loop — **done** (`InbetweenRepair`, Q85) | Needs specific findings first |
| **4** | Adaptive shaping, best-of-N, authored wind, piecewise betweenness (Q83.3) | Needs the profile |

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
