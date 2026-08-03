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
- *"How far do we go simulating a medium?"* — **as far as the expression goes,
  and not one step further.** This is a drawing application, not a physics
  paper: we are not recreating watercolour, we are giving an artist the part
  of watercolour that makes a mark say something. Where a cheap approximation
  and an accurate simulation look the same to a person, the cheap one is
  correct and the accurate one is a defect. Krita's engine pushes further than
  most and still leaves this on the table; the edge is in the *expression*
  rather than in the fidelity, and chasing fidelity at the cost of a frame
  budget spends the advantage rather than earning it.
- *"Should this expensive brush option exist at all?"* — yes, if an artist
  would reach for it deliberately, and **no if it becomes the default**. The
  costly options are opt-in, they live on presets, and the picker badges them
  (`BrushCostOf`, derived from the settings so it cannot lie) so the trade is
  made knowingly. Every simulated medium also ships a fast counterpart — a
  medium nobody can afford is a trap, not a feature.

Two things that read as the same word and are not: **visual variation** is
wanted, **logical randomness** is forbidden. Marks should differ the way real
media differ — because of where they are, what they are on and how fast the
hand moved. That is invariant 2 restated from the artist's side rather than a
constraint fighting it.

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
| What is risky to change? | read `.claude/codemap/HOTSPOTS.md` (generated, not committed — build if absent) |
| What behaviour is promised today? | read `.claude/codemap/FEATURES.md` |
| What are we building, and how far along? | read `.claude/quality/ROADMAP.md` |
| What is known broken? | `python3 scripts/bugs.py next` |
| What is broken in the area I am editing? | `python3 scripts/bugs.py mine <domain>` |
| What does the app do, from the artist's side? | read `docs/MANUAL.md` |
| What does an AI request cost? | read `docs/DESIGN-ai-payload.md` — do not re-derive it |
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
- Work happens on a branch off `main`, merged back when it is green.
  `feature/ui-dockers` was the long-lived UI branch and has landed.

### Branches, merges and pull requests

Delegate them to the **git-handler** agent (`.claude/agents/git-handler.md`)
rather than doing them by hand. It cuts branches from the current default,
checks divergence before it does anything, refuses to merge a red suite,
builds the merged tree before pushing, writes PR bodies against the repo's
template, and reports which branches have gone stale or are already merged and
still hanging around.

It does not merge or open a PR unless that was the actual request — those are
the two git actions other people see.

### Touching anything AI: two agents, on purpose

AI work is reviewed by a **pair**, `.claude/agents/ai-engineer.md` and
`.claude/agents/art-director.md`, and they are meant to disagree.

- **ai-engineer** owns the machinery: what is sent, what it costs, what the
  contract is, what happens when it fails, and the line that keeps a model out
  of the render path.
- **art-director** owns the result: does the inbetween read at 12 fps, is it
  actually *between* the keys, is it on-model, does the mark say anything.

Either alone fails in a direction you can predict. Alone, the engineer
optimises until the output is cheap and lifeless; alone, the director asks for
richness nobody can afford or reproduce. **art-director has a veto on
expression, ai-engineer has a veto on determinism**, and where they disagree
and cannot measure, it goes to `QUESTIONS.md` rather than to whoever ran last.
Q18 — flat point arrays are 57% cheaper and might cost stroke labels — is the
live example, and it is exactly the shape of argument the pair exists for.

Gate G12 in the charter makes this non-optional for a diff touching
`src/Lightbox.Ai`, the MCP surface, a prompt, or an AI path in the view model.

### What an AI request costs, before optimising it

`docs/DESIGN-ai-payload.md` has the measured numbers, and one of them settles
most of these arguments before they start: **images are ~87% of a request's
bytes and ~5% of its tokens; strokes are the reverse.** So "make the payload
smaller" is not a goal — it is two goals that recommend opposite changes, and a
proposal that has not said which one it means is not ready.

The corollaries, all measured: compression is not worth it (82% off the bytes,
nothing off the tokens, and the upload is 0.3 s beside a minute of generation);
GraphQL does not apply (there is no API of ours in the path); and the biggest
lever by six times is **sending fewer strokes**, not encoding them better.
`AiPayloadBudgetTests` keeps the numbers honest.

### Measuring a brush: the saturation trap

**Dabs overlap, so alpha along a stroke saturates.** A brush at `Spacing = 0.05`
lays about twenty dabs on every pixel, and twenty dabs of flow `a` come out at
`1 - (1-a)^20` — which is **0.92 at a flow of 0.12**. So a test that sets flow
to 0.1, renders, and reads the alpha down the middle of the stroke gets 0.93
and concludes the control works, when a brush at flow 1.0 also reads 1.00 and
the two are a hair apart. The same test passes on a build where flow is wired
to nothing.

This has now cost real time three times — B26's depletion tuning (`Reach` 24 →
50 → 12 before the overlap was noticed), and twice since in tests that had to
be rewritten. The rules:

- **Measure below saturation, or not along the stroke.** Either use values low
  enough that `1-(1-a)^n` is still climbing (flow ~0.01–0.02 at ordinary
  spacing), or widen the spacing so the dabs barely overlap, or measure
  something that does not accumulate — stroke *width*, a profile across the
  stroke, the ratio between two places on the same stroke.
- **Always print both numbers.** `output.WriteLine($"faint {a:F3}, full {b:F3}")`
  is what turns "the assertion passed" into "0.929 vs 1.000, which is nothing".
- **Sanity-check the other way.** Assert the faint mark is *present* as well as
  fainter; a test that only checks `a < b` also passes when `a` is zero because
  the brush is broken.

The general form is the lesson `docs/DESIGN-performance.md` records for
measurement: **the number was real and the attribution was not.** Ask *what
else is in this measurement* before *what is wrong with the code*.

### "Optional" has two halves, and the second one is easy to miss

A setting is optional when it is **absent unless used** — not merely inert at
its default. Two ways that goes wrong, both found by dumping the JSON for a
document with one default stroke rather than by reading the model:

- **A non-nullable block serializes even when it is untouched.** The medium was
  behaviourally absent and written anyway: twenty-one keys on every stroke of
  every document, a third of the brush record, for a pass nobody switched on.
  A block whose default is "off" wants to be nullable, or to have a shadow
  property that returns null when it is untouched.
- **A convenience getter beside a nullable field is a property.**
  `BlendOrNormal => Blend ?? Normal` had a public getter, so every stroke wrote
  `"blendOrNormal": "normal"` — reintroducing under a second name the exact key
  that making `Blend` nullable existed to remove. These need `[JsonIgnore]`.

So: after adding a setting, **serialize a document that does not use it and
look**. `Assert.DoesNotContain("\"yourKey\"", json)` is the cheap version and
belongs in the same commit.

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
