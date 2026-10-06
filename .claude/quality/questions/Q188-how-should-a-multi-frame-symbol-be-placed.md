# Q188 · How should a multi-frame symbol be placed

Raised by: B373, fixing the five-second freeze on placing a multi-frame symbol.

What it blocks: whether the placement dialog survives at all, and therefore what
`PlaceSymbol` does when nobody has answered it.

A symbol holding several drawings can land two ways, and both are things people
mean:

- **Import every drawing into the timeline** — the drawings become frames of
  this scene, and the scene grows to at least the length of the symbol.
- **Place one that cycles on its own** — a single placement that runs through
  its own drawings without changing how long the scene is.

The dialog that asked this shipped 2026-08-06 and **had never once been
answerable**: it was awaited with `task.Wait(5000)` on the UI thread, which is
the thread that would have delivered the click. So the question was live in the
code and dead in the product, and the fix had to decide whether it was wanted
at all before deciding how to ask it.

**Recommendation:** repair it as a real async prompt — the view asks and awaits,
the answer is used, and "don't ask again" persists.

The alternatives and what they cost:

- **Remove it, always place as a reference.** Simplest, and never lengthens a
  scene behind the artist's back. Costs the gesture: importing a cycle into the
  timeline becomes a separate explicit command, and that is a real thing people
  want on an asset sheet.
- **Remove it, always import frames.** Least change to observed behaviour, since
  that is what the timeout fell through to. But it is the behaviour *nobody
  chose* — an accident of a timeout — and it silently lengthens the scene.
- **Ask once per project rather than globally.** A sprite-sheet project and a
  shot project reasonably want different answers. Rejected as premature: the
  preference is cheap to move to project scope later, and nobody has yet asked
  for two answers.

**Decided (2026-09-12, owner): repair it as a real async prompt.** Persist the
"don't ask again" answer to settings so it survives a restart, and surface it in
Configure ▸ Library so it can be taken back — a preference an artist can store
but not find again is a trap.

Two consequences worth writing down:

- **The unanswered default is `Reference`, not `ImportFrames`.** A caller that
  never asked — the MCP surface, a test, an agent — must not silently lengthen
  the scene. This changes the observed default, deliberately.
- **The view asks, never the view model.** A dialog completes on the UI thread,
  so a view model that waits for one is waiting on the thread that would answer
  it. `OnMakeSymbolOfLayers` already had this arrangement for its capture-depth
  question; placing now matches it.
