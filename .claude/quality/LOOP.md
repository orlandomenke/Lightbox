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
