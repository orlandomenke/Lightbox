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
