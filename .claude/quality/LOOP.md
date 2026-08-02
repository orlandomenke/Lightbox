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
