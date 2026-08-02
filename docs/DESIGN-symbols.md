# Pillar 3 — reusable assets, as live symbols

*Edit the sword once, every animation holding it updates.*

This is the design for Pillar 3. It is written before the code because the
record change is the whole decision: everything else in the pillar — the
browser, the tagging, the pose and expression libraries — is a way of finding
and placing the thing this document defines.

---

## The decision

An asset is a **live symbol referenced by id**. A frame does not contain a copy
of the sword; it contains a *placement* — an id, a transform, and any
overrides. The strokes live once, in the project.

The alternative considered and rejected was a copied drawing with a link back
and an "update from source" button. It needs no new record type and keeps every
frame self-contained, which is genuinely cheaper. It was rejected because it
turns the pillar's one promise into a button somebody has to remember to press,
on every animation, after every edit. A hundred-shot production where the sword
is right in eighty of them is worse than no sharing at all.

### Why this is not as expensive as it looks

The engine already resolves things by id at render time and has done since the
palette work:

| What | Held by | Resolved by |
| --- | --- | --- |
| Swatch | `Stroke.SwatchId` | `PaletteRegistry.ResolveSwatch` |
| Gradient | `Stroke.GradientId` | `PaletteRegistry.ResolveGradient` |
| Brush tip | `Brush.TipId` | `BrushTipRegistry.Resolve` |
| Clip region | `Stroke.ClipId` | `ClipRegionRegistry.Resolve` |
| **Symbol** | `SymbolPlacement.SymbolId` | **`SymbolRegistry.Resolve`** — new |

A symbol is the fifth entry in a table with four rows in it already. The
pattern, the registry lifecycle, the reset-on-document-change funnel and the
export flattener all exist and all work.

### The invariant it strains, and where it is repaid

Invariant 1 says a document re-renders identically from its own record. A frame
holding `SymbolPlacement("sword-a", …)` does not, on its own.

This is the same boundary the project palette already crossed, and it is repaid
the same way: **the project is what re-renders**, and `Export document…`
inlines everything referenced. `ProjectIo.Flatten` already walks every stroke
and inlines swatches, gradients, tips and clip regions; symbols join that walk.
Invariant 1 then holds exactly where it must — at the point a file leaves the
application.

That flattener is the piece that rots silently, so it gets a **pixel-identity**
test like the palette one did, not a shape test.

---

## The record

```csharp
/// A drawing stored once and placed many times.
public sealed class Symbol
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<string> Tags { get; set; }        // the browser's index
    public SymbolKind Kind { get; set; }          // Prop, Pose, Expression, Hand, Fx, Background
    public List<Frame> Frames { get; set; }       // one frame for a prop, many for a cycle
    public int Fps { get; set; }                  // only meaningful with several frames
    public double PivotX { get; set; }
    public double PivotY { get; set; }
    public int Version { get; set; }              // bumped on every edit; what "outdated" means
}

/// One use of a symbol on one cel.
public sealed class SymbolPlacement
{
    public string Id { get; set; }
    public string SymbolId { get; set; }
    public double X, Y, ScaleX, ScaleY, Angle;
    public double Opacity { get; set; }
    public int FrameOffset { get; set; }          // which of the symbol's frames shows here
    public string? SwatchOverrideId { get; set; } // recolour this use without forking it
    public int SeenVersion { get; set; }          // what it was placed against
}
```

`PaintedFrame` gains `List<SymbolPlacement>? Placements` — **nullable, absent by
default**, so a document that never places one serializes exactly as it does
today. The camera's rule, for the fifth time.

### Two things that are deliberately not in there

**No nesting.** A symbol cannot contain a placement of another symbol, in the
first cut. Nesting brings a cycle check, a depth limit and a dependency graph
that has to be correct before anything renders, and none of that earns its
keep until somebody asks for a hand inside an arm inside a character. The type
allows it later; the loader refuses it now, loudly.

**No per-placement stroke edits.** A placement can be moved, scaled, rotated,
recoloured and time-offset. It cannot have one of its strokes nudged, because
that is a different drawing and pretending otherwise is how a symbol quietly
becomes a copy. **Break link** turns a placement into ordinary strokes on the
cel, and that is the honest way to get there.

---

## Rendering

`FrameRasterizer` gains one pass. For each placement, in order:

1. Resolve the symbol. Unresolved → draw nothing and record it on the document's
   problem list. **Never** substitute a placeholder into the pixels: a missing
   asset that renders as a pink box gets exported by somebody in a hurry.
2. Pick `Frames[(placementOffset + celIndex) % Count]`.
3. Stamp its strokes through `BrushEngine.StampStroke` — the one pixel path —
   with the placement's matrix applied as a **canvas transform**.

That last point is invariant 7 and it is not negotiable. Stroke coordinates are
never multiplied: `Hash01` seeds every dab dynamic from the IEEE-754 bits of a
position, so a scaled placement whose geometry had been multiplied would re-roll
scatter, size, flow, roundness and all three colour jitters. The sword at 0.8×
would not be the sword. `OutputScaleTests` already exists to keep that reason
written down; the symbol pass gets the same treatment.

**Determinism across placements is the subtle one.** Two placements of the same
symbol at different positions currently produce *different* dab jitter, because
the seed is positional. For a prop that is wrong — the same sword should look
like the same sword. So a placement renders in **symbol space** and is
transformed onto the canvas, which makes the seed a property of the symbol
rather than of where it was put. That is the whole reason the transform is a
canvas transform and not a coordinate rewrite, and it is worth its own test:
*the same symbol placed twice is pixel-identical under both placements' own
transforms.*

---

## Steps

Each is a commit, green, with its evidence anchors.

**S1 — the record.** `Symbol`, `SymbolPlacement`, `SymbolKind`, nullable on
`PaintedFrame`, round-trip. Absence test: a document with no placements writes
no key. No UI.

**S2 — the registry and the render pass.** `SymbolRegistry`, resolution through
`OnDocumentChanged`, the rasterizer pass, the canvas transform, the
placed-twice-is-identical test, an unresolved symbol renders nothing.

**S3 — the flattener.** ~~`ProjectIo.Flatten` inlines placements as ordinary
strokes.~~ **Built differently — see below.** `ProjectIo.Flatten` copies the
symbols an exported document places into `Doc.Symbols`, and walks their strokes
for shared swatches and gradients like any others. Pixel-identity test: export,
clear every registry, render, hash-compare against the in-project render.

**S4 — placing and moving.** Drag from a browser onto the canvas; the Move tool
already moves things and a placement is a thing. Break link. One undo step each.

**S5 — the browser panel.** A ninth panel, absent until a project exists — the
same treatment the project panel gets. Grid of thumbnails, filter by kind and
tag, search by name. Tagging is a text field on the symbol, not a taxonomy.

**S6 — editing a symbol.** Open it in a tab of its own, like a reference tab.
On save, `Version++` and every placement of it re-renders. This is the pillar's
headline and it is deliberately last: it is the cheapest step once S1–S3 hold,
and worthless before them.

**S7 — versioning and staleness.** `SeenVersion` against `Version` gives
"this placement was made against an older sword". Report it; do not fix it
automatically — a placement that silently changed under an animator is the
thing they will not forgive.

### After the first cut

The pose, expression, hand, face, prop and FX libraries in the roadmap are all
`SymbolKind` values plus a browser filter, so they land together at S5 rather
than as six features. **Animation templates**, **reusable animation presets**
and the **dependency graph** are separate work and are not in this cut.
Nesting, cross-project libraries and automatic upgrade are explicitly out.

---

## What would make me change my mind

If S2's placed-twice-is-identical test cannot be made to pass without touching
`Hash01`, the symbol transform is wrong and the design needs rethinking before
S3 — not a special case in the engine. The determinism invariant outranks this
pillar.

### What actually changed, and why

S2's test passed as written. **S3 did not**, and the clause above is what
settled it.

"Inline placements as ordinary strokes" cannot be built. Baking a placement's
transform into its strokes means multiplying their coordinates, and every dab
dynamic is seeded by `Hash01` from the IEEE-754 bits of a dab position — so a
flattened sword would come out with different scatter, size and colour jitter
from the one the artist approved. The step's own pixel-identity test would have
failed by construction: the two things S3 asked for contradicted each other.

So the symbols travel with the document instead. `Doc.Symbols` is nullable and
absent by default, exactly like `Doc.Palettes` inlined by the same flatten, and
an exported file renders through the identical pass rather than through a baked
approximation of it. Self-contained, and the same drawing.

The cost, stated plainly: an exported document is no longer *only* strokes — a
reader has to resolve placements to render it. That is the price of the export
being the same mark as the original, and it is the cheaper of the two.
