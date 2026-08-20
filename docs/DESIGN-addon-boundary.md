# The add-on boundary

The design note Q131 asks for. It records where a paid add-on can attach, the
three tiers that boundary has, and the one line that decides whether open core
fractures the file format.

Written before any of it is built, so the owner is agreeing to a shape rather
than to a diff.

## Why, in one paragraph

Q131 chose open core: the application stays free and GPL-3.0, and the bone
system, the fluid effects and named roadmap features become a commercial tier.
Nothing in the tree supports that today. There is **no dynamic loading anywhere
in `src/`** — no `Assembly.Load`, no `AssemblyLoadContext`, no
`Activator.CreateInstance` — **five public interfaces** across Core, Raster, Ai
and Import, and the only place a UI-facing add-on could attach is a view model
with **805 public members**. So the boundary has to be designed rather than
found, and the first question is not *how do we load a plugin* but *what may an
add-on be allowed to do without breaking the document*.

## The line that decides everything

> **An add-on may author the record. It must never be required to render it.**

That single sentence is what keeps `.lbx` trustworthy under open core. A
document is a list of strokes (invariant 1) and the pixels are derived. If a
paid add-on only ever *produces* strokes, then a free build opens the file and
renders it exactly right — it simply cannot re-author that part. If a paid
add-on is needed *at render time*, a free build opens the file and draws
something wrong, which is not a degrade but a corruption with a licence check in
front of it.

**Measured, and the two named features fall on opposite sides of it.**

| Feature | What it produces | Where it runs | Side of the line |
| --- | --- | --- | --- |
| **Fluid effects** | `SimBaker` → `BakedFrame(int Frame, List<Stroke> Strokes)` | an explicit bake | **Authoring.** Once baked it is strokes, and any build renders them. |
| **Bone system** | `Skinning.PoseFrameForRender(...)` → `Frame` | **the render path** | **Rendering.** `VideoExporter`, `SequenceExporter`, `SpriteSheetExporter` and the view model all point `FrameBitmapCache.PoseResolver` at it. |

`Skinning.BakeFrame` exists but is called from exactly two places, both in
`MainViewModel.Armature.cs`, and both are an explicit artist-invoked bake. **A
saved rigged document holds rest strokes plus bones plus poses, and needs the
skinning code to look right.**

**The codebase already documents the failure, which is the useful part.**
`FrameBitmapCache.PoseResolver` is a `Func<Frame, int, Frame>?` described in its
own summary as *"the live rig's one hook … null renders every frame as
recorded"*, and `Draw` reads it as `PoseResolver?.Invoke(frame, celIndex) ??
frame`. A build with no bone add-on leaves that hook null — so every frame of a
rigged document renders **in rest position**, quietly and with no error. That is
the corruption this note's opening line exists to forbid, and it is one null
away in code that is already written.

The same finding is the good news for tier 1: **the rig already has exactly one
seam**, and it is a delegate rather than a call graph. Whatever is decided
below, `PoseResolver` is where a bone add-on attaches.

So the consequence, stated plainly rather than discovered later:

- **Fluid effects can become a paid add-on without touching the file format.**
- **The bone system cannot, as it stands.** A free build opening a rigged
  document would render every frame in rest position — silently wrong art, not a
  missing feature.

That is not an argument against charging for the rig. It is an argument that
**making the rig paid is a file-format change**, and it has to be scheduled as
one. Three ways out, with costs:

1. **Bake poses into the record on save.** The free build renders correctly
   because the strokes are already posed; it just cannot edit the rig. This is
   the honest trade and the one to prefer — *editability* is the paid thing,
   which is what an artist would expect. Costs: the document grows, and the
   rest-pose original has to be kept alongside or the rig is not round-trippable.
2. **Ship the skinning resolver in the free core and sell only the authoring
   UI.** Rigs render everywhere; only making and editing them is paid. Cheapest
   technically, and the weakest commercially — the resolver is most of the
   interesting code.
3. **Accept that rigged files are paid-only files.** A free build refuses them
   rather than lying. Costs: the format fractures, which is the outcome the line
   above exists to prevent.

**Recommendation: (1), with (2) as the fallback if the record growth measures
badly.** It is the only one where a free build never shows an artist something
untrue.

## The three tiers, and why there are three

The boundary is not one mechanism. Where an add-on may attach decides which
licence question it raises and what it may cost per frame.

| Tier | Mechanism | Licence question | Carries | Cannot carry |
| --- | --- | --- | --- | --- |
| **0 — out of process** | pipe / MCP, as `Lightbox.Mcp` already is | **none** | batch operations, asset generation, pipeline and review integration | anything per-event — invariant 6's frame budget rules IPC out |
| **1 — in process, authoring** | managed assembly, load host needed | needs a posture | rig authoring, sim baking, importers, exporters | live render participation |
| **2 — in process, paint path** | managed assembly on the dab path | needs a posture **and** the determinism contract below | brushes, effects, media | — |

**Tier 0 already exists and already has doctrine.** `Lightbox.Mcp` references no
Lightbox assembly and talks over a pipe. `ROADMAP.md` applies the same reasoning
twice on its own terms — Laigter is shelled out to rather than linked because
linking a GPL-3.0 tool "would put Lightbox under GPL-3.0, which is a
project-level licensing decision and must not be made by accident", and the
Perforce and UVCS clients run as separate processes "to keep the licences
apart". An add-on at this tier raises no linking question at all, which makes it
the only tier shippable before a solicitor is consulted.

**Both named commercial features are tier 1.** Neither calls
`BrushEngine.StampStroke` — the only callers are the raster pipeline,
`ShapeBuilder` and the view model's own paint path. So tier 2 is not needed for
anything currently planned to be paid, and should not be built until something
actually requires it. It is the tier with every hard problem in it.

## What the boundary must not break

Four invariants, and one of them is genuinely hard across a trust boundary.

**Invariant 1 — the stroke record is the document.** Anything an add-on paints
goes through `BrushEngine.StampStroke`, or a reload renders a different image.
This is what the line at the top of this note restates from the product side.

**Invariant 2 — no randomness in rendering. This is the hard one.** Dab
dynamics are seeded from position via `Hash01(float x, float y, uint salt)`, so
re-renders, undo and AI inbetweens agree. An add-on that reaches for
`Random` breaks all three at once, and it breaks them *quietly* — the art looks
fine and boils at 12 fps. Two mechanisms, and the second is the one that works:

- **Give seeds, never let an add-on make one.** The tier-2 API passes
  `Hash01`-derived values in and exposes no RNG. Necessary, and not sufficient —
  nothing stops an add-on constructing its own.
- **Verify rather than trust.** Render a stroke twice and compare bytes. It is
  cheap, it is mechanical, and it is the only check that cannot be talked
  around. It belongs in the certification of any tier-2 add-on and in a debug
  build at runtime.

**Invariant 4 — settings that affect pixels are stored per stroke.** An
add-on's settings serialize *into the stroke*, so changing a preference never
alters existing art — and so a document made with add-on v1 still renders as v1
after v2 ships.

**Invariant 6 — painting is bounded work.** A tier-2 add-on must declare its
reach *before* it runs, so the dirty region is computable without executing it.
That is an API shape rather than a request: `Bounds(stroke) → Rect` separate
from `Render(...)`, and an add-on whose bounds lie is a defect the host can
detect.

**And the house rule, which costs nothing here.** *Optional means absent* — an
add-on nobody uses writes no keys and shows no UI. The serialization discipline
already in `CLAUDE.md` applies unchanged, and it is what makes a free document
byte-identical whether or not the add-ons are installed.

## Cost has to stay visible

`BrushCostOf` badges a brush `Fast`, `Textured` or `Expressive` **derived from
its settings, so it cannot lie**, and B177 is the recorded case of that badge
being wrong and being corrected because it was checkable. An add-on gets the
same treatment or the guarantee is one-sided: the picker must badge a
third-party brush from what it actually does, not from what its manifest claims.
An add-on that declines to be measured is badged `Expressive` by default.

## Entitlement, and where it cannot live

Under GPL-3.0 a recipient may modify and rebuild the core, so **a licence check
compiled into the core is removable by construction**. It therefore lives in the
add-on binary or server-side, and the core carries the extension point only.

The corollary is the part worth committing to: **the free core must be fully
functional with no add-ons present, and must never advertise its own
incompleteness.** No disabled buttons for features that are not installed, no
upsell surfaces in the GPL tree. That is the same rule as *optional means
absent*, applied to the commercial tier.

## What has to exist first

In dependency order, and the first item is the expensive one:

1. **A stable public API.** 805 public members on `MainViewModel` is not an API
   an add-on can be versioned against. The decomposition is a **prerequisite**
   rather than adjacent tidying — it is the only place a tier-1 add-on could
   attach. Roughly **9,600 lines** sit in partials that touch none of the
   render-pipeline state and could leave the type across three populations; the
   measurement and the residue-ratio experiment are in the conversation that
   produced Q131.
2. **A load host** — `AssemblyLoadContext`, a manifest, a capability
   declaration, and unload for a disabled add-on.
3. **API versioning**, so an add-on states which host it was built for and is
   refused rather than crashing.
4. **The rig decision above**, because it changes the document.

## What this note deliberately does not decide

- **The licence instrument.** A GPL-3.0 linking exception, an MPL-2.0 relicense
  of the core, or dual licensing — these differ in ways that are a solicitor's
  question and not an engineering one. Q54 and Q131 both say the same thing: an
  hour of advice is cheap now that the stakes are real.
- **Which roadmap features join the tier.** Only bone and fluid are named so far.
- **Pricing, packaging and delivery.** Out of scope here.
