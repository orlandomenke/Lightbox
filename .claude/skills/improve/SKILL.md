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
python3 scripts/roadmap.py check
python3 scripts/bugs.py check
python3 scripts/bugs.py next           # what is known broken, worst first
python3 scripts/roadmap.py next        # candidates, nearest-first
python3 scripts/bench.py should-run    # is the performance map worth re-running
```

`BUGS.md` comes before `ROADMAP.md` for the same reason a broken promise
outranks a new feature: the roadmap is what we intend, the ledger is what is
already wrong.

**If `bench.py should-run` exits 0**, hand the sweep to **perf-warden** as part
of this round. It takes minutes, so it is asked here rather than per build —
and it is asked *here*, at the start of a round, rather than on a nightly
timer, because a sweep produces a decision and a decision needs a decider. A
verdict that lands at 3 a.m. and is read on Thursday has cost the same and
bought less. `should-run` is a git diff against the watched paths, so it costs
nothing on a round that never touches rendering.

Read `.claude/quality/LOOP.md` for what previous rounds already tried, so you
do not re-report known gaps, and `.claude/quality/DECISIONS.md` for what is
already settled, so you do not re-litigate it. The journal is per-round and
perishable; the decisions are neither.

`ROADMAP.md` is where the work comes from when nothing is broken. Its marks
are derived from the code, so `next` is a real answer to "what is closest to
done", not a wish list. `[?]` items are unspecified rather than unstarted —
they need a definition before they need an implementation.

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

**ui-critic** is not in this list either: like leak-hunter it reads a diff, so
it belongs in verify.

Skip any agent whose question is already answered. Three agents that all
report "nothing found" is a wasted round; pick the ones the recent diff makes
relevant. **leak-hunter** is not in this list because it belongs in verify —
it reads the diff you are about to ship, which does not exist yet.

### 2 · Decide

Rank candidates by **risk removed ÷ cost**. Prefer, in order:

1. A broken promise (something that used to work and does not).
2. An open **P1** in `BUGS.md` — by definition it blocks work or damages art
   every session, which nothing on the roadmap does.
3. An unguarded hotspot — top of `HOTSPOTS.md` with no test reference.
4. A confirmed performance regression.
5. A gap the user has actually felt (check the recent conversation and
   `QUESTIONS.md`) over one only the metrics feel.
6. Simplification that deletes code without deleting behaviour.

Take a batch that fits one verification cycle — roughly what you can build
and fully verify before reporting. Two solid improvements beat six unverified
ones. Anything blocked on a decision goes to `QUESTIONS.md` and is skipped,
not guessed.

### 3 · Improve

Make the change. Delegate test writing to **test-smith** when the gap is
"this is untested"; keep implementation on the main thread where you hold the
context.

**Sweep the domain you are already in.** Before finishing a change, run

```bash
python3 scripts/bugs.py mine <domain>
```

for the area the diff touches. Fix its open **P1 and P2** bugs too, each with
its own regression test, in the same commit — you are already holding the
context that makes them cheap, and a known-broken thing left alone in code you
just edited is a choice, not an oversight.

**Mention P3 and P4 without touching them.** The bound is what makes the sweep
safe: a request to change one thing must not come back as a diff touching six.
Anything needing a product decision goes to `QUESTIONS.md` and is left alone.

Close what you fixed with `python3 scripts/bugs.py sync` — the mark is derived
from the test, so this is reporting the result rather than asserting it.

### 4 · Verify — the gates

Run in this order, cheapest first:

```bash
dotnet build Lightbox.sln
dotnet test
dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"
python3 scripts/codemap.py build     # refresh FEATURES.md
python3 scripts/roadmap.py sync      # tick what this round actually landed
python3 scripts/bugs.py sync         # close what its regression test now proves
```

Then, in parallel:

- **leak-hunter** on the round's diff. This is gate **G7** and it is not
  optional, because G4 only covers paths that already have a budget and every
  real stall in this project was in a path that did not. A leak it names is
  fixed, or accepted in the commit message with a measurement — never
  ignored.
- **ui-critic** on the round's diff, whenever it touched XAML, a docker, a
  dialog or a row template. It checks the change against
  `.claude/quality/DESIGN.md` — control sizing, button consistency, docker
  density. A BLOCKING verdict means a control or docker is unusable and the
  round is not done.
- **ai-engineer** *and* **art-director**, together, whenever the round touched
  `src/Lightbox.Ai`, the MCP surface, a prompt, or an AI path in the view
  model. This is gate **G12**, and it is a pair on purpose: one of them owns
  what is sent and what it costs, the other owns whether what comes back is
  worth keeping, and either alone fails in a predictable direction. Spawn them
  in the same message — ai-engineer's `FOR ART-DIRECTOR` block and
  art-director's `FOR AI-ENGINEER` block are how they hand work across, and
  reading both at once is the point. ai-engineer's BLOCKING fails the round;
  art-director's REJECTED is answered with a change or with a question in
  `QUESTIONS.md` naming what would settle it — never by overruling it.
- **adversary** on every claim you intend to report, one claim per agent. A
  claim it refutes is not a finding and not a fix — go back to step 3 or drop
  it.

If leak-hunter reports a changed hot path with no budget, add one before
moving on. That line is how the *next* leak gets in.

Gates G1–G8 in the charter are pass/fail. A round that fails one is narrowed
or reverted; it is never reported as "mostly done".

### 5 · Sharpen — fix the gate, not just the defect

Whenever the round fixed something **the gates should have caught and did
not**, the round is not finished until the gate is fixed too, in the same
commit. This is the loop improving itself rather than only the code, and it is
the difference between a process that converges and one that keeps rediscovering
the same class of bug.

Ask it explicitly, every round: *what would have caught this earlier?*

| The round found | The gate to sharpen |
| --- | --- |
| A stall in a path with no budget | Add the budget (already mandatory above) |
| A behaviour with no test | Add it to the inventory, not just a test |
| A defect the user hit before any agent did | An agent's prompt is missing a question — add it |
| A UI control that drifted | A rule in `DESIGN.md`, or a check in **ui-critic** |
| A roadmap box that was green and wrong | The evidence anchors were too weak — tighten them |
| A question re-litigated from an earlier round | It belongs in `LOOP.md`'s Rejected line |

Two limits, so this does not become its own busywork:

- **Only when the round actually revealed the gap.** Do not audit the tooling
  speculatively; that is a different task and the user can ask for it.
- **A sharpening must be as concrete as a fix.** "Be more careful about
  dockers" is not one. A new line in `DESIGN.md`, a new question in an agent's
  prompt, or a new gate in `CHARTER.md` is.

Record it on the round's `Sharpened:` line so the next round can see what the
system learned.

### 6 · Reflect

Append to `.claude/quality/LOOP.md`:

```markdown
## Round <n> — <date> — <one-line theme>
Found: <what the assessment turned up>
Did: <changes, with the test that proves each>
Rejected: <candidates considered and dropped, with the reason>
Gates: build ✓ tests ✓ (<n> passing) perf ✓ leaks ✓ inventory ✓ roadmap ✓
Sharpened: <what in the loop itself changed so this class of defect is caught next time, or none>
Bugs: <ids closed this round, ids newly recorded, or none>
Roadmap: <items that changed mark this round, or none>
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
- **Do not re-derive settled facts.** `LOOP.md`, `DECISIONS.md` and
  `CHARTER.md` exist so each round starts from the last one's conclusions.
