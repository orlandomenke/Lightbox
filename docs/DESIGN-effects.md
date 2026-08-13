# DESIGN: Effects

*Written 2026-08-12. Decisions taken provisionally — the owner was prompted and
the prompt went unanswered, so per Q66 the four pivotal choices are restated at
the top of the pull request that carries this file, where they can be reversed
in a sentence. Everything below assumes the recommendations held.*

This is the design for the roadmap's `[?] Non-destructive filters`, widened the
way the request widened it: Photoshop-style filters **and** animation effects,
with a node system in the future that these must slot into rather than be
replaced by, possibly project-bound, and — hard requirement — decoupled from
`MainWindow`/`MainViewModel`, which are already the two hottest files in
`HOTSPOTS.md`.

## The one decision everything else falls out of

**There is one effect model, not two.** An effect is a pure function:

```
(input image, frame index, params) → output image
```

Every parameter can be a constant **or** carry keys with easing — the same
key-plus-`Easing` vocabulary `CameraKey` already uses, so a camera move, a
drawing inbetween and an animated blur are all described in one language. A
"Photoshop filter" is simply an effect none of whose parameters have keys.

The two lanes the request floated — *photoshop* and *animation* effects — are
real, but they are a **presentation** difference, not an architecture: the
picker groups effects by a tag on the definition (a grade and a blur read as
filters, a wiggle and a flicker read as animation), and any effect can be keyed
regardless of which shelf it sits on. That is the *every feature is reachable*
rule applied here: the shelf decides what is in front of you, never what the
parameter can do.

Why not two lanes with a shared core: it duplicates the registry, the UI and
the serialization for a distinction that evaporates the moment an artist keys a
filter — and it hands the future node system two node kinds where one would do.
Why not static-only first: retrofitting time-variance into a shipped record is
a migration; shipping the record time-capable with no keys written costs
nothing (a constant param serializes as one number, see below).

## The record

New files in `src/Lightbox.Core/Effects/` — no rendering, no UI, per the layer
rules:

```csharp
EffectStack      { List<EffectUse> Uses }
EffectUse        { string Id; string Kind; bool Enabled = true;
                   Dictionary<string, EffectParam> Params }
EffectParam      { double Value; List<EffectKey>? Keys }   // Keys null = constant
EffectKey        { int Frame; double Value; Easing Ease }
```

- **`Kind` is a string id**, resolved through a registry at render time
  (`"blur.gaussian"`, `"grade.levels"`). An unknown kind — a document from a
  newer build — is **preserved on save and rendered as identity**, flagged in
  the UI, never dropped. Forward compatibility is what a string id buys over an
  enum, and the brush tip registry already made this trade the same way.
- **`EffectParam.Keys` is nullable**, so an unkeyed parameter writes
  `"radius": {"value": 4}` and nothing else. This is the *optional means
  absent* rule at the parameter level, and the serialization test below is
  what keeps it true.
- Evaluation lives in `EffectOps.At(param, frame)` — the exact shape of
  `CameraOps.At`: hold outside the authored range, ease between keys, pure
  function of its inputs.

### Where a stack attaches

Two nullable properties, both **absent until authored** — the camera's rule,
stated in `Layer.OmitFromExport`'s own doc comment and enforced the same way:

| Attachment | Affects | Typical use |
| --- | --- | --- |
| `Layer.Effects` (`EffectStack?`) | that layer's baked output, before blend | blur one layer, glow the ink layer |
| `Scene.Effects` (`EffectStack?`) | the whole composite, before the camera | grade, grain, vignette — where most animation effects live |

A document that never touches effects serializes byte-identically to today.
`Assert.DoesNotContain("\"effects\"", json)` on a default document goes in the
same commit as the record — that is the cheap half of the "optional has two
halves" lesson, applied before it can be missed.

Per-frame attachment is deliberately **not** in v1. A one-frame effect is a
one-frame key range on a layer or scene stack; a per-cel stack triples the
attachment surface for a case keying already covers.

## Invariants, applied

1. **The record is the document (inv. 1).** Effects are parameters, pixels are
   derived. There is no destructive apply in v1; if a *flatten to strokes /
   bake* command ever exists it is an explicit rewrite of the record, not a
   render-time behaviour.
2. **No randomness (inv. 2).** Grain, noise, wiggle and anything stochastic
   seeds from `Hash01` over **document position and frame index** — never an
   RNG, never a clock, never an iteration index. Same reason as brushes: a
   re-render, an export and an AI inbetween must agree. And the flicker rule
   from the front page applies doubly here: an effect whose noise is not
   frame-stable *by choice* (film grain wants to move) must move because its
   seed includes the frame, not because anything drifted.
3. **Settings that affect pixels live in the record (inv. 4).** All of an
   effect's inputs are in `EffectUse.Params`. Nothing reads a preference at
   render time.
4. **Bounded work (inv. 6).** Every effect **declares its reach** — the
   maximum distance a pixel's output can depend on input:

   | Reach class | Examples | Repaint consequence |
   | --- | --- | --- |
   | point (0 px) | levels, HSL, curves | dirty region unchanged |
   | kernel (finite) | blur, glow, sharpen | dirty region inflated by reach, exactly as brush strokes already inflate by their own reach |
   | global | large distortions | whole affected surface re-runs; the effect is badged costly in the picker, the `BrushCostOf` precedent |

   Reach is **derived from the params** (a blur's reach is its radius), so the
   badge cannot lie — also the `BrushCostOf` precedent.
5. **Scale the surface, never the geometry (inv. 7).** Reach and any
   position-seeded noise are declared in *document* pixels and mapped by the
   canvas transform, the same trap `ApplyGranulation` already documented: a
   2× export must not re-roll the grain or halve the blur.

## Rendering: one pass, one seam

The pipeline gains one seam, in two places:

```
strokes → layer bake → [layer effect pass] → blend/composite → [scene effect pass] → camera → view
```

- The pass itself lives in `src/Lightbox.Raster/Effects/` beside
  `TileCompositor` — the model never renders, the App never touches pixels.
  `EffectRegistry` maps kind ids to implementations there.
- **CPU first.** `docs/DESIGN-gpu-compositing.md` already owns the question of
  what moves to the GPU and why; effects add entries to that ledger rather
  than a parallel answer. The pass is written against the same
  surface-in/surface-out boundary GPU compositing uses, so moving an effect to
  a Skia runtime shader later changes an implementation, not the seam.
- **Caching:** a layer's effect output is cached keyed on (layer content
  version, frame, params hash). A static filter on a held cel therefore costs
  once, not per repaint; only keyed params invalidate per frame. This is what
  keeps 12 fps playback honest, and it gets a performance-tagged budget test
  the day the pass lands, not after.

## Project-bound: presets, not applications

What is project-bound is the **named stack** — an *effect preset* saved as its
own file in the project, filed by folder, exactly the shape sheets took when
Q25 was re-answered. An application (`Layer.Effects`, `Scene.Effects`) stays in
the document it affects, because it is part of that document's record
(invariant 1 does not survive a render that depends on a file outside the
document unless the reference is resolved and inlined on flatten — the same
condition Q28 recorded).

v1 ships the record able to hold a preset reference but resolves it eagerly on
apply (copy, not link). Live-linked presets — edit the preset, every user
updates — are a node-system behaviour and wait for it.

## The node system this must grow into

Nothing in v1 builds a node graph, and everything in v1 is shaped so the graph
subsumes it rather than replaces it:

- `EffectUse` **is already a node**: typed params, one image in, one image
  out, pure, deterministic. A stack is the degenerate graph — a linear chain.
- The evaluation signature stays pure (`Render(input, frame, params)`), so a
  future graph evaluator calls the same implementations the stack pass calls.
- The graph, when it comes, is a **project-level document** that can reference
  pages and animations — which is why presets are project files now: the graph
  file lands beside them, and a stack imports into it as one chain of nodes.
- What is deliberately *not* pre-built: multi-input nodes, branching, and the
  graph editor. Building those speculatively is how a node system ships twice.

## Decoupled from MainWindow, structurally

The request's hard constraint, and the part with a registry checklist:

- **Own view model, own docker.** `EffectsViewModel` + an effects docker in
  their own files; `MainViewModel` gains only the registration line and the
  active-selection context it already exposes. No effect logic, no effect
  properties, no effect commands on `MainViewModel` — that is the review bar
  for every diff in this area, checkable by looking at the diff's file list.
- **Landing checklist** (the *land the places it shows up* table, resolved in
  advance): shortcuts through `ShortcutMap`; per-document options in the
  document window scope, not Configure; presets survive save/reuse as project
  files; the docker registers in workspace defaults; and the MCP surface gets
  `effects.*` operations, because an agent that can paint should be able to
  key a blur.

## v1 catalogue

Small, one of each reach class, each proving a different seam: **Gaussian
blur** (kernel; the reach machinery), **levels** (point; the cheap path),
**HSL** (point; colour), **film grain** (point but seeded; invariant 2 under
animation), **vignette** (point with geometry params; keying reads well). Every
further effect is catalogue work, not architecture.

## Build order

1. Record + `EffectOps` + serialization tests (absent-until-used proven).
2. Raster pass + registry + blur and levels, with the cache and its budget test.
3. Docker + view model, wired through the landing checklist.
4. Scene stack, grain/HSL/vignette, keying UI on the timeline.
5. Presets as project files.

Each is one branch. The roadmap item stays `[?]` until step 2 gives it
evidence anchors to name.
