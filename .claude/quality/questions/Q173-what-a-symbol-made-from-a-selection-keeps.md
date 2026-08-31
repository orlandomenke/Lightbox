# Q173 · What does a symbol made from a selection keep at its edges? — **answered 2026-08-29: the clip, and the symbol carries its own regions**

Asked at L5 of Q171, when the owner added a gesture the design note had not
planned for:

> While drawing something we might want to make a part of that drawing a symbol.

## The tool that turned out not to be needed

The request came with *"a symbol selection tool I am also thinking of"*, and the
first useful finding is that there is nothing to build. The Select tool, the
marquee and the lasso already produce exactly the selection this wants, and
`SelectedStrokesForAnOperation` already resolves it — including three rules that
were expensive to get right: a marquee beats picked lines (Q97), strokes are
clipped at the boundary, and erasures are excluded because a copied erasure
lands on a layer with nothing beneath it (Q102, B232).

What is missing is a **command**, not a tool. That is worth writing down because
"add a tool" was the shape the request arrived in, and the cheaper answer was
sitting one call away.

## The problem the gesture exposes

A marquee capture clips the strokes it takes, and a clipped stroke carries a
`ClipId`. Clip regions live in `Doc.ClipRegions` and reach the renderer through
`ClipRegionRegistry.Register(Doc.ClipRegions)` — **populated from the active
document**.

A symbol does not live in a document. It lives in the project's
`assets/symbols.json`, or in the artist's library, and is placed into whatever
document the artist is drawing in. So a symbol whose strokes carry a `ClipId`
would resolve those ids against a *different* document's regions: the wrong
shape, or nothing at all, depending on what happened to be open.

Nothing catches this today because nothing makes a clipped symbol.

| | What it costs |
| --- | --- |
| **Clipped, and the symbol carries its own regions** (**chosen**) | `Symbol.ClipRegions`, nullable and absent unless used, plus a registry lifecycle that registers a symbol's regions when the symbol resolves. A second thing a symbol references, and a second thing the flatten has to walk. |
| The whole stroke, no clipping (recommended, **not** taken) | Nothing to carry and nothing to register. A stroke that only clips the edge of the marquee arrives whole, and is trimmed inside the symbol instead. |
| Refuse a marquee; picked lines only | Smallest, and much narrower than the selection tools an artist already reaches for. *Lasso the sword* is the obvious thing to try. |

**The owner took the first, against the recommendation, and the reason it is the
better answer is consistency:** Copy already clips at the boundary, so a capture
that did not would make two gestures over the same selection mean two different
things. The cheaper option buys its simplicity by making the artist notice which
gesture they used.

The price is stated rather than discovered: a symbol becomes the second kind of
thing that references a clip region, and `ProjectIo.Flatten` grows another walk —
the same shape as the swatch walk that already had to learn about symbols, and
which was added after an exported sword came out in the literal colours its
strokes were carrying rather than the ones it was painted in.

## What is not in this answer

- **Where the source drawing keeps its edge.** Untouched: taking the selection
  out is `DeleteSelectionContents`, which already turns a boxed region into a
  `ToolKind.ClearRegion` stroke so lines crossing the edge keep the part outside
  it. A capture reuses that rather than deleting the strokes it took.
- **Whether a captured selection could span layers.** It cannot, and that is
  the *other* half of L5 — capturing whole layers, from a `LayerLink` or a
  docker multi-selection. Different gesture, same replacement.
