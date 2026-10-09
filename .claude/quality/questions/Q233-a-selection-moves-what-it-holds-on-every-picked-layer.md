# Q233 · A selection moves what it holds on every picked layer: partial marks, the wand, pixel layers — **answered 2026-10-09**

**Answered:** a mark only partly inside a selection is **cut at the edge** (as
recommended); the magic wand stays **strict** (against the recommendation); and
the owner usually selects with **a lasso or a marquee around the thing**, but
wants every way of selecting to work "as expected where applicable".

Raised by the owner, 2026-10-09: *"When I have an active selection on screen and
I switch to another layer, Ctrl+T for transform or move does not work. So it
only samples the layer it became active on."* The rule they want: a selection
applies to whichever layers are picked — the line layer alone, line and shading,
or line, shading and colour — and a picked layer with nothing inside the
selection simply contributes nothing.

What it blocks: B432 (a selection finds a stroke by its path, not its ink) and
the fix for pixel layers — an imported image, a PSD layer or a baked merge —
which a selection could not move at all.

## What was measured before asking

The selection was never tied to a layer: a marquee made on one layer moves
another layer's strokes after a switch, through the same `BeginTransform` that
Ctrl+T and a Move drag both use. Two other things fail:

- **Strokes are found by their path.** A fill's path is its outline, a line's is
  its centre, so a selection made from one layer's pixels (alpha, or a wand)
  overlaps other layers' ink without containing their paths. Measured: a
  selection of the colour layer's pixels, then Ctrl+T on the line layer —
  *"Nothing to transform in this scope."*
- **Pixels are not moved by a selection at all.** The transform commit resamples
  a frame's baseline only when there is no selection; with one, "baseline pixels
  stay put". Measured: a marquee over an imported-pixels layer, and over a layer
  produced by a baked merge, both refuse. This is the likeliest reading of the
  lasso case in the report.

## The decisions

1. **A mark partly inside the selection — cut at the edge.** Only the ink inside
   moves, as Photoshop does and as a marquee already does to strokes here
   (Q166). Alternative: move any mark whose ink reaches in, whole — lines stay
   unbroken, but the selection would stop meaning its pixels. The same rule
   applies to pixel layers: the pixels inside move, the rest stay.
2. **The magic wand — strict.** The recommendation was to let a wand region grow
   to take in marks drawn on top of it or along its edge on the picked layers,
   which is what the foot example needs from a wand clicked on the colour. The
   owner chose to keep it strict: the wand selects exactly the pixels it
   matched, so lines and shading that differ in colour are outside it. The cost,
   stated so it is not rediscovered as a bug: a wand clicked on the colour with
   *sample all layers* on does not move the line or the shading; a lasso around
   the foot, or the wand sampling only the colour layer, does.
3. **How the owner selects — usually a lasso or marquee around the thing.** Not
   a preference to record so much as the test fixture: the shapes that must
   work first are drawn ones.
