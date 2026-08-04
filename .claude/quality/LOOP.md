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

---

# Decisions

Answers to `QUESTIONS.md` entries, recorded here when the question is removed.
The point is the *reasoning*, not the verdict — a verdict alone gets
re-litigated the first time somebody finds it inconvenient.

## Q6 — what a sampled smudge re-reads · answered 2026-08-02 · shipped

**(c): both, chosen per stroke.** Live and Baked are different intentions
about the same gesture — a smudge blending a character into a background wants
to follow the background when it is repainted; a smudge nudged until it looked
right wants to stay exactly as it was — and the app already records intentions
per stroke (invariant 4, the same reason anti-aliasing lives there).
`BrushSettings.SampleSource` is `ThisLayer` (default, and what every stroke
predating this is), `AllLayersLive` or `AllLayersBaked`.

The control sits in **Edit → Configure → Drawing**, not the tool options bar:
it is a decision about how a tool behaves, made rarely, and the options bar is
already the busiest strip in the window.

**Live is built as auto-rebake, not as a render-time cascade.** The question
framed (a) as re-sampling at render time, with the frame cache keyed on the
backdrop's identity. Rejected: that means handing a backdrop to four render
paths — canvas, PNG render, sequence export, sprite-sheet export — that have
to agree forever, and the failure mode is an export that differs from the
canvas with nothing to tell you. Instead `MainViewModel.RebakeLiveSamples` re-
freezes the live strokes at the playhead once per edit, so every render path
reproduces the mark without knowing the layer stack exists. The artist sees the
same behaviour either way.

Consequences accepted:
- The re-bake is **not an undo step**. It is derived from the layers below, not
  authored, and a history with "the background moved" between every real edit
  would be unusable. Undo re-enters the same funnel, so the sample follows the
  document back anyway.
- Only the **playhead's** frames are re-baked. A held cel shown across a range
  whose backdrop differs along it carries one sample and can answer for one
  index.
- With nothing underneath, the sample is **dropped rather than kept**: a stale
  backdrop is a mark blended with something no longer below it, and reading
  its own layer is what the stroke would have done anyway.

Rejected on measurement, not taste: a document-wide "does anything sample?"
guard in front of the re-bake. It walked every cel and every stroke in the
scene, which on a long sequence is more work than the loop it protected, and
it ran on every edit. The per-frame check does the same job at O(layers).

---

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

## Q9 — who owns brush settings · answered 2026-08-02 · shipped

**A variation on (c): global or PER PROJECT, defaulted by project type and
overridable in Configure.**

*Revised the same day, from per-document to per-project.* Per-document fixes
the page you already drew and leaves the next one starting from whatever you
last used elsewhere — the same problem one file later. The brush has to reach
the pages that do not exist yet, and Pillar 1 had already said so: a
character's animations share one palette, one brush set, one set of
references. The store moved from `Doc.Brush` to `ProjectManifest.Brush`, beside
the shared palettes, and `Doc.Brush` was deleted rather than left unused. With
no project open there is nowhere to keep a brush, so the effective scope is
Global whatever the setting says. Neither fixed answer is right, because the two
halves of this application want opposite things from the same control, and the
same person does both.

The case that decides it is not preference, it is memory. Coming back to a
comic page or a game asset after a fortnight, the question is not "which brush
do I like" but "which brush is *this* drawn with" — and on work where the
character of the stroke is part of the style, guessing wrong is visible in the
result. Photoshop and Krita both make you remember; that is the gripe.

Defaults: Illustration, Comic, Game Art and Asset Library keep the brush with
the drawing, because those are documents with gaps between sittings. Animation
and Storyboard keep one brush for the tool, because you are switching
documents constantly and want the same pencil in each. No project open means
Global — what the application has always done.

`Doc.Brush` is a nullable `BrushSettings`, absent from the file by default, and
written **on stroke commit rather than on save**: the session this exists for
is the one that ended without a save. It is not a breach of invariant 4 —
nothing renders from it, every stroke still carries its own settings, and
changing it repaints nothing.

Rejected:
- **A fixed global-with-per-document-override**, the original (c). It makes the
  artist configure the same thing per project when the project type already
  says what kind of work it is.
- **Recording the brush from `AppendExternalStrokes`.** That is the AI and MCP
  path; a stroke the artist did not paint is not the brush they were painting
  with, and letting an agent rewrite the tool bar's memory would undo the point.

Two bugs found while wiring it, both from the same cause — the free-hand
`EndStroke` was missed when a commit-time hook was added to `EndGradient` and
`EndShape`:
- **`FreezeSampledBackdrop` was never called for a hand-drawn stroke**, so
  `AllLayersBaked` — shipped two commits earlier as working — froze nothing and
  fell back to reading its own layer. Live covered for it, because the re-bake
  runs off the edit funnel, so the half with an end-to-end test worked and the
  half with only engine tests did not.
- **Applying a preset reset the sample source**, which made Configure's claim
  that the choice applies to the next mark true only until you changed brush.
  Anti-aliasing was already carried across a preset for exactly this reason;
  sample source was missing from that list.

## B17 and B8 — the two "manual" bugs, both testable after all · 2026-08-02

Both were open only because the code they lived in could not be reached by a
test, and in both cases the fix was to move the decision somewhere a test can
call rather than to accept the label.

**B17** (guides invisible over the drawing) was already fixed in the source and
had been for a while — the draw op painted guides after the artwork,
translucent — but nothing said so, so the box stayed open. `GuidePainter` is
that painting pulled out of `CanvasControl.DrawOp` into pure Skia, and
`PaintDocument` owns the checkerboard/artwork/guides order deliberately,
because splitting those three apart is precisely how the bug happened. Putting
the guides back underneath fails five of seven tests.

**B8** (timeline submenu flickers under a pen) had "cause: not investigated"
and a guess about spurious leave events. The guess was wrong. A pen right-click
is a press-and-hold: the press armed the cel drag, the hold opened the menu,
and moving towards "Insert frame" crossed the six-pixel threshold and started a
drag that seized the pointer and shut the menu. "A mouse is fine" was the
detail that pinned it — a mouse right-click never passes the left-button guard,
so it never arms anything. Two rules now, both in `CelDragGesture` rather than
in a handler: opening a context menu cancels the gesture, and so does letting
go.

Rejected: a source-order test asserting `DrawGuides` appears after `DrawImage`.
It would have caught a reordering, and charter **O2** says tests that assert
internal call order are a liability. Making the order a single function's job
achieves the same thing without the brittleness.

Worth noting for the next round: this is the second time the answer to "no
headless test can reach it" has been an extraction, and the scout report flags
six pointer state machines still sitting in `MainWindow.axaml.cs`. `CelDragGesture`
is one of them; the other five are the same shape.

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
