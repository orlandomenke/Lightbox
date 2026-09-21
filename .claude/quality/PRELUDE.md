# CLAUDE.md — the per-session prelude

budget: 23076

## What this budgets, and why it is characters rather than lines

`CLAUDE.md` is not read the way a source file is read. It is **loaded into every
session and again into every subagent**, so a paragraph added here is not paid
once — it is paid once per agent per session, for as long as the paragraph
exists. Nothing measured that, so it only ever grew. On 2026-08-24 it stood at
37,909 characters, ~9,500 tokens, and **half of it was two sections**; a six-agent
round therefore spent ~66,000 tokens restating the project to itself before any
agent had looked at a single file.

Characters rather than lines because the cost is tokens, and a reflowed
paragraph changes the line count without changing what a session pays.

`python3 scripts/prelude.py check` enforces it and CI runs it.

**Why this is not in `.claude/quality/ratchets/`.** That directory is line
ceilings for source files that are already too big, and its rules do not
transfer: `MonolithRatchetTests` requires a bare path as the heading and reads
the number as lines, `ratchets.py remeasure` re-measures against the merged
tree, and a budget more than 250 lines above its file fails on purpose. None of
that is meaningful for a prose file whose cost is tokens. Filing it there would
have broken that test on the first run — the budget lives here instead, and
`prelude.py` is the only thing that reads it.

## The rule this exists to protect

> **A rule belongs in `CLAUDE.md`. The incident that produced the rule belongs
> in a skill.**

Almost every section here was a short imperative followed by a long, careful
account of the day it was learned. The account is what makes the rule persuasive
to a reader — and what makes it expensive to the many sessions that were never
going to touch that area. A rule has to be **resident** to be obeyed. Its history
only has to be **reachable**.

So when this budget refuses a change, the question is not "can I make it
shorter". It is *which half is this*. History moves to the matching skill and
costs nothing until something asks for it. If the new text really is a rule every
session needs, raise the number and say why below — that is the ritual working,
not a bypass.

## Why it has moved

Newest last. Both sides of a merge keep their entry — taking one deletes the
other's reason and leaves a number nobody can account for.

- **Seeded at 21,593** (2026-08-24), the size of `CLAUDE.md` after the split that
  created this file: 37,909 → 21,593 characters, **−43%**, with not one sentence
  lost. Six sections moved into five on-demand skills — `branching` (10,817 chars
  of merge and ledger-id archaeology), `scope-call` (7,988, the worked scope
  examples and the question-asking protocol's history), `brush-measurement`,
  `optional-settings` and `ai-work`. Each left its rule behind and took its
  reasons with it.

  The two that dominated are worth naming, because they are the shape to watch
  for. `Branches, merges and pull requests` had reached 10,817 characters — 29% of
  the whole file — of which the operative rules were about 2,300; the rest was
  two retired generations of merge machinery and six days of measured id
  collisions. `Absent by default is not the same as out of reach` was 7,988, and
  was really two unrelated things welded together: a scope philosophy and a
  protocol for asking the owner a question.

- **22,354 → 22,450** (2026-08-27, B322/B332): +96 for one row in *Start here*
  pointing at `docs/DESIGN-two-stage-brush.md`. The owner had asked why a brush
  stamps twice on more than one occasion and said outright that the answer *"does
  not stick"* — which it did not, because it had never been written down, so each
  session re-derived it from the source and got it half right. The design note is
  the reachable half; the row is the resident half, and without the row the note
  is a file nobody opens. That is the split this ratchet exists to enforce rather
  than an exception to it.

  Trimmed before raising, which is the order the rule asks for: the row started at
  136 characters and went in at 96 by dropping its editorial half. The other 40
  were not worth a byte of every session forever.

- **21,593 → 22,354** (2026-08-24, same change): +761 for the paragraph in *Start
  here* that states the resident/reachable split and points at this ratchet, and
  for the table row naming the five skills. This is the case the section above
  describes as raising the number honestly: it is a rule about where things go,
  needed by any session that adds to `CLAUDE.md`, and a rule nobody can find is
  the one that gets broken. Recorded rather than absorbed, so the next raise has
  to argue for itself too.

- **22,450 → 23,076** (2026-09-21, Q190/Q191): +626 for the *Every change starts
  at triage* section, one row in *Start here*, and `sensitivity` in the skills row.
  This is a rule, and it has to be resident to work: the whole point of the router
  is that a session runs it **before** it has decided its change is small, so a
  session that has not read the rule will not run it — and the only sessions that
  would not are the ones that were about to skip it. The reasons live in the
  `sensitivity` skill and `docs/DESIGN-sensitive-topics.md`, the reachable half.

  Trimmed before raising: the section started at 790 characters and went in at
  510 by dropping its rationale and its pointer to the skill, which the table row
  already carries. What is left is what a session needs *before* it opens
  `FLOW.md` or `SENSITIVITY.md`: that triage exists, what the two answers mean,
  that a track is not lowered by the session, and that some files are not its to
  edit. The full cost of this feature to every session is larger than this
  number — two agent definitions and a skill are on-demand and are not counted here.
