# Tools and strokes


## Tools

Down the left: **Brush** (B), **Eraser** (E), **Fill**, **Picker**,
**Gradient**, **Select** (S). Press Select again to cycle its variants, or hold
it for the list: Freehand, Polygon, Box, Circle, Magic wand.

Hold **Ctrl** at any time to pick a colour off the canvas without changing tool.

## What a stroke is

**A frame is a list of strokes; the pixels are derived.** Nothing paints except
through the stroke record, so a reload renders exactly the same image, undo is
exact, and the inbetweener has real geometry to work with rather than pixels to
guess at.

The same is true of fills (a fill is a stroke with contours) and selections (a
selection is an entry in the document, referenced by the strokes clipped to it).

Settings that reach pixels — anti-aliasing, pressure curves — are recorded **on
each stroke**, so changing a preference never alters art you have already made.

## What you see while drawing is what you get

**The mark under the pen is the mark you will have when you let go.** Not close
to it — the same pixels. That covers everything: how dense the dabs are, how far
the paint load has run down, where a scattered brush threw each stamp, how the
tip texture sits.

It is worth stating because it has not always been true, and because the way it
used to fail was hard to name. The preview built the stroke a piece at a time and
each piece started the brush over, so an airbrush came out darker while you drew
than after, an oil brush looked fully loaded until you released and then faded to
nothing, and a textured tip rearranged itself on pen lift. If you see anything
like that again, it is a bug and worth reporting — it is not the medium being
unpredictable.

**That now includes smudge, blender and blur.** They were the last to hold out:
because they rework pixels already on the layer rather than adding new ones, each
piece of the preview restarted not only the brush but the colour it was dragging,
so a smear reached further after you let go than it had while you drew. Smudge and
blender are exact now — the same pixels, live and committed. Blur is exact in the
area it touches and can differ by a shade at the very rim of the mark, which is a
deliberate trade: making it exact means re-blurring the whole stroke on every
movement, and that is slower than you can draw.

The one thing you may notice: on a fast flick the very tip of the mark settles a
fraction behind the cursor. That is the last stroke of the brush waiting until it
knows which way you turned, and it is deliberate — the alternative is stamps that
visibly jump into place.

## Drawing fast

A pen reports its position at a fixed rate, so the faster you draw, the further
apart the points it records. Lightbox lays the brush along the **curve** through
those points rather than along the straight lines between them, which is why a
quick arc drawn with a fat brush comes out as an arc instead of a row of flat
facets with the tops of the stamps showing on the outside of the bend.

Corners you meant are kept: turn sharply enough and the stroke stays sharp
there, so a drawn rectangle has square corners and a flick still has a point.

---

## Shapes

The **Shape** tool draws a line, rectangle, ellipse or polygon: pick the shape
in the tool options and drag. **Shift** squares it — a circle, a square, a
regular polygon — and **Alt** grows it from the point you started rather than
towards it.

A shape is an ordinary stroke, drawn with whatever brush you have loaded. A
watercolour rectangle is watercolour; it erases, re-renders and inbetweens like
every other mark, and it snaps to guides like every other mark. The trade is
that it is not re-editable as a shape afterwards — that is the right bargain for
a tool where the unit of work is two hundred drawings, not one.

---
