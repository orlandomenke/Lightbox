# Q243 · Flip through drawings by role (key, breakdown, inbetween) — **answered: later, after the speed work**

**Answered 2026-10-11: later.** Finish making flips fast first; then take this
up as its own feature.

Raised 2026-10-11 by the owner, while the flip speed work (B443) was in
progress: most artists flip through keys, breakdowns and inbetweens, a
selection of those or all of them, and holds can be left out. Flipping through
every frame is still a real use.

What exists today: "Flip to next key" (2) and "Flip to previous key" (1) go to
the next frame where the active layer's drawing changes, so holds are already
skipped. Every drawing can be marked Key, Breakdown or Inbetween (`FrameRole`),
but the marking only colours the timeline cell; flipping ignores it. Stepping
through every frame is the arrow keys.

What it blocks: nothing now. When it is built, the idle warm must prepare
whatever the chosen flip lands on — it asks the commands' own lookup
(`KeyIndexFrom`), so that is where a role filter goes.

**Recommendation was the chosen one** (later). The alternatives, for when it is
picked up:
- One setting for what 2 / 1 flip through (all drawings, keys and breakdowns,
  keys only). One pair of shortcuts, but their meaning changes with a setting.
- Separate bindable commands per role ("flip to next key pose", "next
  breakdown"), 2 / 1 staying "every drawing". More shortcuts, each meaning one
  thing; they belong in `ShortcutMap` either way.
- No role flipping at all: roles stay a timeline colour.
