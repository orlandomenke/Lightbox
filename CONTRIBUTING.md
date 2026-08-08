# Contributing

**Lightbox is not accepting pull requests yet.** That is a temporary position
with a specific reason, and it is worth stating plainly rather than leaving
people to guess from silence.

## Why not

Lightbox is licensed **GPL-3.0**, and every line of it is currently written by
one author. That means the copyright is held in one place, which keeps two
options open that a scattered copyright would close:

- **Relicensing.** A sole copyright holder can release the same code under other
  terms later — a commercial licence, a more permissive one, a dual arrangement.
  The moment an outside contribution lands, that stops being possible without
  tracking down every contributor and getting their agreement.
- **Correcting course.** The project is in alpha and the document format still
  changes. Whole subsystems get rewritten. A contribution accepted today could be
  deleted next week, which is a poor trade for the time it took to write.

Neither is a judgement about anybody's code. Accepting contributions properly
means a contributor licence agreement, a review commitment, and a stability
promise the project cannot yet make — and half-doing it is worse than waiting.

## What is welcome now

- **Bug reports.** Especially with the crash log: **Help ▸ Open the diagnostics
  folder**, and attach the file. It names the exact build, which "the latest
  version" does not — see the manual's
  [troubleshooting section](docs/manual/13-keyboard-and-troubleshooting.md).
- **Questions about how something works**, and disagreement with how it works.
  Much of the reasoning behind this codebase is written down in
  `.claude/quality/` — the roadmap, the bug ledger, and `QUESTIONS.md`, which
  records design decisions along with what they cost. Arguing with those is
  useful.
- **Forks.** GPL-3.0 says what you may do; nothing here asks you to do less.

## What changes when this changes

When Lightbox leaves alpha this file will be replaced with real contribution
guidance: a CLA or a DCO, what a good pull request looks like, and which parts of
the codebase are stable enough to build on. Until then, an issue is the way in.
