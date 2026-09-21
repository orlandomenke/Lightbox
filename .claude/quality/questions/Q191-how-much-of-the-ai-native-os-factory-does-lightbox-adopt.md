# Q191 · How much of the AI-Native-OS factory does Lightbox adopt — **answered 2026-09-21: the full pipeline, in two tracks chosen by a triage step**

**Raised by:** the same request as Q190. The sibling project runs every change
through eight strict stages — design, tests, implementation, code review,
security review, performance, QA, retro — with a coach agent that trains the
others and a ledger of what each cycle cost.

**What it blocks:** the shape of `.claude/quality/FLOW.md` and whether a
`triage` agent exists.

## The question, as prompted

Three shapes, recommended first:

- **Guardrails first** — a user-only baseline, a guardian agent, token-free hooks;
  the coach, ledger and recall follow as a second branch.
- **Guardrails plus coach, ledger and recall** — about four or five branches.
- **The full pipeline** — the strict eight stages per change. Advised against,
  because it conflicts with two standing rules here: *fixing is the default*
  (a small P3 is fixed inline, not filed) and *one branch, one objective*. A
  full pass for a one-line fix would either be skipped, which is worse than not
  having it, or would make people stop fixing small things, which is the failure
  `CLAUDE.md`'s ledger arithmetic already measured (79% of B161–B179 left open).

**Answered: the full pipeline, but in two tracks, so small things are still fixed
inline and larger work is branched — chosen by a scout at the first step.** This
goes past the recommendation on purpose, and it answers the objection rather than
overriding it: the objection was to a *single* heavy path, and two tracks with a
mechanical router removes that.

## What the answer turns into

- **A `triage` agent runs first**, backed by `scripts/sensitivity.py triage`. The
  script is the router and is deterministic; the agent adds the judgement the
  script cannot (is this really one objective?) and can only move a change **up**
  a track, never down.
- **The router is sensitivity-first, not size-first.** A one-line change to where
  an API key is read from is small and is not fast-track work. Size only decides
  between tracks once nothing sensitive is touched.
- **The fast track is not a hole.** It still runs the hooks and
  `sensitivity.py scan` — both cost no tokens — so the only thing it skips is
  ceremony, never a check. This is the sibling project's own rule (never save
  tokens by skipping a gate) applied to the routing.
- **Not every stage becomes a new agent.** Lightbox already has `story-analyst`,
  `test-smith`, `adversary` and the review agents; the implementer is deliberately
  the main thread, which holds the context and is the one the owner is talking
  to. Only the two roles that had no home — `triage` and `sensitivity-guardian` —
  are new. See `docs/DESIGN-sensitive-topics.md` for the mapping and what was left
  out.

## What this defers, and why that is a cost

The coach (tiered self-editing with an evidence rule), the cycle ledger, a
cross-ledger `recall` and token budgets are **not in this branch**. The design
document specifies each, adapted to Lightbox — but a coach needs a ledger to read
and a ledger needs cycles to have run, so building them first would have produced
an empty ledger reviewing nothing. The cost of deferring is that until they land,
the pipeline's rules are only as good as the owner's eye on them; nothing yet
notices that a stage keeps bouncing work back.
