# Q190 · Which sensitive areas does the agent system guard, and how — **answered 2026-09-21: all four, as one baseline with numbered rules**

**Raised by:** the owner, asking for the agentic system built in a sibling
project (a user-space shell for Windows, developed by a looping pipeline of
specialised agents) to be brought to Lightbox — translated, not copied — for an
application positioned against Photoshop, Krita, GIMP, Moho and Toon Boom, and
asking specifically that it take sensitive topics into account.

"Sensitive" did not resolve on its own: it could mean the artist's data, the
project's legal exposure, the artist's work, or the security of the tooling. The
repository already had pieces of each and a rulebook for none of them.

**What it blocks:** `.claude/quality/SENSITIVITY.md`, the `sensitivity-guardian`
agent and gate G13. Without knowing the scope there was no way to say what the
guardian reviews.

## The question, as prompted

Which of four areas should the system cover, recommended: all four.

| Area | What it holds |
| --- | --- |
| **Artist data and AI consent** | what leaves the machine for a cloud provider, redaction, opt-in, no training on the artist's work, labelling AI output, style-mimicry of named artists, keys |
| **IP and licensing** | clean-room policy for other tools' formats and behaviour, GPL-3.0 hygiene, dependency, brush-pack and font licences |
| **Work is never lost** | atomic save, recovery, format migration, undo integrity, determinism of the stroke record |
| **Security and untrusted input** | imported files and agent-supplied strokes as data, secrets in a public repository, supply chain, shell guard |

**Answered: all four** — the recommendation, so no cost is being carried against
the advice. Two things follow that are worth stating rather than assuming:

- **They are one file, not four.** The rules are prefixed by area (`A`, `L`, `W`,
  `S`) so a finding can cite one, but the trust-boundary table is shared, because
  the same boundary usually touches two areas — an imported brush pack is both a
  licensing question (where did it come from) and a security one (what does the
  parser do with hostile bytes).
- **The baseline records what is already true as well as what must become true.**
  Atomic save (W1), a bounded PSD reader (S1) and a deterministic stroke record
  (W5) were already in the code; the rules exist so a later change cannot quietly
  undo them. What is not true yet is listed under *Known gaps* in the file rather
  than asserted.
