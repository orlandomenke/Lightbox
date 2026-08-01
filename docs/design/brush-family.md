# The smudge/blur family, and aligning brush settings with Photoshop

Status: design. Nothing here is implemented yet except where noted.

## 1. Four brushes, two behaviours

| Brush | Reads | Writes | Deposits colour |
| --- | --- | --- | --- |
| Smudge | active layer | active layer | no |
| Blur | active layer | active layer | no |
| Smudge (all layers) | composite of visible layers | active layer | no |
| Blur (all layers) | composite of visible layers | active layer | no |

Reading is fixed per brush, not a setting — picking the tool picks the
behaviour, as in Photoshop where Sample All Layers is a per-tool option that
presets carry. Each of the four gets its own preset and its own stored
settings, so tuning the sampled smudge never disturbs the isolated one.

### What already works

The isolated pair is correct today. `BrushEngine.StampSmudge` and
`StampBlur` never read `stroke.Color`, so they are colourless by
construction, and the committed render samples the active layer's own
pixels. A live-preview bug where dragging with Smudge stamped flat
foreground-coloured dabs for the whole stroke — only snapping to the real
smear on pen-up — is fixed (`MainViewModel.BeginStroke` now gives smudge the
same real-pixel copy blur already had).

### What the sampled pair needs, and the decision it hinges on

`FrameRasterizer.Materialize` renders **one layer in isolation**: baseline
PNG, then that layer's strokes, with `targetPixels` being that layer's own
bitmap. Layers are composited afterwards, in `SceneRenderer.ComposeInto`.
Nothing at rasterization time can see another layer.

So a sampled brush needs a *backdrop* — the composite of the other visible
layers — threaded from the caller into `BrushEngine`. Mechanically that is
an extra optional parameter on `StampStroke`, `Materialize`, `Append` and
`AppendDraft`, plus a backdrop argument on `FrameBitmapCache.Get`. The
mechanical part is easy. The consequence is not:

**A layer's cached bitmap stops being a function of that layer alone.** Edit
a lower layer, and every sampled stroke above it must re-render. The cache
key has to include the backdrop's identity, and invalidation has to cascade
upward through the stack.

That is a genuine design fork, and it is Q6 below. It is the only thing
blocking the sampled pair; everything else is plumbing.

## 2. Photoshop alignment — the selection

Our `BrushSettings` currently carries: size, hardness, opacity, flow,
spacing, scatter, granulation, wet edge, tip id, angle, roundness,
smoothing, anti-aliasing, and the pressure curves. Photoshop's brush panel
is far larger, and the `.abr` importer necessarily drops what we cannot
represent.

Rather than chase the whole panel, here is the selection worth having — the
ones that change how a mark *reads*, that `.abr` and `.kpp` files actually
carry, and that our dab-stamping engine can honour without a rewrite.

### Tier 1 — take these first

| Photoshop name | What it does | Why it earns its place |
| --- | --- | --- |
| **Size Jitter** + Minimum Diameter | dab size varies per dab | The single biggest difference between a "digital" and a "natural" mark. We have scatter but not size variation. |
| **Angle Jitter** / **Roundness Jitter** | dab orientation and squash vary | Makes a custom tip read as bristles rather than a repeated stamp. We store angle and roundness but never vary them. |
| **Shape Dynamics → Direction** | angle follows the stroke direction | Required for any flat/chisel tip to behave like one. Cheap: we already compute segment direction for spacing. |
| **Dual Brush** | a second tip masks the first | This is what makes Photoshop's textured brushes look textured; it is also the single most common thing lost on `.abr` import. |
| **Transfer → Flow Jitter** | flow varies per dab | Gives the wash/build-up variation that currently only pressure can produce. |

All five are per-dab modulations of values we already have, and all five can
be seeded from dab position through `Hash01`, so they cost nothing in
determinism (invariant 2) and nothing in re-render divergence.

### Tier 2 — worth having, more work

- **Texture** (paper grain multiplied into the dab, with depth and scale) —
  we have granulation, which is a fixed-frequency version of this; making
  the tile and depth settable is most of the way there.
- **Colour Dynamics** (foreground/background jitter, hue/sat/brightness
  jitter) — natural-media colour variation. Needs a second colour in the
  record.
- **Noise** and **Wet Edges** as independent toggles — we have wet edge.
- **Brush Pose** (tilt, rotation from the stylus) — we read tilt from the
  pen already but do nothing with it.

### Tier 3 — deliberately not doing

- Airbrush/build-up timing, bristle qualities, and the full Mixer Brush
  reservoir model. Each is a simulation rather than a parameter, and none
  survives an `.abr` round trip in a form we could honour faithfully.

### Where these go in the UI

The brush flyout already has General / Effects / Pressure / Presets pages.
Photoshop groups by *dynamic*, not by *parameter*, and that grouping is what
makes the panel legible: Shape Dynamics, Scattering, Texture, Dual Brush,
Transfer. Adopting those names for the Effects page's sections would make
imported presets recognisable to anyone coming from Photoshop, which is the
point of aligning.

## 3. Questions this raises

Added to `.claude/quality/QUESTIONS.md` as Q6–Q8.
