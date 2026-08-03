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

---

## Animated symbols as a reference underlay

*Written up after the question "are symbols only single images, or could we make
an animation symbol — a run cycle as reference?"*

### The part that already exists

**A symbol has always been able to hold an animation.** `Symbol.Frames` is a
list, `Symbol.Fps` sets its rate, a placement advances with the timeline, and
`SymbolPlacement.FrameOffset` shifts where in the cycle it starts — so one stored
walk can carry two characters half a stride apart. Editing it opens a cel per
frame and it is animated with the ordinary tools. So "an animation symbol" is not
a feature to add; it is a symbol with more than one frame in it.

What was missing is not the *animation*. It is the **underlay**: a reusable
animation you draw over, that is never part of the artwork, and that updates
everywhere when the source is edited.

### The composition, and it is nearly complete

Three parts exist independently and add up to almost the whole thing:

| Part | State |
| --- | --- |
| A reusable, live-linked animation | `Symbol` with several frames. Built. |
| Reference footage against the timeline | `ReferenceStrip` / `StripSlicer`. Built, for *imported* footage. |
| A layer that never exports | `Layer.OmitFromExport = true`. Built, for backgrounds. |

Place an animated symbol on a layer pinned out of exports and you have a
reusable, live-updating, non-exporting animated underlay **today**. Edit the base
run cycle once and every shot drawn over it follows; export and it is not there.

That is the design, and the honest framing of the remaining work is that it is
**presentation and discoverability, not mechanism**:

1. **It reads as artwork.** A reference wants to look like a reference — the
   onion-skin treatment, a flat tint at low opacity, not a full-strength drawing
   competing with the line on top of it. Layer opacity gets part of the way and
   is the wrong control for the job: it is a compositing value the artist may
   also want for real reasons.
2. **Nothing points at the workflow.** No menu says "use this as a reference",
   so an artist would have to invent the three-step recipe. A feature nobody can
   find is not shipped, whatever the record can express.
3. **It is not obviously non-exporting.** The pin is right, but "this layer is a
   guide" and "do not export this layer" are the same setting wearing one label,
   and the first is what the artist means.

### The shape it should take: `LayerRole`

A nullable role on `Layer` — the camera's rule, absent until used — with one
value to start: `Reference`.

```
Layer.Role = LayerRole.Reference
  → rendered as a ghost (the onion-skin path, not layer opacity)
  → never exported (implies the existing pin, without spending it)
  → refuses paint? No — see below
  → shown in the Layers docker with the reference badge, not a hidden flag
```

Three decisions worth settling now, because they are the ones that will be got
wrong later:

- **A reference layer is not locked.** Roughing *on* the reference before
  committing on a clean layer is a real way to work, and locking it would make
  the feature narrower than the underlay it replaces.
- **The role implies the export pin rather than replacing it.** They stay
  separate fields: an artist can pin an ordinary layer out of exports without
  making it a ghost, and that is a different intention.
- **Ghost rendering goes through the onion-skin path**, not through a second
  tinting implementation. There is one way to draw "this is not the current
  drawing" in this application and adding a second would let them drift.

### Why this serves the on-model goal too

The question also noted that a reference document is meant to keep drawings **on
character**, so it serves more than one goal. That is right, and it is why this
should not become a fourth parallel feature. There are two presentations of one
idea:

- **A reference you look at** — the character sheet, `ReferenceSheet`, in its own
  tab. On-model checking, turnarounds, expression sheets.
- **A reference you draw over** — the same art, on the canvas, under the line,
  aligned to the timeline.

The base-character case the question describes — an unstyled, line-only rig whose
cycles are drawn over per shot and updated centrally — is the second presentation
of the first idea, and it is the strongest argument for the role: a studio's
"base knight, no styling, 12-frame run" is one global symbol, and every animation
that uses it is a layer pointing at it.

### Not doing

- A *second* record for animated references. `Symbol` already is one, and a
  parallel `ReferenceAnimation` would be the mistake Q11's "reusable animation
  presets" was struck for: a feature nothing can distinguish from a shipped one.
- Onion skin *of* a reference layer. A ghost of a ghost is unreadable, and the
  reference already shows the whole cycle by advancing with the timeline.
- Auto-tracing a reference. That is inbetweening pointed at the wrong problem.
