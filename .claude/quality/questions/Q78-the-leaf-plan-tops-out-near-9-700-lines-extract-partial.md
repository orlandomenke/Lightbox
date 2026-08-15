# Q78 · The leaf plan tops out near 9,700 lines — extract, partial-split, or accept? — **answered 2026-08-13: finish the Tier 1 leaves, against the recommendation**

**Asked after five steps of decomposition moved `MainViewModel.cs` from 13,110 to
12,878 — 1.8% — and the owner asked whether the file staying humongous is a
problem.** It is, and the measurement is what makes it a real question rather than
a mood:

- The file is **7,492 code lines**, 4,140 comment, 1,247 blank. At 32% comment it is
  *below* the repository's 40% average, so "it is heavily documented" is not
  available as an explanation.
- **Every Tier 1 leaf extracted would leave 9,676 lines.** All ten, each its own
  branch with tests and a `leak-hunter` pass.
- The reason is structural: it is not one monolith but **61 sections sharing a
  scope**. The largest is 764 lines and there is a tail of 43 sections totalling
  4,743. Leaves come out at 150–590 lines each, which cannot outrun the total.

**The recommendation was to partial-split the view model now**, applying the half of
Q76 that has only ever been used on the view: measured, **52 of the 61 sections
(9,089 lines) move with no grouping at all**, 36 fields stay in the root, and 9
sections need a sibling. That is the same shape as `MainWindow.axaml.cs`, which went
5,544 → 429 in one branch with the class body proven byte-identical. Tier 0 is what
made it cheap — the 22 `_live*` fields and 7 publish fields are now behind two root
references instead of spread across those sections.

**The owner chose to finish the Tier 1 leaves first instead.** What that buys:
genuine decoupling rather than file boundaries, each leaf landing as a real
collaborator with its own guard, in the manner of `SelectionManager`,
`LivePaintSession` and `PublishState`. Splitting a section into a partial moves it
without giving it an owner; extracting it gives it one, and the three Tier 0
extractions are the evidence that the owner is where the value is.

**What that choice costs**, recorded because it should not have to be rediscovered:

- **Ten more branches, and the file is still ~9,700 lines at the end of them.** The
  answer to "is it small enough now" will be no.
- **Every one of those ten is authored inside a 12,000-line file**, which is the
  condition the split would have removed first. The partial split would have made
  each subsequent leaf a change to an 800-line file instead.
- **The order is not reversible for free.** Extracting a leaf and then partialling
  what remains is fine; partialling first would have made each extraction smaller to
  review. Doing it second means the ten reviews are the expensive kind.

The partial split is not refused, only deferred — it remains the move that answers
the size question, and this entry is what stops it being re-litigated from scratch.

**Q78's leaf pass finished at two AI-path extractions, reviewed by the G12 pair.**
`ConfiguredArtist` took the four provider fields (`_artist`, the two labels, the
enabled flag) and the one operation that sets them together; `ReferenceViewImages`
took the reference-view PNG cache, the render, the downscale and the 768 px request
cap. `MainViewModel.cs` 12,852 → 12,736.

**`ai-engineer`: CLEAN.** Verified member-by-member against `HEAD` — same disposal
order, cap applied at exactly one site, invalidation still inside `MarkDocumentEdited`
rather than `OnDocumentChanged`, no seed/clock/ordering introduced, and the in-flight
`CancellationTokenSource` correctly left on the view model so a provider swap
mid-request cannot inherit the previous request's cancellation.

**`art-director`: ACCEPTABLE, with one finding that was right and is now fixed.** The
extraction copied the sentence "Line art survives the downscale" into the new class's
header — a claim `docs/DESIGN-ai-payload.md` already contradicts: face close-ups
rendered through this exact path at 768 lose eyebrows and turn eyes to grey smudges,
because mipmapped minification greys a thin dark line toward the ground. **Q27 is
answered (d) — choose the cap per view — and this refactor had quietly given a flat
768 a more authoritative-looking home than it had before.** The remarks now carry the
failure mode, name Q27 as the settled answer, and record its three conditions (the
cap is shown per view, a view can be pinned, the heuristic is a pure function of the
view). Q27's heuristic is still unbuilt; this is the placeholder saying so.

**The lesson worth keeping:** a pure code move can still make a claim worse, because
moving prose into a smaller, better-named file makes it read as more settled than it
was. Neither the compiler nor the suite can see that, and it is what the pair is for.

**One pre-existing issue surfaced and deliberately not fixed here.** `ai-engineer`
noted that `_ai.Artist` is dereferenced inside the request lambdas without
re-narrowing after the `is null` guard, so a `ReloadAiProvider()` landing between the
guard and the lambda's invocation would throw. Identical in shape to the code before
this refactor, so not introduced. **Not fixed because it needs a decision, not a
patch:** capturing the artist at the guard makes a request that started before a
provider swap finish on the old provider, while dereferencing late makes it finish on
the new one, and which is correct is a question about what a provider swap means
mid-request rather than about null-safety. That is the "needs a decision" row of the
fix-rather-than-file rule, and it belongs in its own branch with its own question.

**The deferred half of Q78 was then done, and it is what answered the size question.**
`MainViewModel.cs` 12,749 → **655** lines across 19 partials, in two separately
verified steps.

**Step A hoisted 33 shared fields to the root**, giving the split its one rule: a
section's own state travels with it, shared state does not move. **Step B split 61
sections into 19 files** — with the shared state hoisted, union-find over what remained
returned 61 *independent* groups, so the grouping was chosen by concern rather than
forced by coupling.

**The threshold was the whole difficulty.** At "a field crossing three or more sections
stays in root", union-find chained 16 sections into one 4,500-line group, because a
field shared by exactly two sections links them and the links form chains. Lowering it
to "more than one" moved 37 → 54 fields into the root and broke every chain. **That is
the trade the split makes visible rather than removes:** 54 of 114 fields are read from
two or more places. They are now in one marked block instead of scattered through
12,000 lines, which is the honest measure of how coupled this class still is.

Verified as the view split was: coverage with no gaps or overlaps, every marker at
brace depth 1 so no member was cut in half, and the class body **identical as a
multiset of lines** against HEAD — 11,454 non-blank before and after, the only
additions being ten comment lines.

**The nineteen partials are deliberately not given ratchet budgets, and the objection
to that is recorded in the test.** Growth will now land in whichever partial owns the
feature, so the mechanism that capped it has nothing to cap. Kept anyway because that
destination is the split working rather than leaking, and because the largest partial
is 1,310 lines — a file a person can read. Pre-emptively budgeting nineteen readable
files looks like discipline and is noise. Add one when a file stops being readable,
with the number that made it necessary.

**What the whole exercise cost and bought**, since the leaf-versus-split ordering was
argued twice: the leaf pass produced three collaborators (`GuideSnap`,
`ConfiguredArtist`, `ReferenceViewImages`) and moved the file 12,878 → 12,852 → 12,736
— about 0.9%. The split moved it 12,749 → 655 in one branch. Both were worth doing and
the order was wrong: had the split come first, each of the three leaf extractions would
have been a change to an 800-line file instead of a 12,000-line one. That cost was
stated when the ordering was chosen and is recorded here as having been real.

**All five collaborators were re-applied on top of main (52 commits, PR222 included),
and two of them are better for it.** PR222 rewrote the live-post pipeline and added
publish pacing — the code Tier 0 had extracted — so `RenderLivePostProcess` no longer
exists upstream. Rather than route 41 hunks (two of them rewrites, +195/−39 and +112)
into nineteen partials, the merge took main's files verbatim and the restructure was
re-derived on top: PR222's behaviour is intact by construction, which is the only claim
that is cheap to check.

`PublishState` absorbed `_presentedSeq`, `_publishWhenPresented`, `_lastPublishTicks` and
`_damTimerArmed`. They belong with `_publishSeq` rather than beside it — `CanvasIsBehind`
compares three at once, and a deferral released twice puts a second frame in flight,
which is what the pacing exists to prevent. `NotePresented` and `TakeDeferral` clear the
flag inside the state so "released" and "flag down" cannot come apart.

`LivePaintSession` absorbed `_livePostGeneration`, and the bump moved inside
`ResetPostProcess` where PR222 had it. The only thing that invalidates in-flight work is
this state being reset, so the two must not be separable.

**The split went first this time**, which is the Q78 lesson applied: each extraction was
a change to an 800-to-1,800-line file rather than a 13,000-line one, and the difference
was obvious in how quickly each one landed.

Final: `MainViewModel.cs` 13,628 → 692 across 18 partials; `MainWindow.axaml.cs`
5,706 → 455 across 15. 4,191 tests green, PR222's own guards included.
