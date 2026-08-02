# Lightbox

A raster + vector desktop application for **frame-by-frame animation** and
**digital painting**, with AI assistance throughout — most visibly filling in
the inbetweens. C#/.NET 8, Avalonia 12, SkiaSharp.

## What it is for, and how that settles arguments

Two first-class purposes, not one with a hobby attached:

1. **Frame-by-frame animation.** Drawing on paper, one frame at a time:
   exposure sheets, holds, onion skin, flipping, animating on 2s. The unit of
   work is a sequence, and anything that makes a single drawing nicer at the
   expense of handling two hundred of them is a bad trade.
2. **Digital painting.** A single image worth finishing: real media, pigment
   that layers like pigment, brushes an artist can tune and import from the
   tools they already own.
3. **AI assistance** serving both — inbetweening, reference generation, and
   an MCP surface so an agent can work the document directly.

### Two output targets, and neither is the default

Animation here lands in one of two places, and a feature that serves one must
not tax the other:

- **Assets** — sprite sheets and character cycles for a game. The canvas *is*
  the output. There is no camera, frame bounds must stay consistent, and every
  frame is a deliverable.
- **Shots** — sequences for a film or show. The canvas is a world, a camera
  frames part of it, and the deliverable is what the camera saw.

The consequence is that shot-level machinery — camera, multiplane, parallax —
is **opt-in and absent until asked for**. A document that never adds a camera
must serialize, render and export exactly as it does today, and must never
show camera UI or pay for one. "Optional" here means absent, not disabled.

Most scope questions answer themselves once asked against these. Some worked
examples, so the reasoning is reusable:

- *"How much of Photoshop's brush panel should we take?"* — the parts that
  change how a **mark reads** and that `.abr`/`.kpp` files actually carry.
  Not the parts that only pay off on a single illustration you will spend a
  day on, because every one of them also has to survive being replayed across
  two hundred frames.
- *"Should a simulation be allowed?"* — yes, if it is deterministic. A stroke
  is replayed on load, on undo, and by the inbetweener; a mark that cannot be
  reproduced exactly is not a mark, it is a one-off. This is why invariant 2
  is absolute rather than a preference.
- *"Should this be per-document or per-preference?"* — if it reaches pixels,
  per stroke. An artist who returns to a scene after a month must find it
  exactly as they left it.
- *"Is flicker acceptable?"* — no. An effect that varies subtly between
  similar strokes looks fine on one image and boils at 12 fps. Anything
  stochastic must be seeded from geometry, not from an index or a clock.

When a request genuinely does not resolve against these, it belongs in
`.claude/quality/QUESTIONS.md` rather than in a guess.

## Start here, not with a search

`.claude/codemap/` is a generated index of the whole solution. Reading it
costs a fraction of exploring the source, and it is rebuilt automatically at
session start when it is stale.

| Question | Do this |
| --- | --- |
| Where does X live? | `python3 scripts/codemap.py find X` |
| What is in this file, who uses it, what tests it? | `python3 scripts/codemap.py file <path>` |
| What is the shape of the codebase? | read `.claude/codemap/INDEX.md` |
| What is risky to change? | read `.claude/codemap/HOTSPOTS.md` |
| What behaviour is promised today? | read `.claude/codemap/FEATURES.md` |
| What are we building, and how far along? | read `.claude/quality/ROADMAP.md` |
| What is known broken? | `python3 scripts/bugs.py next` |
| What is broken in the area I am editing? | `python3 scripts/bugs.py mine <domain>` |
| What does the app do, from the artist's side? | read `docs/MANUAL.md` |
| What should I pick up next? | `python3 scripts/roadmap.py next` |

Rebuild by hand with `python3 scripts/codemap.py build` after large changes.

`BUGS.md` is the same idea pointed at defects: every entry names the
regression test that closes it, `bugs.py sync` derives the checkbox from
whether that test exists, and deleting the test reopens the bug. An agent
about to edit an area runs `bugs.py mine <domain>` and fixes the open P1/P2
bugs it finds there alongside its own work.

`docs/MANUAL.md` is the user manual, and it is **part of the definition of
done**: a change that alters what an artist sees or does updates the relevant
section in the same commit. It describes what exists today and marks what does
not as *Planned* — a manual that documents a feature nobody can use is worse
than no manual, because it cannot be trusted anywhere.

`ROADMAP.md` holds the six pillars that give the app its identity, plus the
drawing floor beneath them. **Its checkboxes are derived from the code**, not
asserted: each item names the types and tests that would exist if it were
built, and `python3 scripts/roadmap.py sync` resolves them. Landing a feature
means adding its evidence anchors in the same commit — a green box with no
anchor is the one thing the file cannot represent.

## Invariants — breaking these is a defect even if tests pass

1. **The stroke record is the document.** A frame is a list of strokes; the
   pixels are derived. Anything that paints must go through
   `BrushEngine.StampStroke` so a reload renders the same image.
2. **No randomness in rendering.** Effects (scatter, granulation, jitter) are
   seeded from dab position via `Hash01`. An RNG would make re-renders and
   AI inbetweens diverge.
3. **Fills and selections are part of the record.** A fill is a `ToolKind.Fill`
   stroke with contours; a selection is a content-hashed entry in
   `Doc.ClipRegions` referenced by `Stroke.ClipId`.
4. **Settings that affect pixels are stored per stroke**, not read from
   global state at render time (anti-aliasing, pressure curves), so changing
   a preference never alters existing art.
5. **The view transform is view-only.** Zoom, rotation, mirror and pan never
   touch the document. The **camera** is the one transform that is not: it is
   authored, keyframed, saved and exported. It still never mutates a stroke —
   invariant 1 holds — it only decides what part of the record a render shows.
   "View through camera" is on the view-only side of that line.
7. **Render bigger by scaling the surface, never the geometry.** Stroke
   coordinates are never multiplied. `Hash01` seeds every dab dynamic from the
   IEEE-754 bits of a position, so doubling a coordinate re-rolls scatter,
   size, flow, roundness, rotation and all three colour jitters — a 2× render
   would be a *different mark*, not a sharper one. Output scale is therefore a
   canvas transform (`FrameRasterizer`/`BrushEngine`), and
   `OutputScaleTests` renders it the wrong way round on purpose to keep the
   reason written down.
6. **Painting is bounded work.** Live preview and commits repaint only the
   region a stroke can reach; anything that goes full-canvas per pointer
   event is a performance regression (see `.claude/quality/CHARTER.md`).

## Layout

- `src/Lightbox.Core` — document model, serialization, geometry, inbetweening.
  No rendering, no UI.
- `src/Lightbox.Raster` — `BrushEngine` (the only pixel path), flood fill,
  frame rasterization.
- `src/Lightbox.App` — Avalonia UI: view models, canvas control, compositing.
- `src/Lightbox.Ai` — Claude/Ollama artists behind an interface.
- `src/Lightbox.Mcp`, `src/Lightbox.Import` — MCP server, brush importers.

## Working here

- Build: `dotnet build Lightbox.sln`
- Test: `dotnet test` (all four suites must stay green)
- Performance budgets run inside the normal suite, tagged
  `[Trait("Category", "Performance")]`. They are deliberately loose — they
  catch order-of-magnitude regressions, not drift.
- The app runs headless for visual checks under Xvfb; see `MANUAL_TESTING.md`.
- Work happens on `feature/ui-dockers`.

### Verifying UI behaviour

Prefer a headless pixel test over a screenshot: tests in
`tests/Lightbox.App.Tests/LivePreviewPixelTests.cs` drive the real
begin/move/end pipeline and inspect the published frame. Synthetic input
through Xvfb is unreliable in this environment — a dropped click looks
exactly like a bug.

## Self-improvement loop

`/improve` runs an audit → fix → verify loop that guards the behaviour
inventory, expands tests, watches performance, and writes down the questions
it cannot answer itself. See `.claude/skills/improve/SKILL.md`.
