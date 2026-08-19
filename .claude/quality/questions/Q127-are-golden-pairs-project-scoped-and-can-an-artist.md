# Q127 · Are golden pairs project-scoped, and can an artist add their own? — **answered 2026-08-19, both as recommended**

Raised by the owner while reading phase 2's state: *"But the pair is document or
project dependent? Or a global pair?"* — a question about the shipped set that
turned out to be about something the shipped set never covered.

**What the code says today.** Entirely global. `GoldenSet` is a static class
with every pair written literally in it, compiled into the app; the profile it
produces lands on `AiConnection.LastProfile` in `ai.json`, beside
`brushes.json`. Nothing is scoped to a document or a project. One accident in
Lightbox's favour: `CapabilityProfiler.ProfileAsync` takes an
`IReadOnlyList<GoldenPair>` rather than reaching for `GoldenSet` itself, so a
second source of pairs needs no rearchitecting — only `ConfigureWindow` picks
`Short()` or `Full()`.

**What the question exposed, and it is the reason this is a question at all.**
The phase-2 note *"still open: the hand-drawn organic pairs"* silently conflates
two features:

1. **Shipped organic pairs** — drawn by whoever builds Lightbox, committed and
   reviewed, filling the empty `Organic` row for everybody. Global, and its
   scope was never in doubt: it is the scope every other pair already has.
2. **Artist-supplied pairs** — drawn from the artist's own document, of their
   own character in their own style. **Q34 never asked about these.** It asked
   whether the *grader* ships, not whether the *set* can be extended.

Only (2) is a live decision, and it is worth having because it is the stronger
version of the whole feature: the question an artist has is not "can this model
inbetween a generic quadruped" but "can it inbetween **my** character". A
shipped pair is a proxy for that; a drawn-from-my-project pair is the thing
itself.

## 1 · Can an artist add their own pairs, and at what scope?

**Answered: yes, and they are project-scoped.**

A golden pair is drawn art of a specific character in a specific style, and that
is what a project holds — `DESIGN-project-scoping.md` is already where a
character lives. The two rejected scopes, and why:

- **Document** is too small. A pair is a reusable asset that outlives the
  workfile it was captured from, not a property of one.
- **Global (beside `ai.json`)** is simpler — no project format change — and is
  wrong the moment somebody has two productions, because one set of pairs would
  then span unrelated shows and styles.

**What it costs, stated rather than discovered later:** a capture command that
takes three drawings from a document (key, the artist's own inbetween, key), a
place in the project format for pairs, and the reporting split below. It does
**not** cost a change to `CapabilityProfiler`, which already accepts an
arbitrary pair list.

## 2 · How do artist pairs appear in the profile?

**Answered: their own section, never blended into the shipped scores.**

This is Q34's obligation doing its work at one remove. Q34 made the set *"a
published claim"* — committed, reviewed, and changing it changes what Lightbox
says about somebody's model. An artist's pair is by construction unreviewed, so
blending it into the same percentage destroys the property that made the claim
worth anything: **an organic score has to mean the same thing on every machine**,
or it can be compared neither between two models nor between two people.

Reported separately, both halves keep their meaning — the shipped rows stay
comparable, and the artist's rows answer the question they actually asked. The
rejected option (one blended organic score) is easier to read and buys a number
nobody can interpret.

## What this does not decide

**How a drawing is scored against a reference drawing.** `GoldenPair.KnownGood`
exists on the record and is read by **nothing** — no scorer, no profiler, no
test — so today a hand-drawn pair would be graded exactly like a constructed one
and its reference ignored. That gap is prior to both answers above: stroke-by-
stroke distance would punish a model for decomposing the same motion into
different strokes, which is not an error. It needs the G12 pair and probably a
question of its own, and it is the real blocker on the `Organic` row rather than
the drawing.

**Blocks:** the artist-supplied half of phase 2. Does not block the shipped
organic pairs, which can proceed under (1)'s existing scope.
