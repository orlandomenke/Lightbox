# Brush tips — a generated library, not a live calculation

Reference for the brush-tip work. It covers the tip *asset*: where tips come
from, how they are made, how they are stored, and how one gets chosen at stroke
time. It does not cover the medium simulation — that is
`DESIGN-fluid-media.md`, and the two meet in exactly one place, noted at the
end.

## The decision that shapes everything else

**A tip is an asset in a library. It is generated once and then only looked
up.** Nothing about a tip is computed while a stroke is being drawn.

That is already how the engine treats imported tips — `BrushTipRegistry`
decodes a PNG once, holds the `SKBitmap` *and* an `SKImage` beside it, and the
comment there says why: a stroke stamps hundreds of dabs, and wrapping the
bitmap per dab would allocate per dab and throw away the mipmap chain Skia
builds on downscale. The generator work extends that rule rather than
introducing it: **procedural tips are baked to the same raster form as scanned
ones, and after that the engine cannot tell them apart.**

Two consequences worth stating before anything else, because they decide the
data model:

- **The library stores the raster, not the recipe.** A tip generated from
  "soft circle, hardness 0.4" is stored as pixels. Storing the recipe and
  re-deriving at load would mean that improving the falloff curve silently
  repaints every drawing that ever used it — invariant 1, from the other
  direction. The recipe is kept *alongside* as provenance so the artist can
  reopen and tweak, but what renders is the baked raster, and tweaking produces
  a new tip rather than mutating the old one.
- **Nothing here reaches the stroke record except an id.** `BrushSettings.TipId`
  already exists and is already the whole coupling. Everything below is about
  what that id resolves to.

---

## What already exists — do not rebuild it

The spec this is drawn from describes a whole engine. Most of the runtime half
is built. This table is here so the tip work does not re-implement it.

| Spec asks for | Already in the code |
| --- | --- |
| Grayscale tip, high value = opacity | `BrushTipRegistry` — importers bake grayscale into the alpha channel |
| Bake once, cache, never parse mid-stroke | `BrushTipRegistry` holds decoded `SKBitmap` + `SKImage` per id |
| Bilinear filtering, never nearest-neighbour | `BrushTipSamplingTests` — `AnEnlargedTipIsSmoothedRatherThanBlocky`, `AMinifiedTipIsAveragedNotPointSampled`. This was B84 and it is closed |
| Mipmap chain for small radii | Skia builds it on the cached `SKImage`; the registry exists partly to stop it being discarded |
| Rotation, and rotation to stroke direction | `TipRotationDeg`, `RotationJitter`, `AngleFollowsDirection` |
| Dab positions along the path, spacing, per-dab alpha × pressure × flow | `BrushEngine.StampStroke`, `DabPositions`, `GeometryOps.Densify` |
| Dual-texture engine — tip × canvas grain | `TextureSurface` (`PaperKind`) × `TextureScale` × `TextureDepth`, via `PaperField` |
| Dense spacing for oil ribbons | `Spacing`; the Oil preset already ships 0.06 |
| Loading tips from art-tool formats | `.abr`, `.gbr`, `.gih`, `.kpp` readers in `Lightbox.Import` |
| Tips travel with the document | `Doc.BrushTips`, resolved by `TipId`, inlined by `ProjectIo.Flatten` |

Three places where the spec would be a **regression** if followed literally:

1. **Do not hand-roll bilinear sampling or a mipmap chain.** Skia does both, on
   the cached image, and `BrushTipSamplingTests` pins the behaviour. A hand
   loop would be slower and would reopen a closed bug.
2. **Do not add a second wet edge.** The spec's Part 4.3 — accumulate the
   borders where consecutive stamps overlap — is a stand-in for what
   `FluidLattice`'s capillary term now does properly (B24). `BrushEngine`
   already guards against running the cheap version alongside the simulation,
   and adding a third would triple the rim.
3. **Do not build screentones with `x % spacing == 0`.** That is a one-pixel
   hard line: it aliases at every scale, and a mark whose grid phase shifts
   between frames *boils* at 12 fps, which is the failure mode CLAUDE.md names
   explicitly. See *Hatching and screentones* below for what replaces it.

One place where the roadmap currently over-claims, found while writing this:
**"Brush rotation and tilt support" is ticked, and tilt does not exist.**
`StrokePoint` is `(X, Y, Pressure)`. Rotation is real; tilt is not read from
any device, not stored, and not used. That item is split below.

---

## What is missing

Five things, roughly in dependency order.

1. **A tip library.** Tips exist today only inside a document, and only because
   an importer put them there. There is no collection, no browser, no way to
   make one, and nothing shared between documents.
2. **A procedural generator.** No code produces a tip matrix from a formula.
   Round dabs are drawn by Skia as circles, which is fine and fast, but it
   means there is no path from "I want a ring, or a chisel, or a hatch" to a
   tip.
3. **An image-to-tip pipeline.** The importers read *brush formats*. There is
   no path from "here is a 600 DPI scan of an ink stamp" to a usable tip —
   levels, squaring, centring, inversion and edge masking are all currently
   the artist's problem in another application.
4. **Tilt, and velocity.** Neither is in the stroke record. Both are
   prerequisites for the multi-capture work and neither is a small change,
   because `StrokePoint` is part of the serialized document.
5. **Multi-capture tip sets.** A tip that is several captures indexed by tilt
   and speed, blended at runtime.

---

## 1. The tip record and the library

```
BrushTip
  Id            "tip_…", globally unique, already the registry's key
  Name          what the artist called it
  Png           the baked raster — square, power-of-two, shape in alpha
  Pivot         (x, y) in normalised tip space, default (0.5, 0.5)
  Origin        Procedural(recipe) | Scanned(source, adjustments) | Imported(format)
```

`Pivot` is the one addition that is not obvious and it earns its place twice.
It is where the pen tip touches, which is not the centre of the mark for
anything captured at an angle — and it is what makes blending between two
captures align instead of ghosting. The spec makes that a scanning discipline
("align them by their pivot point"); making it a field makes it *data*, which
means it can be nudged after the fact and cannot be silently got wrong.

`Origin` is provenance, not a render input. Nothing reads it while drawing.

**Where the library lives** follows the rule already settled for palettes and
brush presets, and for the same reason: a tip is part of how a project looks.

- **Project tips** in `<project>.lbproj/tips/`, shared by every document under
  it, resolved through the project the way palettes are.
- **User tips** in the app's own store, available with no project open.
- **Document tips** — `Doc.BrushTips`, which already exists — stay as the
  flattening target. `ProjectIo.Flatten` inlines the tips a document actually
  references, so an exported file still renders standalone. That is invariant 1
  at the boundary, and it is machinery that already works; the tip library just
  becomes another thing it inlines.

A **tips panel**, absent until it has something to show, on the pattern the
symbol panel already set.

---

## 2. The procedural generator

A square `D × D` matrix, `D = 2 × Radius`, centre at `(Radius, Radius)`, alpha
from the Euclidean distance `d` to the centre. Baked to PNG and handed to the
library; never evaluated during a stroke.

**Every formula below is anti-aliased at the boundary.** The spec's hard
`if d <= Radius` produces a stair-stepped circle, and at animation frame rates
the steps crawl. The rule for the whole generator: wherever a formula has a
threshold, the output is the *coverage* of that threshold over the pixel — one
`smoothstep` across a one-pixel band — not a binary test.

- **Hard circle** — `1` inside, `0` outside, with a one-pixel feathered
  boundary. "Hard" describes the falloff, not the sampling.
- **Soft circle** — full inside `Radius × Hardness`, then smoothly to zero at
  `Radius`. Worth noting the engine already has a `Hardness` with an
  established curve for its round dab; the generator must produce a tip that
  *matches* it at the same hardness, or the same slider will mean two different
  things depending on whether a tip is selected.
- **Ring** — full between `InnerRadius` and `OuterRadius`, feathered on both
  edges. This is the one the spec gets right as written.
- **Chisel / flat** — not in the spec and it should be: it is the shape that
  makes `AngleFollowsDirection` worth having, and it is the cheapest useful
  answer to `BristleDrag` (see the last section).

### Hatching and screentones

Two different features that the spec runs together, and separating them is the
whole design note here.

- **Hatching as a tip** — the grid is in tip-local space, so it rotates with
  the dab and follows the stroke. That is what you want for a cross-hatching
  brush. Built as: signed distance to the nearest grid line, `smoothstep`ed
  over the line width. Line width and spacing are both in tip pixels, so
  scaling the brush scales the hatch, which is correct for a *mark*.
- **Screentone as a fill** — the grid must be locked to the document, not to
  the dab, or the dots swim as the stroke turns and the whole point of a
  screentone is lost. That is not a brush tip at all; it is a pattern fill, and
  it belongs with the fill tool. Recording it here so nobody builds half of it
  as a tip and then discovers the other half.

---

## 3. Scans into tips

The capture discipline in the source spec is good and this document does not
restate it: high-contrast black media on smooth white stock, varied pressure
and angle, 600 DPI flatbed rather than a camera, scan the whole series in one
pass under one setting.

What matters here is **which half of the isolation workflow the app should
own**, because the spec assumes all of it happens in a photo editor.

**In the app**, because these are mechanical, checkable, and the place they go
wrong is silent:

- **Luminance, then levels.** Black point and white point, previewed, with the
  histogram. Same adjustment applied across a whole selected batch in one
  action — the spec's most important rule for lerping is that every capture in
  a series gets *identical* levels, and the way to guarantee that is to make
  applying them separately the harder path.
- **Invert.** Ink-on-paper to shape-on-nothing.
- **Square, centre, resample** to the chosen power of two.
- **Edge masking, enforced rather than requested.** The spec says "ensure the
  edges fade to black". The generator should *guarantee* it — multiply by a
  short border ramp — and it should **refuse, loudly, when significant alpha
  reached the boundary before the ramp**, because that means the crop cut
  through the mark and the fix is to re-crop, not to fade. A tip that stamps
  visible box edges down a stroke is the single most common way a hand-made
  brush is ruined, and it is trivially detectable.
- **Pivot.** Default to the centroid of the alpha mass, which is right for a
  symmetric stamp; let the artist drag it, which is what an angled capture
  needs.

**Not in the app**: dust removal, despeckling, and anything else that is
general photo retouching. Those exist elsewhere and doing them badly here is
worse than not doing them.

---

## 4. Tilt and velocity — the prerequisite nobody costs

`StrokePoint` is `(X, Y, Pressure)`. It is a serialized part of the document.
Adding to it is the largest hidden cost in this whole area, and the
multi-capture feature is impossible without it.

- **Both must be optional, not defaulted.** A mouse has no tilt, and `0` is a
  legal tilt value meaning "perpendicular" — so absent has to be distinguishable
  from zero, or every mouse stroke claims to have been drawn with the pen held
  upright and any tilt-driven tip picks the wrong capture.
- **Old files must load unchanged**, and a document that never saw a tilt-aware
  pen must serialize with no tilt keys at all. Same rule as the camera:
  optional means absent, not present-and-zero.
- **Velocity needs time, which is also not stored.** Note for the fluid-media
  document: it claims "the stroke already carries the timestamps". It does not.
  Either a per-point timestamp joins the record, or velocity is derived from
  point spacing — which is a *resampling artifact*, not a speed, and after
  `Densify` and the smoothing filters it is not even that. Timestamps are the
  honest answer and they are the same widening problem as tilt, so the two
  should land together.
- **Whatever is stored must be what was replayed.** Deriving speed at render
  time from wall-clock would break invariant 2 outright.

---

## 5. Multi-capture sets

A tip becomes a small grid of captures indexed by tilt and by speed, blended at
stamp time:

```
Output = Lerp(A.Alpha, B.Alpha, t)
```

The idea is right and the naive implementation is not affordable. Three things
the source spec does not address:

- **Blend at the size being drawn, not at the source size.** Two 1024² captures
  lerped per dab is a million operations per dab, hundreds of times a stroke.
  Blend at the mipmap level the dab will actually sample.
- **Quantise `t`, and cache the result.** Blend at, say, sixteen steps and key
  the blended tip by `(setId, level, step)`. A stroke drawn at a steady tilt
  then blends once and reuses it, and the quantisation removes the risk of the
  mark shimmering as tilt wobbles by a degree — which is the same anti-boil
  argument as everywhere else.
- **Alignment is the pivot field, not a scanning rule.** Blend with each
  capture offset so its pivot lands on the dab position. Ghosting is what
  happens when this is left to how carefully someone cropped.

**Cost.** A tip set is more memory and real per-dab work, so `BrushCostOf`
should treat a brush carrying one as `Expressive` — the badge exists so that
trade is made knowingly, and this is exactly the kind of thing it is for. That
is a decision to take when the feature lands, not before.

---

## Where this meets the medium work

One place. **B23's remaining half is `BristleDrag` and `Pickup`**, and
`DESIGN-fluid-media.md` already concluded that a directional tip with
`AngleFollowsDirection` delivers `BristleDrag`'s *appearance* far more cheaply
than the advection loop. A chisel tip from the generator, or a scanned
dry-brush skim, is therefore the practical route to half of that bug — and a
scanned bristle is *structured* where `Hash01` is not, which is the honest
answer to "it looks like a texture created from noise".

`Pickup` is not reachable this way. A tip is a stamp that knows nothing about
the canvas, and pickup is a coupling to what is already there. It stays with
the advection loop.

---

## Order, and why

1. **The library and the tip record.** Everything else writes into it, and
   getting `Pivot` and the project/user/document scoping right first is cheaper
   than retrofitting them under two producers.
2. **The procedural generator.** Self-contained, testable against exact
   formulas, and it makes the library non-empty on day one.
3. **The scan pipeline.** Same target, more UI, and the edge-mask check is the
   part that earns its keep.
4. **Tilt and velocity in the record.** Deliberately after the first three,
   because it is a serialization change and should not be entangled with a
   feature that is still moving.
5. **Multi-capture sets.** Last, and only once 4 is real.

## Not decided here

Whether procedural tips are re-generatable in place (edit the recipe, replace
the raster) or always fork to a new tip — the argument above favours forking,
but it has a real cost in library clutter. Nor the on-disk format for a tip set
versus a single tip; nor whether the tips panel is its own dock or a mode of
the brush picker; nor the tilt curve, which will want the same editor the
pressure curves already have.
