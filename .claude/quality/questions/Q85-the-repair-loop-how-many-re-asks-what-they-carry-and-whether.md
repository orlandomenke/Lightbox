# Q85 · The repair loop: how many re-asks, what they carry, and whether they are the default — **answered 2026-08-14, one against the recommendation**

Phase 3 of `docs/DESIGN-ai-correctness.md` is the repair loop: stage 4 of the
pipeline, sitting between *verify* and *refuse*. The design already stated the
principle — *"repair with the fault, not a blind retry"* — and left four things
open that decide what it costs and how it feels. Prompted as one batch, four
answers, three as recommended.

1. **Two re-asks, then refuse** — *against the recommendation of one.* The
   argument for one was that a model which has already ignored a named fault is
   mostly going to ignore it again, and that three calls is 90 to 360 seconds of
   an artist waiting for a frame they may not get. The owner's choice is the
   other reading, and it is a real one: the common failure is not a model that
   cannot draw the frame, it is a model that fixes the fault it was told about
   and trips a different check. One re-ask cannot see that shape at all; two
   can.

   **What it costs, measured rather than guessed** (`ARepairReAskCostsAboutHalfAgain_NotAWholeSecondRequest`,
   on the 40-stroke pair `DESIGN-ai-payload.md` uses throughout): a first ask is
   102.1 KB, a repair carrying one rejected frame is 153.3 KB — **1.50×** — and
   the worst case, two re-asks on a run where everything fails, is **4.01× a
   single ask across three calls**. That is the bill this answer accepts. The
   ratio is bounded below 2 by a budget test, because a repair costing more than
   a whole second request would mean the block had stopped being a correction.

   **What it costs in time, recorded so it is not rediscovered as a bug:** the worst
   case is three full calls to produce nothing, and the artist's only feedback
   during it is the status line. So the loop reports the attempt it is on while
   it runs, cancellation is honoured between rounds, and the refusal names how
   many attempts were spent — *"Nothing was inserted after 3 attempts"* — because
   three calls and one call are very different bills for the same empty result.
   `InbetweenRepair.MaxReasks` is the constant, and it is one number to change if
   the wait turns out to be worse than the frames are worth.

2. **The re-ask carries the fault *and* the model's own rejected drawing.** The
   refusal already reads as a sentence a person could act on — *"the ‘near-arm’
   did not stay between the keys — it sits 60px from where the motion puts
   it"* — but it names a stroke in a drawing the model can no longer see, and a
   model asked to fix a stroke it cannot see has to redraw the frame from the
   keys with a hint. Sending its own answer back turns the re-ask into an edit.
   It costs roughly one extra frame's strokes beside the two keys, on the repair
   call only.

   Declined: **also** sending the deterministic answer as a reference. Q32 and
   Q33 together say the free engine is weakest on exactly the complex organic
   subjects that get refused, so that would risk teaching the model to copy a
   bad reference precisely where the reference cannot be trusted.

3. **On by default**, unlike best-of-N. The brush rule — an expensive option is
   opt-in, deliberate, and never the default — does not cover this case, and the
   distinction is worth keeping: **best-of-N buys a better frame; repair buys a
   frame at all.** The alternative to spending the call is an empty slot. Also
   declined: on for local models and off for metered ones, which makes the same
   document behave differently depending on a setting in another window.

4. **A repaired frame records how many asks it took**, absent unless more than
   one — `AiProvenance.Attempts`, under the same optional-means-absent rule the
   record itself follows. It is the only durable trace: the status line saying so
   is gone by the next action, and *"how often does my model need a second go"*
   is the number that tells an artist whether the model they brought is
   borderline. It is also what the capability profile can eventually report.
   Declined for now: a marker on the cel in the timeline. Useful, and it is
   timeline UI on an AI branch, and the AI-provenance badge does not exist there
   either.

**The guarantee that fell out of building it, and was not in the question.** A
repair can never cost a frame that was already accepted. Accepted frames are
carried into the next round untouched, so the only way one can newly fail is the
coherence check — a repaired neighbour that makes it jitter. A round is therefore
adopted only when every frame accepted before is still accepted *and* at least
one more is; a round that gains two and loses one is dropped whole. Counting
accepted frames instead would take a frame the artist had already been given,
which reads as a bug however the totals came out.
`ARepairThatWouldCostAnAlreadyAcceptedFrameIsNotAdoptedEvenWhenItGainsTwo` is
the test, and it is the one test in the file that fails against the counting
rule.

**And one thing the answer to (2) turned out not to cover, found by the G12 pair
and reported by both reviewers independently.** "Fault plus the rejected
drawing" is enough for every refusal *except one*. `InbetweenFault.Incoherent` —
*"it jitters against the frames beside it"* — is defined against frames the
re-ask does not otherwise carry, so a model told that and shown nothing can only
guess. The first cut made it worse by *saying* "keep your corrections consistent
with the frames that were accepted" while sending none of them.

The extension, decided on the measurement rather than referred back: a coherence
repair also ships the **immediate accepted neighbours**, and no other fault does.
That is 2.50× a first ask against the ordinary 1.50×
(`AJitterRepairCostsMoreBecauseItShipsTheNeighbours`), paid only by the fault
that cannot be stated without it. The general rule it leaves behind is worth more
than the fix: **a fault can only be repaired if the re-ask carries what the fault
is measured against** — a constraint on any check added later, not a bug in this
one.
