# Q160 · How do the Photoshop filters reach a layer's own stack? — **answered 2026-08-24: by being native, which is what picks the set**

Asked when the catalogue turned to the classic Photoshop filter menu. The
seam already shipped decides more here than taste does: **a per-pixel CPU
pass is backdrop-only** (Q159), because a layer's own effects apply inside a
Skia `SaveLayer` where there is nothing to read back. Hue/Saturation and film
grain live with that and steer to adjustment layers; a filter menu built the
same way would put *every* filter one step away from the layer an artist has
selected.

So the set was chosen by which filters are **native**:

| | What it costs |
| --- | --- |
| **Convolution + tone** (recommended, **chosen**) | Two Skia primitives — a matrix convolution and a colour lookup table — buy sharpen, emboss, find edges, threshold, posterize, invert and gradient map. Every one works on a layer, an adjustment layer and the scene alike, at native speed. |
| The blur family (motion, radial, zoom) | Fewer filters per unit of machinery, and the expensive ones want the effect output cache that is still open on the roadmap. |
| The distorts (twirl, ripple, spherize) | The most dramatic set and the one the seam serves worst: each is a per-pixel displacement, so all of them would be backdrop-only until the CPU-pass limitation is lifted. |
| Curves first | The most-reached-for adjustment there is, but it needs a curve-editing widget — the same one the timeline's keying UI needs, and worth building deliberately rather than as a side effect. |

**Lifting the backdrop-only limitation is its own branch**, deliberately not
this one: the self path is currently a pure Skia filter inside a save-layer,
and letting a CPU pass run there means reading the layer's own pixels back —
an architecture change, and mixing it into a catalogue branch is two
objectives in one.

## Gradient map, and the reach the registry does not have

A gradient map wants a *gradient*, and the document has them —
`Doc.Gradients`, keyed by id. The registry cannot see them: a definition is
handed a use, a frame and a scale, and nothing else. That is not an oversight
to route around but the property that makes an effect a pure function of the
record (invariant 2's precondition), and threading a document into the
registry to look up an id would trade it away for one effect.

So v1's gradient map is **two authored colours** — shadow and highlight, in
the `Colors` map Q153 added — with a *midpoint* parameter to bias the ramp.
That is a real gradient map and needs no new plumbing. Mapping tone through a
*document* gradient is a later branch, and the honest way to do it is to
resolve the gradient into the use's own colours when it is chosen, so the
record still says everything the render needs.

It is native despite mapping tone to colour, which is worth writing down
because it looks like a per-pixel job: a colour matrix flattens the pixel to
its luminance, and three per-channel tables then map that luminance to the
ramp's red, green and blue. `SKColorFilter.CreateCompose` of the two is the
whole effect.

## What "native" turned out to mean, which was not "a convolution"

The obvious build was one primitive — Skia's 3×3 matrix convolution — behind
sharpen, emboss and find edges, since all three *are* kernels. It measured
**~1270 ms per 960×540 compose, twenty times an 8 px Gaussian blur**: Skia's
CPU convolution has no fast path where blur, offset and arithmetic blend all
do. The budget test caught it before the shape was written down anywhere,
which is the second time that test has paid for itself.

Rebuilt from the primitives Skia is quick at, the same two filters measure
**173 ms**, and read better besides:

- **Sharpen is an unsharp mask** — `(1 + amount) × source − amount × blur` —
  so it gains a radius, which is the control the Photoshop dialog actually
  offers and the fixed kernel could not express.
- **Find edges is the difference between the picture and a blur of it**,
  inverted, so flats come out white and lines dark.

Both carry a **radius floor of 2**, and that is a finding rather than a
taste: Skia's raster blur is a no-op below sigma 1, so a radius under 2
subtracts the picture from itself and hands back exactly what it was given. A
slider whose lower half does nothing is a slider that lies.

## Emboss is deferred, and this is why

It is the one of the three that resists the reformulation. Relief means
"signed difference, centred on mid grey", and the arithmetic filter's
constant `k4` is added to **every channel including alpha** — so mid grey
arrives as a half-transparent white, and forcing alpha back to solid
afterwards leaves it white, because the colour was already divided by the
alpha it was given. Getting it right needs a five-to-seven node graph (two
one-sided differences, a grey base, two more blends) referencing the input
four times, which is exactly the shape that made the first layer-style draft
exponential.

So emboss is **not shipped rather than shipped badly**, and it is on the
roadmap with this reason attached. It becomes a five-line CPU pass the day
the backdrop-only limitation is lifted, which is a branch of its own already.
