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

### What a symbol layer may carry, in the first cut

Allowed: `Name`, `Visible`, `Opacity`, `BlendMode`, `Cels`, `Locked`,
`AlphaLocked`, `OnionEnabled`.

**Refused at load, loudly:** `Mask`, `Effects`, `Adjusts`, `ClipToBelow`,
`Depth`, `BoneId`, `SimId`, `GroupId`, `LinkId`, `IsBackground`,
`OmitFromExport`.

This is not squeamishness, it is the next section: those eleven are exactly the
properties whose rendering lives somewhere `SymbolRasterizer` cannot reach.

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

Three ways out, and the choice matters more than it looks:

| | What it costs |
| --- | --- |
| **Restrict what a symbol layer carries, composite in Raster** (recommended) | Opacity and blend mode are Skia, which Raster already has. One rendering path, no new seam. The price is that effects, masks and adjustment layers do not work *inside* a symbol in this cut. |
| Invert it: an interface in Core, App supplies the compositor | The `IPixelResampler` pattern, which this codebase already uses for exactly this shape of problem. But a symbol rendered with no compositor registered — a Raster unit test, the MCP server — needs a fallback, and a fallback *is* a second rendering path. Two paths that are supposed to agree and are only checked when someone remembers. |
| Move the compositor down into Raster | The honest long-term answer and much the largest. `EffectPasses` and `LayerShapes` reach into scene-level state; pulling them down is its own project with its own design note. |

**Take the first.** It buys the whole feature the question asked for — lines,
colour, shading and effects *as separate layers* — and defers only the ability to
put a live blur inside a symbol, which nobody has asked for. It also leaves the
second and third options open: the restriction is a loader rule, so lifting it
later changes no stored file.

The failure to avoid is the second option's fallback. This application has one
pixel path on purpose.

---

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

## Steps

Each is a commit, green, with its evidence anchors.

**L1 — the record and the file.** `Symbol.Layers`, the converter reading `frames`
and `layers`, the single-layer write rule, `FrameCount` over the stack.
Round-trip and absence tests. **No rendering, no UI** — every existing symbol
still renders through the old path because a one-layer stack is what it already
was.

**L2 — the loader's restriction.** The eleven refused properties, refused
loudly, with the reason in the message. Cheap, and it has to precede L3 or L3
silently renders a mask as nothing.

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
- **Effects, masks and adjustment layers inside a symbol.** The loader refuses
  them, and lifting that is the compositor-in-Raster project.
- **Rigging a symbol's layers.** `BoneId` is refused for the same reason, and it
  is a much larger question — a rigged symbol placed twice is two poses of one
  armature, which the record has no way to say.
- **Detaching a cycle into cels.** A different axis from detaching a drawing.

---

## What would make me change my mind

If L3 cannot make `SymbolRenderTests`' placed-twice-is-pixel-identical test pass
**without touching `Hash01` or the placement matrix**, the compositing is
happening in the wrong space and the design is wrong — stop and rethink before
L4, rather than adding a special case to the engine. Determinism outranks this
feature, and it outranked the pillar that introduced symbols too: that is the
clause that killed the original S3 and produced the better answer.

The second thing that would change it: if the eleven refused properties turn out
to include one an artist reaches for immediately — a mask is the candidate —
then the restriction is buying less than it costs, and the `IPixelResampler`
inversion becomes worth its fallback risk. That is a question to answer with a
real complaint, not in advance.
