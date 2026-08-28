# Q171 · Should a symbol own a layer stack? — **answered 2026-08-28: yes, the Flash model**

Asked when the owner went looking for the workflow they already have in Adobe
Animate: an artwork is *several* layers — lines, colour, shading, highlights,
effects — and all of them are one thing worth reusing. A head, in other words.
Lightbox could not express it, and the investigation found three separate
reasons why, one of which was silently destructive.

## What the record says today

A document has two axes — layers (a stack) and cels (time):
`Scene → Layers → Cels → Frame`. A `Symbol` has **one**: `Frames` is a flat
`List<Frame>`, which is the *time* axis. There is no layer axis in a symbol at
all, so "make a symbol of my four layers" has nowhere to land.

Three places enforce it independently, which is why it did not read as a single
missing feature:

| Where | What it does |
| --- | --- |
| `MakeSymbolFromDrawing` | Takes `PaintTarget()` — the **active layer's** frame — and only its `Strokes`. |
| `OpenSymbol` | Builds the editing tab with `Layers = [layer]`, exactly one. |
| `SyncEditedSymbol` | `Layers.SelectMany(l => l.Cels)` — folds every layer into one frame list. |

**The third is a defect rather than a limit.** Nothing stops an artist adding a
layer inside a symbol tab (`AddLayer` has no guard), and the `SelectMany` then
turns the layer axis into the time axis. Measured on a scratch worktree at
`afba7436`:

```
beforeFrames=1  beforeLayers=1  →  layersAfterAdd=2  →  symbolFramesAfter=2
```

A lines layer and a colour layer become frames 1 and 2 of an animation. No
warning, no error, and the artist's structure is gone.

## The answer, and what it costs

| | What it costs |
| --- | --- |
| **A symbol owns a layer stack** (recommended, **chosen**) | The record, `OpenSymbol`, `SyncEditedSymbol`, the rasterizer pass and the flattener all move. Effectively "a symbol contains a Scene". L–XL. |
| Keep symbols flat; flatten on capture with a warning | S, honest, and the lines/colour split is lost inside the symbol forever. |
| Promote a `LayerLink` to a symbol that remembers its split | M — and it makes `Frames` mean two things at once, which is the exact trap the `SelectMany` above already fell into. |

**Flash is the precedent and the reason it works.** Every symbol there has its
own complete timeline *with its own layers*; nesting is unlimited; a Graphic
instance is locked to the parent timeline with a first-frame offset, which is
already what `SymbolPlacement.FrameOffset` is. Lightbox has the timeline half
and not the layer half, and that asymmetry is the whole of what is missing.

## The consequence that was prompted with it

**Detaching rebuilds the layer stack the symbol was made from.** In Flash this
is two operations — *Break Apart* un-nests onto the current layer, *Distribute
to Layers* spreads the result back out — and the pair only works because the
symbol had layers to begin with. Once a symbol owns a stack, one operation can
do both, and the alternative (always flatten to the current layer) would throw
away the structure this question exists to preserve.

`BreakLink` today bakes only the frame at the playhead, so an animated symbol
detaches to a single drawing rather than to its cycle. That stays true until the
stack lands; it is a separate question whether detaching a cycle should produce
cels.

## Not in this answer

- **Nesting.** The type has always allowed it and the loader refuses it. A
  layer stack does not by itself change that, and the cycle check, depth limit
  and dependency graph are still the reason it waits.
- **`LayerLink` is not the answer, but it is the raw material.** Its own doc
  comment already describes the thing this question asks for — *"a set of layers
  that are one drawing — lines, colour, details, effects"* — and it is a
  document structure, so it gives cohesion without reuse. It is the obvious
  capture unit when the stack is built.
