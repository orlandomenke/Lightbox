---
name: improve
description: Runs a self-directed audit → fix → verify loop over the codebase. Guards existing behaviour, expands test coverage, watches performance budgets, and records the questions it cannot answer itself. Use when asked to improve, harden, audit, or tidy the project, or when handed a broad request that needs decomposing before work can start.
---

# The improvement loop

Work in rounds. Each round leaves the repository better and provably not
worse, or it is reverted. Stop when the work runs dry, not when it runs long.

**Read first, once per session:** `.claude/quality/CHARTER.md` (gates and
objectives) and `.claude/codemap/HOTSPOTS.md` (where risk lives). Do not read
the codebase to orient yourself — that is what the index is for.

## Round structure

### 0 · Orient (cheap)

```bash
python3 scripts/codemap.py stale || python3 scripts/codemap.py build
```

Read `.claude/quality/LOOP.md` for what previous rounds already tried, so you
do not re-litigate settled decisions or re-report known gaps.

If the user gave a request this session, run **story-analyst** on it first.
Build what needs no decision; everything else becomes a question.

### 1 · Assess — in parallel, in subagents

Spawn these together in one message so they run concurrently. They read a lot
and return little, which is the entire point: their context is not yours.

| Agent | Question it answers |
| --- | --- |
| **feature-guard** | Is anything we promised now unproven or broken? |
| **perf-warden** | Do the budgets still hold, and what got slower? |
| **code-scout** | Where does the area we are about to touch actually live? |

Skip any agent whose question is already answered. Three agents that all
report "nothing found" is a wasted round; pick the ones the recent diff makes
relevant.

### 2 · Decide

Rank candidates by **risk removed ÷ cost**. Prefer, in order:

1. A broken promise (something that used to work and does not).
2. An unguarded hotspot — top of `HOTSPOTS.md` with no test reference.
3. A confirmed performance regression.
4. A gap the user has actually felt (check the recent conversation and
   `QUESTIONS.md`) over one only the metrics feel.
5. Simplification that deletes code without deleting behaviour.

Take a batch that fits one verification cycle — roughly what you can build
and fully verify before reporting. Two solid improvements beat six unverified
ones. Anything blocked on a decision goes to `QUESTIONS.md` and is skipped,
not guessed.

### 3 · Improve

Make the change. Delegate test writing to **test-smith** when the gap is
"this is untested"; keep implementation on the main thread where you hold the
context.

### 4 · Verify — the gates

Run in this order, cheapest first:

```bash
dotnet build Lightbox.sln
dotnet test
dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"
python3 scripts/codemap.py build     # refresh FEATURES.md
```

Then send every claim you intend to report to **adversary**, one claim per
agent, in parallel. A claim it refutes is not a finding and not a fix — go
back to step 3 or drop it.

Gates G1–G6 in the charter are pass/fail. A round that fails one is narrowed
or reverted; it is never reported as "mostly done".

### 5 · Reflect

Append to `.claude/quality/LOOP.md`:

```markdown
## Round <n> — <date> — <one-line theme>
Found: <what the assessment turned up>
Did: <changes, with the test that proves each>
Rejected: <candidates considered and dropped, with the reason>
Gates: build ✓ tests ✓ (<n> passing) perf ✓ inventory ✓
Questions raised: <ids, or none>
Next: <the strongest remaining candidate>
```

The **Rejected** line matters as much as the others — it stops the next round
rediscovering the same dead end.

## When to stop

Stop and report when any of these is true:

- **Dry:** two consecutive rounds produced no candidate above "minor".
- **Blocked:** everything left needs a user decision. Report the questions.
- **Budget:** the user set a scope and it is spent.
- **Gate stuck:** a gate fails for a reason you cannot fix without a decision
  — report it, do not paper over it.

Do not stop merely because a round succeeded, and do not continue merely
because you can find something to change. "Satisfied" means the gates hold
and nothing above minor remains.

## Reporting to the user

Lead with what changed and what it means for them. Then the questions, as a
short numbered list with recommendations — they should be answerable in one
line each. Keep the round-by-round detail in `LOOP.md`; the user gets the
conclusion, not the journal.

## Token discipline

These are the habits that make the loop affordable to run repeatedly:

- **Query the index, do not grep the repo.** `codemap.py find` answers "where
  is X" for a few hundred tokens; a repo-wide grep plus reading three
  candidate files costs thousands.
- **Read regions, not files.** The index gives line numbers.
  `MainViewModel.cs` is 3000+ lines; you almost never need all of it.
- **Delegate reading to subagents.** An agent that reads twenty files and
  returns twelve lines has spent its own context, not yours.
- **Run one command that answers the question.** `dotnet test` once beats
  four filtered runs.
- **Do not re-derive settled facts.** `LOOP.md` and `CHARTER.md` exist so
  each round starts from the last one's conclusions.
