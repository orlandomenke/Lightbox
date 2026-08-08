# Tools and strokes


## Tools

Down the left: **Brush** (B), **Eraser** (E), **Fill**, **Picker**,
**Gradient**, **Arrow** (A), **Select** (S). Press Select again to cycle its
variants, or hold it for the list: Freehand, Polygon, Box, Circle, Magic wand.

Hold **Ctrl** at any time to pick a colour off the canvas without changing tool.

### Arrow and Select are not the same tool

They sound alike and they do different things, so it is worth one paragraph.

| | |
| --- | --- |
| **Select** (S) | Picks an **area**. Draw a shape round part of the drawing and what you paint afterwards is confined to it. What you have selected is a region, not any particular mark. |
| **Arrow** (A) | Picks a **thing**. Click a line and you have got that line — the entire stroke you drew in one go, however far it runs. It also picks guides, placed symbols and rig anchors. |

Click to take something; **Shift**-click to add another, and Shift-click one you
already have to put it back. Click empty canvas to let go of everything — though
a Shift-click that misses leaves your selection alone, because you are part-way
through building one. **What you have picked is traced in cyan**, so it is
always clear which line you got.

**Drag from empty canvas** to sweep up everything the box touches — touched, not
enclosed, so a box across a limb takes its lines without your having to draw one
round the whole character. Hold **Shift** as you start the drag to add to what
you already have. The box is cyan and dashed rather than the marching ants of an
area selection, because it is doing a different job.

It picks what you can see. Where two lines cross you get the one on top, and an
eraser stroke never steals a click from ink that is visible underneath it —
though an eraser on its own is selectable, so a stray one can still be removed.
Clicking inside a filled shape picks the fill; clicking in a hole in that shape
does not, because the hole is not part of it. A gradient is picked by the line
you dragged to make it, not by everywhere it reaches.

**Guides and symbols win over lines** where they overlap. The drawing is the
thing that is everywhere, so if it won, a guide crossing a line would be
unclickable wherever you had drawn.

Moving to another layer lets go of what you had picked, because it is not there
any more. Stepping along a **hold** does not — the same drawing is still on
screen.

If the layer is locked it says so rather than doing nothing — but only when you
actually click a line on it, not every time you click past one.

A line drawn entirely outside the canvas cannot be picked yet — filed as B134,
and it matters mainly for the infinite canvas that is still being built.

### What you can do with what you picked

**Move it** by dragging one of the selected lines. The outline follows your
pointer while the button is down and the drawing itself arrives when you let go —
so you can see exactly where it is going without the canvas redrawing the whole
frame on every twitch. A press that does not move is just a click.

**Nudge it** with the arrow keys: one pixel, or ten with Shift. This is the way
to place a line exactly, because a drag cannot reliably land on a single pixel.
The cost is worth knowing: while the Arrow is in your hand *and* something is
selected, ← and → nudge instead of stepping frames. Let go of the selection, or
pick up another tool, and they step frames again.

**Delete it** with Delete, or the button in the tool options. Undo puts the lines
back in the order they were in, so nothing quietly ends up in front of something
it was behind.

**Recolour it** with the button in the tool options — the selected lines take the
current foreground colour. If a line was taking its colour from a **palette
swatch**, recolouring it this way stops it following that swatch: you asked for
this colour rather than that swatch's colour. Undo restores the link.

Everything is one undo step per action, however many lines are selected.

> **A moved line's texture changes, and that is deliberate.** The grain of a
> brush comes from *where the mark is on the canvas* — that is what stops it
> boiling when you animate. Move a line and it is somewhere else, so it grains
> differently. If you need a mark preserved exactly, move the **layer** rather
> than the line.

*Planned:* scaling and rotating what you picked, and a box with handles to do it
with.

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
