# The improvement system

Tooling that lets an agent work on this repository without re-learning it
every session, and without quietly breaking what already works.

```
.claude/
  codemap/        generated index — read this instead of searching
  quality/        the standards, the journal, the settled decisions and the
                  open questions
  agents/         specialists that read a lot and report a little
  skills/improve/ the loop: audit → fix → verify → reflect
  workflows/      deterministic multi-agent orchestration
  settings.json   keeps the index fresh at session start
```

## Using it

| I want to… | Do this |
| --- | --- |
| Find where something lives | `python3 scripts/codemap.py find <term>` |
| Understand one file's role | `python3 scripts/codemap.py file <path>` |
| Improve the project for a while | `/improve` |
| Run a deeper multi-round audit | invoke the `improve-loop` workflow |
| See what the app promises today | `.claude/codemap/FEATURES.md` |
| See where change is dangerous | `.claude/codemap/HOTSPOTS.md` |
| Answer a blocked decision | edit `.claude/quality/QUESTIONS.md` |

## Why it is shaped this way

**The index exists to stop re-reading the codebase.** Answering "where does
pressure handling live" by searching costs a few thousand tokens and gets
paid again every session. `codemap.py find pressure` answers it for a few
hundred, with line numbers, the tests that cover it, and who depends on it.
It rebuilds in under a second, so it is never worth reasoning from a stale
copy.

**Hotspots rank risk instead of guessing at it.** Heat combines commit churn,
fix-commit churn, how many files depend on a file, and its size; risk is that
heat discounted by test coverage. The top of that list is where the next
defect will be, and it is derived from this repository's own history rather
than intuition.

**Subagents are a context strategy, not a delegation habit.** An agent that
reads twenty files and returns twelve lines has spent *its* budget. The
assessment phase runs several of them at once precisely because that phase
reads the most and concludes the least.

**Findings are refuted before they are fixed.** Editing is the expensive
part, so every candidate goes to an adversary that defaults to rejecting it.
A confident-sounding finding that dissolves under one command was worth
nothing and would have cost a real change.

**The gates are pass/fail.** A round that cannot build, cannot pass its
tests, cannot hold its performance budgets, or drops a promise from the
inventory is narrowed or reverted — never reported as mostly done. The whole
value of an autonomous loop is that its output can be trusted without
re-checking it by hand.

**Ambiguity becomes a question, not a guess.** Anything where a wrong
assumption means rework goes to `QUESTIONS.md` with options and a
recommendation, and the loop keeps working on the parts that are clear.

## Maintaining it

The index regenerates itself; the charter and the journal do not. When a
performance budget legitimately changes, update the table in
`CHARTER.md` in the same commit as the measurement that justifies it. When a
question is answered, move it to the Answered section with the commit that
implemented it — that record is what stops the same debate recurring.
