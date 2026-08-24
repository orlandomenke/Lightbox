---
name: ai-work
description: Reviewing and costing AI work in Lightbox — the ai-engineer / art-director pair and where their vetoes sit, and what a request actually costs in bytes versus tokens. Read on any diff touching src/Lightbox.Ai, the MCP surface, a prompt, or an AI path in the view model (charter gate G12).
---

# AI work: the pair, and what it costs

Two agents review this, on purpose, and they are meant to disagree. The cost
section below settles most optimisation arguments before they start.

## Two agents, on purpose

AI work is reviewed by a **pair**, `.claude/agents/ai-engineer.md` and
`.claude/agents/art-director.md` — machinery and result respectively, per their
own descriptions — and they are meant to disagree.

Either alone fails in a direction you can predict. Alone, the engineer
optimises until the output is cheap and lifeless; alone, the director asks for
richness nobody can afford or reproduce. **art-director has a veto on
expression, ai-engineer has a veto on determinism**, and where they disagree
and cannot measure, it goes to `QUESTIONS.md` rather than to whoever ran last.
Q18 — flat point arrays are 57% cheaper and might cost stroke labels — is the
live example, and it is exactly the shape of argument the pair exists for.

Gate G12 in the charter makes this non-optional for a diff touching
`src/Lightbox.Ai`, the MCP surface, a prompt, or an AI path in the view model.

## What an AI request costs, before optimising it

`docs/DESIGN-ai-payload.md` has the measured numbers, and one of them settles
most of these arguments before they start: **images are ~87% of a request's
bytes and ~5% of its tokens; strokes are the reverse.** So "make the payload
smaller" is not a goal — it is two goals that recommend opposite changes, and a
proposal that has not said which one it means is not ready. The corollaries are
measured in that doc — compression, GraphQL and the six-times lever of sending
fewer strokes are all settled there, so read it rather than re-deriving them.
`AiPayloadBudgetTests` keeps the numbers honest.
