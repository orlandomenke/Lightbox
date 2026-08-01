# Lightbox

A raster + vector desktop animation app for hand-drawn frame-by-frame work,
where AI fills in the inbetweens. C#/.NET 8, Avalonia 12, SkiaSharp.

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

Rebuild by hand with `python3 scripts/codemap.py build` after large changes.

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
   touch the document.
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
