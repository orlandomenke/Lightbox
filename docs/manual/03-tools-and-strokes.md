# Tools and strokes


## Tools

Down the left: **Brush** (B), **Eraser** (E), **Fill**, **Picker**,
**Gradient**, **Arrow** (A), **Points** (N), **Pen** (P), **Width** (W), **Shape** (U), **Select** (S). Press Select again to cycle its
variants, or hold it for the list: Freehand, Polygon, Box, Circle, Magic wand.

**The rail arranges itself, and takes only the width it is using.** One centred
column when the window is tall enough to hold every tool in one, two when it is
not — and the rail is exactly as wide as the columns it chose, rather than a
fixed width with space around it.

Drag its edge to overrule that. A width you set is yours and stays put when the
window changes shape, and it is also the way to the two layouts the rail will
not choose for itself: **three columns**, worth the canvas it costs only when
you have asked for it, and past ~150&nbsp;px a **single labelled list**.

**Hold Ctrl to borrow the eyedropper.** The tool actually changes while you
hold it — the rail highlights, the cursor changes — and letting go puts your
brush back exactly where it was. The colour you want is nearly always already on
the canvas, and going to fetch it is what breaks the stroke you were about to
make.

It works from the Brush, Eraser, Fill, Shape and Gradient. It does **not** work
from the Pen or either arrow, and that is deliberate: those have work in flight
— a path you are still placing, a line you are inside — and borrowing a tool
must never be the thing that finishes it. Move keeps Ctrl for its own "drag the
whole layer".

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

**Picking up another tool lets go too.** Selected lines are the Arrow's — no
other tool moves, recolours or deletes them — so the highlight goes when the
Arrow does, rather than sitting on the canvas pointing at something you can no
longer do. Clicking empty canvas, or picking a guide or a symbol instead, lets
go for the same reason.

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

### Reshaping a line

**Double-click a line to go inside it.** The line gets a row of points along it
and everything else on the canvas stops responding — click another line while
you are in here and nothing happens, deliberately. **Esc** when you are done.

Why a mode rather than a held key: reaching into a drawing and moving its
geometry is not something that should ever happen by accident, and a modifier is
something you have to remember *not* to be holding.

**The points come from the line you drew.** You do not have to have planned for
this — any line, drawn at any time, gets its points worked out when you first
double-click it. A long stroke usually comes back as a handful of points rather
than the hundreds you actually drew, because a point every wobble is not
something a hand can work with.

| | |
| --- | --- |
| **Drag the line itself**, between two points | Pulls the curve to where you put it. The points either side do not move — this bends the line rather than shifting it, and it is usually the one you want. |
| **Drag a point** | Moves it, and the line follows. |
| **Click a point** | Selects it, and shows its **handles** — the two arms that decide how the line curves through it. Drag an arm to bend the curve. |
| **Square points are corners**, round ones are smooth | On a smooth point the two arms stay in line, so the curve runs through without a kink. On a corner they are independent. |
| **Alt-drag an arm** | Breaks the pair, turning a smooth point into a corner. |
| **Shift-click** | Adds a point to what you have got; dragging then moves them together. |
| **Click empty canvas** | Lets the points go, without leaving the mode. |

Each drag is **one undo step**, however far you pushed the point around.

### Fewer points

**Simplify** — in the Arrow's options while you are inside a line — refits the
line through fewer points and tells you how many are left: *"Simplified: 31
points to 12."* Press it again and it goes further. Each press is its own undo
step, so one too many costs a single **Ctrl+Z** rather than the whole line.

It refits **the line as it is now**, not the line you originally drew, so
reshaping first and simplifying afterwards keeps the reshape. It also keeps the
weight: fewer points describing the same line still carry the same taper.

A shape that genuinely needs the points it has says so rather than doing nothing.

### Making a line heavier or lighter

**The Width tool (W)** changes how thick a line is along its length. Go into a
line the usual way — double-click it with the Arrow — then drag away from the
line to fatten it there, or back towards it to thin it. The change is local: it
spreads a short way either side of where you are pointing and leaves the rest of
the line as you drew it.

It is the same number your pen pressure writes, so a line you drew with a taper
and a line you widened by hand are the same kind of thing afterwards. A line
drawn with the **pen**, which had no pressure at all, starts out even and can be
given a taper this way.

The whole drag is **one undo step**, and undoing it puts every original pressure
back exactly.

**Pulling the line is worth trying before anything else.** Most of the time what
you want is *this bit of the line should be over there*, and reaching for that
directly is quicker than working out which point governs it and which way its
arms need to go. It works on a straight run too — drag the middle of a straight
line and it bends, which is how you turn a corner-to-corner segment into a curve
without adding anything.

**The white arrow (N)** is the same thing as a tool rather than a gesture — for
when you are already inside a line and want to keep reshaping. It does nothing
until a line is isolated, which is on purpose: it and the Arrow do different
jobs and blurring them is how a click starts meaning two things.

> **Reshaping keeps the weight you drew with.** A line that tapers at the ends
> still tapers after you have moved its points about — the pressure spreads
> along the new shape rather than being flattened out. What it does *not* keep
> is the **grain**, for the same reason moving a line does not: the texture
> comes from where the mark is on the canvas.

### Drawing a line with the pen

**The pen (P) draws a line by placing its points instead of by hand.** Click to
put a corner down; click *and drag* to put a curved point down, pulling its
handles out as you go. The line follows the pointer as you move, so you can see
the curve the next click is going to make before you commit to it.

| | |
| --- | --- |
| **Click** | A corner point. |
| **Click and drag** | A smooth point — the drag pulls its handles out. |
| **Alt while dragging** | Only the outgoing handle, leaving the point a corner: a curve that arrives straight and leaves bent. |
| **Shift** | Constrains to 45°, so horizontals, verticals and diagonals are exact. |
| **Click the first point** | Closes the line, and finishes it. |
| **Backspace** | Takes the last point back off. |
| **Enter or Esc** | Finishes the line. |

**Neither Enter nor Esc throws the line away.** Both mean *done*, and so does
reaching for another tool — a path you have spent a minute placing is artwork,
not a gesture in progress. If you did not want it, **Ctrl+Z**: the whole line is
one undo step however many points went into it.

**What the pen makes is an ordinary line.** Not a shape and not a separate kind
of object — the same thing the brush makes, with the same brush settings, which
means it erases, fills against, exports and inbetweens exactly like a drawn one.
The white arrow opens the points *you* placed rather than working them out
afterwards.

A pen line has no pressure variation, because nothing was pressing. That is what
it is for — a clean, even line. If you want weight in it, reshape it afterwards
or draw it by hand.

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
