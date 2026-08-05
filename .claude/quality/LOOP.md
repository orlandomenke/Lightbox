# Loop journal

One entry per improvement round. The **Rejected** line is not filler: it is
what stops the next round rediscovering a dead end and spending a full
assessment on it.

## Round 0 — 2026-08-01 — the loop builds itself

Found: no agent infrastructure existed. Every session re-derived the layout of
a 20k-line solution by grepping, and nothing guarded the behaviour inventory
between milestones — the docker-reopen bug survived one "fixed" report
because no test held that promise.

Did:
- `scripts/codemap.py` — indexes 125 files into symbols with line numbers,
  dependency edges, git churn, fix-churn, and test linkage; `find`/`file`
  queries replace repo-wide grep.
- Hotspot scoring: heat (churn × fix-churn × centrality × size) discounted by
  test coverage, so "risky to change" is ranked rather than guessed.
- Six subagents (code-scout, feature-guard, test-smith, perf-warden,
  adversary, story-analyst), each returning conclusions instead of file dumps.
- `/improve` skill (single round, main thread) and an `improve-loop` workflow
  (multi-round, adversarial, parallel).
- Charter with six pass/fail gates and the measured performance budgets.

Rejected:
- Roslyn-based indexing — accurate but needs a build step and a package;
  regex parsing is good enough for an index whose job is to point at line
  numbers, and it runs in about a second.
- A PostToolUse hook recording edited files — git already answers that.
- Committing `map.json` — 350 KB of generated churn in every diff. The three
  markdown views are committed instead; the JSON regenerates in ~1s and the
  SessionStart hook does it in the background.

Gates: build ✓ tests ✓ (339 passing) perf ✓ inventory ✓ (baseline established)
Questions raised: Q1–Q5, all blocking pending backlog items.
Next: answer Q1–Q5, then the first real round should start with the hotspot
list — `MainWindow.axaml` and `MainWindow.axaml.cs` carry 21 commits and 12
fix-commits between them with almost no test coverage.

## Round 1 — 2026-08-02 — the stroke walks the curve it was drawn as

Found: **M16b** was real and had two independent causes, only one of which is
worth fixing. Measured on a 180 px arc rendered with a 60 px brush, the outer
edge of a bend scallops by (a) the *chord error* — the dab walk followed
straight lines between recorded points, putting the path 2.7 px inside the
curve at 20° between samples and 6.1 px at 30° — and (b) the *dab envelope*,
`R − √(R² − (step/2)²)`, which is 0.34 px at the default spacing and 4 px at
spacing 0.5. Only (a) is a defect: coarse spacing is a spatter brush asking for
exactly what it gets, and an artist cannot ask their pen to report faster.

Straight strokes measured clean throughout (ripple ≤ 7/255, zero edge wobble),
which is why this survived — **every brush pixel test in the repository drew a
straight line.**

Separately, `feature-guard` found the canvas-relief feature had its decision
tested exhaustively and its effect not at all.

Did:
- `GeometryOps.Densify` — centripetal Catmull–Rom through the recorded points,
  subdividing any span over 2 px. Interior chord error 0.685 → 0.007 px at 10°
  and 2.735 → 0.064 px at 20°. `DensifyTests` (10), `StampingArcTests` (5).
- Corner detection at a 60° turn, so a drawn rectangle keeps square corners.
  The tell is *overshoot*, not rounding-in: a cubic through a right angle
  bulges past the vertex, and the guard pixel goes 0 → 255 without it.
- A fast path returning the caller's own list when no span needs subdividing,
  so the live-painting path allocates nothing on an ordinary stroke.
- `CanvasQualityEffectTests` (4) — Half publishes a smaller surface, the
  snapshot still reports the document's size, and an export is full resolution
  whatever the canvas is set to.

Rejected:
- **Clamping the dab step** so the envelope error stays sub-pixel. It would fix
  cause (b) and break every spatter and stipple brush, whose coarse spacing is
  the point. Cause (b) is not a defect.
- **Reflecting the missing end control** (`p0 = 2·p1 − p2`, the textbook end
  condition) instead of duplicating it. Measured worse — 2.9 px → 3.8 px on the
  end spans — because a reflected control puts the tangent on the chord of the
  wrong side of the vertex. The end spans running straighter is correct: the
  record does not say how the curve was continuing past its last point.
- Densifying **inside** `StrokeStabilizer` rather than at the dab walk. The
  smoothed points are the record; inventing points into it would make the file
  claim the pen reported places it never did.

Gates: build ✓ tests ✓ (1395 passing, +19) perf ✓ leaks ✓ (G7 CLEAR)
inventory ✓ roadmap ✓ bugs ✓ · G9 not applicable, no XAML in the diff.

Sharpened: two objectives in `CHARTER.md`, both from defects this round found
that the gates should have. **O7 — test what a setting does, not that it
changed**, from canvas relief passing every branch test while nothing read the
output. **O8 — draw a curve, not just a line**, from M16b hiding in the one
case where cutting a corner costs nothing.

Bugs: none closed, none newly recorded. B17 and B8 remain open and
`evidence: manual`.
Roadmap: "A fast curve is stamped along the curve, not the chords between pen
samples" added and green; "Smudge and blur sample all layers, live or frozen"
landed in the previous commit.
Questions raised: none.
Next: **B17** (guides invisible over the drawing, P2) is the oldest open thing
and needs a pair of eyes rather than a test. After that the thin pillars are 4
(14%), 5 (21%) and 6 (24%) — pillar 4, animation-aware drawing tools, is the
one that most changes what the app is for.

## Round 2 — 2026-08-04 — B39 was reachable after all

Found: B39 (P1, open since round 1) had eight clean measurements against
`BrushEngine` and a note saying "no headless test can reach it." That note was
wrong about the layer, not about the method — nothing had yet driven the
artefact through the App's actual compositing (`_liveComposite`, the
overlay-selection code, `SceneRenderer`) rather than `BrushEngine.StampStroke`
directly. feature-guard and an independent read of `StampSmudge` both also
surfaced a second, unrelated defect: `outputScale` applied to the canvas
transform *and* manually multiplied inside `LerpDab`, landing a Smudge dab at
`outputScale²`. Dormant today — nothing in the app renders an effect brush
above 1x — but real. perf-warden's sweep found no regression in the 30 files
changed since the last baseline; two apparent cliffs were confirmed as this
container's measurement noise (reproduced identically on unmodified old code
in a worktree) rather than code, and the baseline was refreshed.

Did:
- **B39** (`BrushEngine.cs` was innocent; `MainViewModel.cs` was not). Root
  cause: `BeginStroke` never cleared `_liveScratch` when starting a Blur/Smudge
  stroke, so whatever ordinary stroke ran immediately before left its dabs
  sitting there. The publish path correctly substitutes `_liveComposite` for
  the layer bitmap on an effect-brush stroke, but the overlay-selection code
  below it didn't know that substitution had happened, so it built a
  `StrokeOverlay` from the stale `_liveScratch` anyway and `SceneRenderer`
  composited it a *second* time, `SrcOver`, on top of content `_liveComposite`
  already carried once — `a + a·(1−a) ≠ a` for partial alpha. Measured through
  the real pipeline (not `BrushEngine` directly): a wash at alpha 61 read back
  at 108 mid-drag, exactly that arithmetic. Fix: the overlay-selection branches
  now only run when `_liveComposite is null`. `AnEffectBrushMidDrag_
  CannotExceedTheWashItStartedFrom` (`LiveToolPreviewTests.cs`) drives
  `BeginStroke`/`MoveStroke`/`EndStroke` on a soft wash — not a bar, the same
  discriminator the Raster-level tests needed — and reads the published
  snapshot, both mid-drag and after release.
- **B57** (new, found and closed the same round). `StampSmudge` wrapped its
  dab loop in `target.Scale(outputScale)`, matching every other stamp path,
  but `LerpDab` already converts every coordinate to device pixels by hand —
  it has to, since it writes through raw pixel spans — so the canvas scale
  doubled it. Fix: `StampSmudge` no longer touches the canvas transform.
  `ASmudgeAtHigherOutputScale_LandsInTheSamePlace` (`OutputScaleTests.cs`)
  closes the coverage gap `OutputScaleTests` had for `BrushKind.Smudge`/`Blur`.
- **A hole in the first version of the B39 fix, found by adversary before it
  shipped.** `_liveComposite` was reset only in `BeginStroke`/`EndStroke`;
  anything that abandons a stroke without a commit — `AttachEditor` on a tab
  switch, `StartPlayback` mid-drag — calls `_strokeBuilder.Cancel()`, which
  knows nothing about it. Left non-null, the new guard above would silently
  suppress the overlay for every gradient, shape or brush drag afterward, on
  any document, until an unrelated `BeginStroke` reset it. Reproduced by the
  adversary, confirmed by temporarily reverting the fix and watching a new
  test fail with the exact predicted symptom, then closed:
  `ClearLiveEffectState()` factors `EndStroke`'s existing reset out so
  `AttachEditor` and `StartPlayback` call it too.
  `AbandoningAnEffectBrushDoesNotBlockTheNextOrdinaryStroke`
  (`LiveToolPreviewTests.cs`) guards it — deliberately through the *gradient*
  tool rather than another brush stroke, since `BeginStroke` would have masked
  the gap by clearing `_liveComposite` itself regardless of whether `Cancel()`
  was fixed.
- Refreshed the performance baseline (perf-warden) from the one clean
  pre-contamination sweep run; no code regression found.

Rejected: nothing — this round had one live P1 candidate and it absorbed the
whole round, which the process is supposed to allow ("two solid improvements
beat six unverified ones").

Gates: build ✓ tests ✓ (2405 passing, +4) perf ✓ (budgets pass, baseline
refreshed) leaks ✓ (G7 CLEAR, two passes — the second after `ClearLiveEffectState`
was added) inventory ✓ roadmap ✓ bugs ✓ (B39, B57 closed; ledger has 0 open P1).

Sharpened: none named as a gate change this round — the actual gap was that
nobody had tried the App-level pipeline for B39, not that a gate was missing
one. Worth stating anyway since it generalises: **when a bug is marked "no
headless test can reach it," check whether that is true of the bug or only of
the layer tried so far** before accepting the manual-evidence label. Two of
the last three "manual" bugs (B17, B8, now B39) turned out to be reachable
once the search moved to the layer the artefact actually lived in.

Bugs: B39 and B57 closed this round, both with regression tests. Ledger has
zero open P1s for the first time since round 1 started.
Roadmap: no items changed mark this round — this was bug-ledger work, not
roadmap work.
Questions raised: none.
Next: perf-warden flagged a new unswept dimension — the rig overlay
(`RigOverlay.cs`, landed since the last sweep) draws anchor/collision-shape
count every frame while the rig tool is active, a `WhileDrawing`-cadence path
with no cost-at-scale sweep. Otherwise the highest-priority open bugs are both
P2: B50 (Watercolor brush nearly invisible — art-direction work with a veto
attached, not a code fix) and B29 (full recomposite ~20 ms/layer blocks
playhead dragging, `evidence: manual`).

## Round 3 — 2026-08-04 — the sweep that shouldn't be written

Found: the round was asked for as a sweep — round 2 closed naming the rig
overlay as an unswept dimension on a `WhileDrawing`-cadence path. **The
premise was false in a more interesting way than the sweep would have been.**
Nothing draws the rig overlay per frame because *nothing draws it at all*:
`RigMarks`, `RigEditMode`, `SelectedRigMarkId`, `AddAnchorAt`, `AddShapeAt`
and `HasRig` have no consumer outside `MainViewModel.Rig.cs` and its own
tests — no binding, nothing in `CanvasControl.cs`, no menu item, no
`ShortcutMap` entry. An artist cannot switch the mode on, so an artist cannot
place a socket or a hitbox on the canvas at all.

`ROADMAP.md` marked it `[x]` and said in prose "**The canvas overlay is
built**" — while the collision-shapes item three lines above said "the one
canvas overlay **still to be built**". Both were in the file at once.

Did:
- **Wrote no sweep, and measured instead of guessing whether one was owed.**
  Driving the real view model at 2 / 10 / 50 / 200 / 600 marks, `PressRig`
  costs 0.0025 / 0.0078 / 0.15 / 0.05 / 0.17 ms against a 16 ms frame — no
  cliff within ten times any real character rig, so a scenario in the harness
  would have measured a path nobody can walk *and* found nothing. Allocation
  is the one number that grows cleanly (~0.26 KB per mark per press, 11.6 KB
  at 50, 156 KB at 600) and is worth a look only once the overlay is wired to
  hover. All of it recorded on the roadmap item so the next person does not
  re-derive it. Charter **O9** explicitly allows this — "records in the
  roadmap item why it does not need one" — and this is the first time that
  branch has been taken.
- **B58** (P2, `ui`): the rig overlay is unreachable. Three unresolved
  anchors name the missing pieces — `RigOverlayPainter`,
  `TheRigOverlayReachesTheCanvas`, `RigEditModeIsBindable` — left in place
  rather than dropped, the way `NormalMapPanel` is on the normal-map item.
  `roadmap.py sync` derived the box down to `[~]` from those anchors; the
  mark was not typed.
- Corrected the contradictory prose on the socket-system item.

Rejected:
- **Writing the sweep as asked.** A `WhileDrawing` scenario for a path with
  no per-frame caller measures a hypothesis, and the harness would then carry
  a scenario whose shape changes the moment the overlay is actually built.
  The measurement above is the part worth keeping, and the roadmap item is
  the right place for it.
- **Fixing B58 in this round.** It is a painter, pointer wiring, a menu home
  and a shortcut — and *where the mode lives* is a product decision, not a
  derivable one. Filing it beats guessing at it.

Gates: build ✓ tests ✓ (2405 passing, unchanged — this round touched no
production code) perf n/a (nothing measured changed) leaks n/a (no code in
the diff) inventory ✓ roadmap ✓ (`check` passes; 165 built / 5 partial,
was 166/4) bugs ✓.

Sharpened: **charter O10 — an evidence anchor must name a surface an artist
can reach.** This is the gate that failed: sixteen anchors on the rig item,
every one a type, a decision or a unit test, so the box went green the moment
the logic compiled. It is O7 (*test what a setting does, not that it
changed*) one level up — the whole feature rather than one setting. Every
item claiming a user-visible capability now owes at least one anchor that
fails until the capability is reachable. Worth noting how close this came to
being missed: `RigEditingTests` carries the comment "the canvas is handed an
empty list", describing an integration that has never existed, and it reads
as reassurance.

Bugs: B58 recorded. None closed.
Roadmap: "Hitbox and hurtbox editor" `[x]` → `[~]`; socket-system prose
corrected.
Questions raised: none — B58's open decision (where the rig mode lives in the
UI) belongs to whoever builds it, and is recorded on the bug rather than as a
blocking question.
Next: **B58** is now the strongest candidate and the one with a user-visible
payoff — a built, tested, unreachable feature is the cheapest kind of feature
to finish. After it, the P2s are B50 (Watercolor, art-direction) and B29
(recomposite cost, `evidence: manual`).

**Worth carrying forward:** this is the second round running where the
recorded note about a defect was confidently wrong about *where* it lived —
B39's "no headless test can reach it", and now "an unswept dimension" for a
feature with no per-frame path. Both notes were written in good faith by
something that had looked at the right file and drawn the wrong boundary.
Reading the note is not the same as checking the premise, and both times
checking it took one grep.
