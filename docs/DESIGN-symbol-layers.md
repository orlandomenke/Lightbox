# Q171 — a symbol owns a layer stack

*A head is a lines layer, a colour layer, and two effect layers. All four are
one thing worth reusing.*

This is the design for Q171. It is written before the code for the reason
`DESIGN-symbols.md` was: **the record change is the whole decision**, and
everything else — capture, the editing tab, detaching, export — is a
consequence of it.

---

## Where this starts

A document has two axes. A symbol has one:

| | Layer axis | Time axis |
| --- | --- | --- |
| `Scene` | `Layers` | `Layer.Cels` |
| `Symbol` | **absent** | `Frames` |

`Symbol.Frames` is a flat `List<Frame>`, and it is *time*. That is why a second
layer in a symbol tab used to become frame 2 of the animation: there was nowhere
else for it to go. The guard that stopped it (`AddLayer` refuses,
`SyncEditedSymbol` reads one layer by id) is a holding action, and both halves
come out here.

Three things enforce the single layer today, and each is a step below:
`MakeSymbolFromDrawing` captures the active layer alone, `OpenSymbol` builds the
editing tab with `Layers = [layer]`, and `SyncEditedSymbol` reads one layer back.

---

## The decision

**`Symbol` gains `List<Layer>` and loses `List<Frame>` as its model** — the same
`Layer` the document uses, with the same `Cels`, holds and exposure.

```csharp
public sealed class Symbol
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<string> Tags { get; set; }
    public SymbolKind Kind { get; set; }
    public List<Layer> Layers { get; set; }   // was List<Frame> Frames
    public int Fps { get; set; }
    public double PivotX { get; set; }
    public double PivotY { get; set; }
    public int Version { get; set; }
}
```

### Why the document's `Layer` and not a narrower `SymbolLayer`

A trimmed `SymbolLayer` — name, opacity, blend mode, cels — is the tempting
shape. It is smaller, it serializes tightly, and it says exactly what a symbol
can hold.

**It is also the mistake this codebase has already made once and undone.** There
used to be `PaintedFrame` and `VectorFrame`, and `Frame`'s own doc comment
records how that ended: *the vector class was not a different kind of drawing, it
was this one with two abilities removed, and the only thing the distinction
decided was what a frame could not hold.* It produced B132 — placing a symbol on
a vector layer returned early, silently, because the frame had nowhere to put it.

`SymbolLayer` would be `Layer` with abilities removed. The second type would
drift from the first, every feature added to layers would have to be added twice
or deliberately not, and the answer to "why can't I do this inside a symbol"
would be a type rather than a reason.

So the type is shared, and **what a symbol layer may carry is decided by the
loader, not by the record** — which is exactly how nesting is already handled.

### What a symbol layer may carry

**Everything the compositor can render**, which after the move in the next
section means opacity, blend mode, visibility, masks, live effects, clipping to
the layer below, and adjustment layers. A head keeps its lines, its colour, its
shading and its *live* effect layers, and all of it travels with the symbol.

Four are still refused at load, and each for a reason that is not about
rendering:

| Refused | Why |
| --- | --- |
| `BoneId` | A rigged symbol placed twice is two poses of one armature, and the record has no way to say that. Its own question, much larger than this one. |
| `SimId` | A simulation is a thing that runs over a timeline; a symbol placed with a frame offset would need two of them out of step. Same shape of problem as bones. |
| `GroupId`, `LinkId` | Both name something in the *document's* layer list. Inside a symbol they would point at nothing, and a dangling id that renders fine is worse than a refusal. |
| `IsBackground`, `OmitFromExport` | Both are statements about a document's output, and a symbol is not a document. `OmitFromExport` inside a symbol has no meaning that is not already the layer's visibility. |

`Depth` is the one genuinely open question and it is deferred rather than
refused — see *Not in this cut*.

---

## The constraint that shapes everything else

**`SymbolRasterizer` is in `Lightbox.Raster`. The layer compositor is in
`Lightbox.App`.**

```
Lightbox.App  →  Lightbox.Raster  →  Lightbox.Core
```

`SceneRenderer`, `EffectPasses`, `LayerShapes` and `FrameBitmapCache` are all in
`Lightbox.App/Rendering`. `SpriteSheetExporter.ComposeFrame` is the reference
implementation of "a scene's layers at frame *n*", and it is a private method in
the App project. `Lightbox.Raster` cannot reference any of it.

And symbol rendering genuinely has to work from inside Raster:
`FrameRasterizer.StampPlacements` calls `SymbolRasterizer`, so moving symbol
rasterization up into App is not available.

### The measurement that settled it

The first draft of this note recommended *restricting what a symbol layer
carries* so compositing could stay in Raster, and costed "move the compositor
down" as **its own project**. Both were wrong, and the owner's answer to the
question the draft raised is what forced the check: the effect layers in a head
are **live** effects, not hand-painted ones, so a symbol that cannot hold one is
a symbol that cannot hold the artwork this feature exists for.

So the cost of moving was measured rather than guessed. Every file the
compositor needs was compiled *inside* `Lightbox.Raster` as a spike:

| File | Lines |
| --- | --- |
| `SceneRenderer` | 1102 |
| `FrameBitmapCache` | 646 |
| `GpuComposite` | 331 |
| `GpuComposeProbe` | 263 |
| `LayerTextureCache` | 208 |
| `LayerShapes` | 124 |
| `CameraTransform` | 101 |
| `EffectPasses` | 81 |
| **Total** | **2,856** |

**All 2,856 lines compile inside Raster with exactly three errors, and all three
are the same thing:**

```
FrameBitmapCache.cs:20    Services.MemoryBudget.FrameCache()
LayerTextureCache.cs:83   Services.MemoryBudget.LayerTextures()
GpuComposeProbe.cs:145    Services.DiagnosticLog.WriteNote(…)
```

Not one of them is rendering. Two are the **default value of a settable
property** — how much memory to spend — and the third is where to write a
diagnostic note. `GpuComposite` turns out to depend on `SkiaSharp` alone, with
no Avalonia anywhere; `CameraTransform` is Core plus Skia; the two references to
`LiveTipPlan` and `MainViewModel` in `SceneRenderer` are **in doc comments**.

The compositor is not in App because it belongs there. It is in App because
that is where it was written.

### The decision

**Move the compositor down into `Lightbox.Raster`,** and invert the three policy
calls:

- `FrameBitmapCache.ByteBudget` and `LayerTextureCache.BudgetBytes` already are
  `{ get; set; }`. Raster carries a conservative default; App sets the measured
  one at startup. This is the `IPixelResampler` shape — Core or Raster declares
  what it needs, the layer that knows the machine supplies it — and it is one
  line each.
- `GpuComposeProbe`'s note becomes a nullable sink that App wires to
  `DiagnosticLog`. A diagnostic that is absent writes nothing, which is already
  what `DiagnosticLog`'s own contract promises: *nothing here throws, a log that
  can break the application is worse than no log.*

What this buys, beyond symbols: `SpriteSheetExporter.ComposeFrame` stops being a
private reimplementation of compositing in the App project, and the export path,
the canvas path and the symbol path all reach the same one.

**The two rejected options, kept because the reasons are still true:**

| | Why not |
| --- | --- |
| Restrict what a symbol layer carries | It was the first draft's recommendation and the owner's answer killed it: live effect layers inside a symbol are the *point*, not a nice-to-have. Restricting would have shipped a feature that could not hold the artwork it was built for. |
| Invert with an interface, App supplies the compositor | A symbol rendered with no compositor registered — a Raster unit test, the MCP server — needs a fallback, and a fallback **is** a second rendering path. Two paths that are supposed to agree and are only checked when someone remembers. This application has one pixel path on purpose. |

### What this does not license

The move is **mechanical and must stay mechanical**. It is a file move, a
namespace change, three inversions, and the `using` lines that follow — no
behaviour change, no tidying, no "while I am here". Compositing is the most
load-bearing code in the application and the whole argument for moving it is
that nothing about it changes.

The guard on that is the existing suite: `SpriteSheetExportTests`, the effect
compose-cost tests and the render tests all exercise this code and none of them
should need editing. **If a test needs its expectations changed, the move stopped
being mechanical and should be stopped and re-read.**

## Rendering, which is smaller than it looks


`SymbolRasterizer` already funnels every placement through one method:

```
Stamp(placement)
  → Resolve(symbol, index, info, scale)      // cache by symbol|version|frame|size|scale
      → Render(frame, info, scale)           // ← the only thing that changes
          → FrameRasterizer.Materialize(frame, …)
          → crop to ink
  → PlacementMatrix(placement, symbol)       // pivot, scale, rotation
  → draw the cached bitmap
```

**Only `Render` changes.** It stops taking a `Frame` and starts taking the
symbol and a time index, composites the stack into the same full-size surface,
and crops as before. Everything downstream — the ink crop, the cache key, the
placement matrix, the resample-don't-re-rasterise rule — is untouched.

That matters for the invariant. Compositing happens **in symbol space, before
the placement transform**, so:

- No stroke coordinate is rewritten, so no `Hash01` seed moves (invariants 2
  and 7).
- The same symbol placed twice is still the same mark twice, because the
  composite is a property of the symbol and not of where it was dropped.
- `SymbolRenderTests`' placed-twice-is-pixel-identical test must keep passing
  **unchanged**. If it cannot, this design is wrong and needs rethinking before
  anything ships — the same clause `DESIGN-symbols.md` wrote for S2, and it
  earned its keep there.

The full-size-then-crop pass stays for the reason it exists: a smudge or blur
stroke inside a symbol samples the target bitmap by document coordinate, and a
pre-cropped surface would have it sampling the wrong place.

### What "which frame" means now

`FrameIndexAt` is unchanged, but `FrameCount` stops being `Frames.Count`:

```csharp
public int FrameCount => Math.Max(1, Layers.Count == 0 ? 0 : Layers.Max(l => l.Cels.Count));
```

Holds now mean something inside a symbol — a `Cel` with a null `Frame` is a
layer that does not change on that frame, which is what lets a colour layer sit
still under a moving line. `ExposureSheet.ExposedFrame` already answers this and
is in `Lightbox.Core`, so Raster can call it.

---

## Serialization, and the file that must not change

The rule this repository applies to every optional thing: **absent unless used**
— a document that does not use a feature serializes exactly as it did before the
feature existed.

Every symbol that exists today is one layer with default properties. So:

- **Read** `frames` (old) *or* `layers` (new). One converter, the shape
  `FrameConverter` already established.
- **Write** `frames` when the stack is exactly one layer carrying nothing but
  cels; **write** `layers` otherwise.

Every symbol in every project on disk round-trips byte-identically, and a
`layers` key appears in a file exactly when there is a stack in it.

`ASingleLayerSymbolWritesNoLayersKey` is the guard, and it belongs in the same
commit as the converter — the `optional-settings` skill exists because this is
the rule that gets asserted and not checked.

The two storage sites are unchanged in shape: `assets/symbols.json` for a
project, `symbols.json` beside the settings for the artist's library, and
`Doc.Symbols` as the flattening target.

---

## Capture: how a stack gets into a symbol

`MakeSymbolFromDrawing` takes the active layer and only its strokes. It gains a
sibling rather than a flag, because the two gestures answer different questions.

**`MakeSymbolFromLayers(name, kind, layers)`** — the layers in stack order,
their strokes cloned into the symbol, and every one of them replaced by a single
placement on the lowest of them.

Two routes to it, and both already have a natural handle:

- **A `LayerLink`.** Its own doc comment describes this feature: *"a set of
  layers that are one drawing — lines, colour, details, effects — declared once
  so they behave as one."* That is the unit, already in the record, already
  spanning folders, already surviving every frame. *Make a symbol of this link*
  is the gesture.
- **A multi-selection in the Layers docker**, for the case where the artist has
  not made a link and does not want one.

The existing single-layer gesture stays exactly as it is. It is the common case
and it should not grow a dialog.

**Two things capture must get right**, both learned from the existing gesture:

- The strokes keep their own coordinates and the placement sits at the origin,
  so what renders afterwards is the same mark — no coordinate rewritten, no
  seed moved. `MakeSymbolFromDrawing` already asserts this in pixels; the
  multi-layer form needs the same test, not a shape test.
- A frame that is imported pixels with no strokes still cannot become a symbol.
  That limit is `PngBase64` having no provenance, and it is unchanged here.

---

## Detaching: the stack comes back

Q171's second half. `BreakLink` bakes the symbol's strokes onto the current
drawing; with a stack it **rebuilds the layers**.

Flash does this as two operations — *Break Apart* un-nests onto the current
layer, *Distribute to Layers* spreads the result out. Lightbox has no
"distribute" concept and does not need one: the symbol knows its own stack, so
one operation can put it back.

- The symbol's layers are inserted **above the layer holding the placement**, in
  stack order, named after the symbol's layers.
- The placement is removed and the layers carry the baked strokes.
- **One undo step**, as break-link already is.
- Still the frame at the playhead. Detaching a *cycle* into cels is a separate
  question and is not in this cut — it is a different axis and a different
  gesture.

Break-link remains the one place in the application where a mark is allowed to
change, and for the reason already written down at `BreakLink`: it is
deliberate, artist-initiated and single-undo, which is the case invariant 2 does
not cover.

---

## The editing tab, and the guard coming out

`OpenSymbol` stops building a one-layer wrapper and starts building a scene from
the symbol's stack. Then:

- **`AddLayer`'s refusal comes out.** Adding a layer inside a symbol is the
  point of the feature.
- **`DocumentTab.SymbolLayerId` and the one-layer read in `SyncEditedSymbol`
  come out.** The sync writes the stack back.
- The tab keeps its transparent background, its no-file-to-save behaviour and
  its version bump, all unchanged.

The guard's tests invert rather than disappear: *adding a layer in a symbol tab
is refused* becomes *adding a layer in a symbol tab adds a layer to the symbol*.
The one to keep as-is is `ImportingACycleStillLandsItAcrossTheTimeline`, which
guards the axis this feature is most likely to damage by accident.

---

## The outside: a placed symbol and the rig

Everything above is about a symbol's **inside**, where `BoneId` is refused. The
owner asked the other half, and it is a fair and different question:

> I am fine with a symbol not containing bones. But an imported symbol can still
> be attached to a bone?

**Today, no — and it is two gaps, not one.** Both were measured rather than
assumed:

| Route | State |
| --- | --- |
| A placement on a rigged layer follows the bone | **No.** `Skinning.PoseFrameForRender` loops `frame.Strokes` and transforms stroke control points. The word `Placement` does not appear in `Skinning.cs`. |
| A symbol pinned to a named point that rides a bone | **Half.** `VariantAttachment` (Q143) pins a symbol to an anchor and is built and working. But `Anchors.ResolvedAt` reads the anchor stored on each *drawing*, and nothing applies the armature to it — `ROADMAP.md` lists *anchors riding bones* as still to come in bones phase 1. |

So a symbol rides a point the artist moves per drawing, which works and is the
normal thing for frame-by-frame. It does not ride a bone the artist poses.

### The decision: anchors carry it, and placements do not gain a `BoneId`

**Anchors ride bones, and a symbol pinned to an anchor comes along for free.**

The alternative — `SymbolPlacement.BoneId`, posed directly — is rejected. It is a
second mechanism for pinning a symbol to a character, doing what anchors already
do, and the sword would then have two answers to "what am I attached to" that
can disagree. Anchors also already carry a *direction* (Q144), so
`FollowingTheAimTurnsThePlacementAndItsOffset` means rotation arrives with the
position rather than needing its own field.

This is not new work invented here. It is bones phase 1 work this feature
**depends on**, and it pays for itself twice over: the limb-length guide is
parked on the same item.

### Why this is cheap, and why the determinism argument does not apply

Worth stating because it is the opposite of the situation everywhere else in
this note.

Posing a **stroke** is expensive and dangerous: control points move, so
`Hash01` reseeds unless the dynamics are seeded from bind-pose coordinates —
which is exactly the trap `docs/DESIGN-bones.md` spends its length on.

Posing a **placement** is neither. A placement is *already* a transform —
position, angle, scale about a pivot — and `SymbolRasterizer` renders the symbol
in symbol space and then applies that transform to the finished image. So a bone
moving a placement changes the matrix and nothing else:

- **No seed moves**, because no coordinate is rewritten. Invariant 2 is not in
  play at all.
- **The render cache still hits.** The key is
  `symbol|version|frame|size|scale`, and posing changes none of those — a bone
  swinging a sword through a hundred frames re-renders the sword zero times.

The expensive half of rigging is already paid for by the design that made
symbols deterministic.

### The one thing that still needs deciding

**Does a plain placement on a rigged layer follow that layer's bone, or does
pinning have to be explicit through an anchor?**

The recommendation is **explicit**: a placement follows a bone only when it is
attached to an anchor that rides one. Implicit would mean that rigging a layer
silently moves every symbol anybody ever dropped on it, including the background
prop that was only there because that layer happened to be selected — and
"my scenery moved when I posed the arm" is a bug report nobody enjoys.

Left as a question rather than built, because it belongs to the bones work and
not to this note.

## Steps

Each is a commit, green, with its evidence anchors.

**L1 — the record and the file.** `Symbol.Layers`, the converter reading `frames`
and `layers`, the single-layer write rule, `FrameCount` over the stack.
Round-trip and absence tests. **No rendering, no UI** — every existing symbol
still renders through the old path because a one-layer stack is what it already
was.

**L2 — the compositor moves down.** The eight files into `Lightbox.Raster`, the
three policy calls inverted, `SpriteSheetExporter.ComposeFrame` rewired to the
moved one. **Mechanical, and the guard is that no existing test changes its
expectations** — if one does, stop and re-read. Then the four refused properties
above, refused loudly at load, with the reason in the message.

This is the step with the risk in it, and it is worth doing on its own branch
with the whole suite green before L3 starts. It touches nothing about symbols;
it is the thing that makes L3 possible.

**L3 — the render pass.** `Render` composites the stack in symbol space.
`SymbolRenderTests`' placed-twice-is-identical test passes **unchanged**, and a
new test asserts a two-layer symbol composites in stack order at the right
opacities.

**L4 — the editing tab.** `OpenSymbol` builds the stack; `SyncEditedSymbol`
writes it back; the guard and `SymbolLayerId` come out.

**L5 — capture.** `MakeSymbolFromLayers`, from a `LayerLink` and from a docker
multi-selection. Pixel-identity test on the capture, as the single-layer gesture
has.

**L6 — detach.** `BreakLink` rebuilds the stack, one undo step.

**L7 — the flattener and export.** `ProjectIo.Flatten` already copies whole
symbols into `Doc.Symbols` rather than inlining strokes, so a stack travels
without changes — which is the payoff of the decision recorded at the end of
`DESIGN-symbols.md`. It still gets a **pixel-identity** test, because that is
the piece that rots silently.

L1–L3 are the feature; L4–L6 are the ways in and out of it; L7 is proving the
file still means the same thing.

---

## Not in this cut

- **Nesting.** A symbol containing a placement of another symbol is still
  refused by the loader. A layer stack does not change the cycle check, the
  depth limit or the dependency graph, and those are still the reason.
- **Rigging a symbol's layers.** `BoneId` is refused *inside* a symbol: a rigged
  symbol placed twice is two poses of one armature, and the record has no way to
  say that. Its own question. The **outside** of it — a placed symbol following a
  bone — has a section above and an answer: anchors carry it, and it waits on
  bones phase 1.
- **`Depth` inside a symbol.** Multiplane depth is a property of a *scene's*
  camera, and a symbol has no camera. Whether a placed symbol's internal depths
  should compose with the placing document's is a real question with no obvious
  answer, so it is refused for now and marked as the one to revisit — unlike
  the other three, refusing it is a guess rather than a reason.
- **Detaching a cycle into cels.** A different axis from detaching a drawing.

---

## What would make me change my mind

If L3 cannot make `SymbolRenderTests`' placed-twice-is-pixel-identical test pass
**without touching `Hash01` or the placement matrix**, the compositing is
happening in the wrong space and the design is wrong — stop and rethink before
L4, rather than adding a special case to the engine. Determinism outranks this
feature, and it outranked the pillar that introduced symbols too: that is the
clause that killed the original S3 and produced the better answer.

The second thing that would change it is L2. The spike says the compositor moves
with three inversions and no behaviour change, and a spike is a compile, not a
test run. **If moving it makes any existing test change its expectations, the
move is not mechanical after all** — and then the honest answer is to stop,
because "restrict what a symbol can hold" is still available and is a smaller
thing to be wrong about than compositing.

This note has already been wrong once in exactly that direction: the first draft
recommended the restriction and costed the move as its own project, on a guess.
The measurement took twenty minutes and reversed it. The lesson is cheap to
state and was not free to learn — *this repository costs its refactors by
compiling them, not by looking at them.*
