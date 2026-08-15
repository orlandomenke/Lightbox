# Q12 · Whether an animation template is a document or a project type — **answered (a)**

**Answered 2026-08-03: (a), a document with a flag.** Designed out in
`docs/DESIGN-templates.md`, because "changeable on the fly" was asked for
explicitly and it is the property that decides the mechanism: a template is
**copied, never referenced**, so editing one is safe precisely because it
cannot reach back into work already started from it. (c) stays available —
a starter pack is (a) plus content, and needs no change to the mechanism.


**Blocks:** the last `[?]` in Pillar 3.

*Animation templates* — starting a new animation from a skeleton rather than an
empty document — is real and absent. What is undecided is where it lives, and
the app already has two mechanisms that overlap it: `NewDocumentSettings`
(size, fps, frame count) and project types (which decide the workspace).

- **(a)** *A document in the project marked as a template.* Copy it, rename it,
  start drawing. Costs nothing new — a template is an ordinary animation with a
  flag — and an artist can make one out of work they have already done, which
  is where real templates come from.
- **(b)** *A built-in list* (walk cycle 8 on 2s, run cycle 6, blink 4, take 12).
  Better on day one, worthless on day two: every studio times its own walk
  differently, and a list nobody can add to becomes a list nobody uses.
- **(c)** *Both* — built-ins that are seeded as project documents on first use,
  so they are editable from the moment they appear.

**Recommend (a).** It is the smallest thing that is not a guess about how other
people animate, and (c) is (a) plus a starter pack, which can be added later
without changing the mechanism.

---
