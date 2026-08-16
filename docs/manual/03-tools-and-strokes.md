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

**The tool keys work the same way when held.** Tap **E** and you have chosen
the eraser, as ever; *hold* **E**, scrub, and let go, and you never left the
brush. **B**, **F**, **V** and **I** do the same — brush, fill, move and the
eyedropper — so you can reach for one, use it and land back where you were
without thinking about it. The split is how long the key is down: a quick press
is a choice, anything longer is a borrow, which is Photoshop's spring-loaded
rule, so the reflex transfers.

The other tool keys only latch. **S** is taken — pressing it again cycles the
selection variants, so it cannot also mean "hold me". The pen, both arrows and
the Width tool are left out for the reason Ctrl is: they have work in flight,
and letting go of a key is not a good way to finish a path you were in the
middle of placing.

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

**Hovering shows you the line before you take it**, traced faintly in the same
cyan — a dimmer version of what the click is about to give you. Where lines
overlap that is the difference between picking the one you meant and finding out
afterwards. The Width tool previews the same way, for the same reason.

**Drag from empty canvas** to sweep up everything the box touches — touched, not
enclosed, so a box across a limb takes its lines without your having to draw one
round the whole character. Hold **Shift** as you start the drag to add to what
you already have. The box is cyan and dashed rather than the marching ants of an
area selection, because it is doing a different job.

It picks what you can see. Where two lines cross you get the one on top.
Clicking inside a filled shape picks the fill; clicking in a hole in that shape
does not, because the hole is not part of it. A gradient is picked by the line
you dragged to make it, not by everywhere it reaches.

**What you erased is not there to pick.** An eraser leaves a record of itself so
that reloading a drawing rebuilds it exactly, but as far as every tool is
concerned the rub and what it rubbed out have both gone: you cannot click an
eraser stroke, a box dragged over one does not sweep it up, and clicking the gap
an eraser left picks nothing rather than the line that used to run through it. A
line erased along its whole length is out of reach entirely. **Undo is what
brings an erasure back** — it is the only thing that does, so an eraser stroke
you did not mean to make is undone rather than selected and deleted.

Erasing lightly is a different act and is treated as one. An eraser below full
opacity *fades* a line rather than removing it, and a faded line is still a line
you can pick, move and recolour. The same goes for a line rubbed through the
middle: the surviving ends are still yours, and only the gap is out of reach.

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

A line drawn entirely outside the canvas can still be picked, moved and
deleted — where a line is does not depend on where the paper ends.

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

**Scale and rotate what you picked** with **Ctrl+T**, the same transform the
rest of the application uses — the handles appear around the lines rather than
around the whole drawing, and only they move. Enter applies, Esc cancels.

While lines are picked the **scope** is this drawing and says so: a line lives
on one drawing, so "all layers" or "the whole animation" would name cels it is
not on.

**A marquee selection wins if you have one up.** If you have both an area
selected and lines picked, Ctrl+T transforms the area — so if a transform takes
something you did not expect, the usual reason is a marquee still up somewhere
off screen, and **Ctrl+D** is the answer. The transform says what it is moving
when it starts, so you can see which one it took before you drag anything.

Dragging picked lines now moves them **as you drag** rather than on release, and
the whole drag is still one undo step.

### Reshaping a line

**Double-click a line to go inside it.** The line gets a row of points along it
and everything else on the canvas stops responding — click another line while
you are in here and nothing happens, deliberately. **Esc** when you are done —
or just pick another tool: anything that cannot work the points (anything but
the white arrow and the Width tool) leaves the mode on its way past.

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
| **Drag a point** | Moves it, and the line follows as you drag — the shape you see mid-drag is the shape you get on release. |
| **Alt-drag a point** | Pulls a curve out of it: the point stays put and its two arms reach for the pointer, mirrored — the same gesture the pen uses to place a curved point. The quickest way to turn a corner into a curve. |
| **Click a point** | Selects it, and shows its **handles** — the two arms that decide how the line curves through it. Drag an arm to bend the curve. |
| **Square points are corners**, round ones are smooth | On a smooth point the two arms stay in line, so the curve runs through without a kink. On a corner they are independent. |
| **Alt-drag an arm** | Breaks the pair, turning a smooth point into a corner. |
| **Shift-click** | Adds a point to what you have got; dragging then moves them together. |
| **Click empty canvas** | Lets the points go, without leaving the mode. |

Each drag is **one undo step**, however far you pushed the point around.

### Fewer points

**Simplify** — on the quick bar while you are inside a line, beside the count
of points it currently has — refits the line through fewer points and tells you
how many are left: *"Simplified: 31 points to 12."* Press it again and it goes
further. Each press is its own undo step, so one too many costs a single
**Ctrl+Z** rather than the whole line.

It appears with the line you are inside rather than with a tool, because that is
what it acts on — go in with the white arrow or the Width tool and it is there.

It refits **the line as it is now**, not the line you originally drew, so
reshaping first and simplifying afterwards keeps the reshape. It also keeps the
weight: fewer points describing the same line still carry the same taper.

A shape that genuinely needs the points it has says so rather than doing nothing.

### Making a line heavier or lighter

**The Width tool (W)** changes how thick a line is along its length. Hover a
line and it lights up; drag away from it to fatten it there, or back towards it
to thin it. The change is local: it spreads a short way either side of where you
are pointing and leaves the rest of the line as you drew it.

You do not have to open the line first — the drag takes hold of whatever is
under the pointer, one press, the same as the white arrow. If you are already
inside a line reshaping its points, reaching for this works on that line without
leaving.

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

**The white arrow (N)** is the same thing as a tool rather than a gesture.
**Hover a line with it and you see its points** before you commit to anything —
which is how you tell a line with three points from one with thirty without
going inside it first. **One click** goes in and takes hold of whatever was
under the pointer, so you can start dragging a point immediately.

One click here, two with the Arrow, and the difference is deliberate: a click
with the Arrow ordinarily means *pick this whole thing*, so reaching into
geometry has to be asked for twice. Reaching into geometry is the only thing
the white arrow does, so there is nothing to ask twice about.

The two tools still do different jobs — the Arrow picks whole lines, the white
arrow picks the points inside one — and blurring them is how a click starts
meaning two things.

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
| **Click the first point** | Closes the line, and finishes it. A ring appears around the first point when you are near enough, and the preview snaps shut — closing is announced before it happens, never a surprise. |
| **Backspace** | Takes the last point back off. |
| **Enter or Esc** | Finishes the line. |

**Neither Enter nor Esc throws the line away.** Both mean *done* — a path you
have spent a minute placing is artwork, not a gesture in progress. If you did
not want it, **Ctrl+Z**: the whole line is one undo step however many points
went into it.

**Reaching for another tool does not finish the line either.** The path in
progress stays on screen, parked, and the pen picks it up exactly where you
left it — so grabbing the eyedropper for a colour mid-path costs nothing.
Enter, Esc or closing the loop are how a path becomes a line.

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

**An erase that rubbed nothing out is not recorded.** Sweep the eraser across
blank canvas, or press Delete with an empty selection, and nothing is written:
no stroke on the drawing, and no step in the history. It did nothing to nothing,
so there is nothing to keep. Erasing down the gap between two lines counts as
nothing too — what matters is whether any pixel changed, not whether ink was
nearby.

The one consequence worth knowing: **Ctrl+Z straight after an erase that hit
nothing takes back whatever you did before it**, because as far as the drawing is
concerned that erase never happened. An erase that *did* rub something out is
recorded and undone exactly as it always was.

This also keeps the timeline honest. Erasing on a **hold** normally starts a new
drawing on that frame — but if the erase turns out to have hit nothing, the hold
stays a hold. A gesture that changed no pixels never changes your timing.

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

### The bucket shows its region before you click

Hover with the **Fill** tool and the region a click would flood is tinted in
the colour you are holding, its outline drawn solid. It is traced by the same
code the click runs — same tolerance, same gap setting, same smart-fill
sampling — so the click gives you exactly the tinted region, never a surprise.
Moving inside a region costs nothing (the answer cannot change until you cross
its edge); the trace runs in the background, so a fast hand never waits on it.
The magic wand previews the same way, as a faint, still dashed outline —
dashes mean selection, and it stays faint and still so it cannot be mistaken
for one you have already made.

## Drawing fast

A pen reports its position at a fixed rate, so the faster you draw, the further
apart the points it records. Lightbox lays the brush along the **curve** through
those points rather than along the straight lines between them, which is why a
quick arc drawn with a fat brush comes out as an arc instead of a row of flat
facets with the tops of the stamps showing on the outside of the bend.

Corners you meant are kept: turn sharply enough and the stroke stays sharp
there, so a drawn rectangle has square corners and a flick still has a point.

## The pointer tells you what the tool will do

The pointer changes with the tool, so you can tell what is armed without looking
away from the drawing. The brush and the eraser show their real size and shape as
a ring; the eyedropper and the fill show a crosshair with their own icon beside
it — the crosshair is where the tool acts, the icon says which one you are
holding; the pen, the shapes and the selections show a plain crosshair; the move
tool shows arrows; the two selection arrows show a pointer.

**If the pointer shows a "no" symbol, the tool will not do anything where you
are** — that is the point of it, and the reason why appears in the strip at the
bottom of the window. It means one of:

- the layer is **hidden**. Turn its eye back on in the Layers panel.
- the layer is **locked**. Unlock it with the padlock.
- you are **outside the selection**. Strokes only land inside it.
- the layer is **alpha locked** and there is no paint under the brush. Alpha lock
  means "only draw where I have already drawn", so bare canvas is off limits —
  move over existing paint and the pointer allows it again.

The last two change as you move, because they are about *where you are* rather
than about the layer. Before, all four did nothing and said nothing, which is
hard to tell apart from the application being broken.

**Hold `Ctrl` and the brush becomes an eyedropper** for as long as you hold it —
the colour you want is usually already on the canvas, and fetching a tool to get
it breaks the stroke you were about to make. The pointer changes to say so. It
also stops refusing while you hold it, because picking a colour off a locked
layer was always allowed.

### The eyedropper's ring

With the eyedropper in hand — chosen, or borrowed with `Ctrl` — a ring follows
the pointer, split in two:

- the **top half** is the colour you would take if you clicked now;
- the **bottom half** is the colour you already have;
- the **middle is a hole**, so the pixel you are aiming at is never covered.

It is there so picking a colour is a comparison rather than a guess: whether a
shadow off the drawing is actually different from the one loaded is the question
you are asking, and the answer used to be in a panel on the other side of the
window. Click and both halves become the same colour, which is the ring saying
the pick landed.

The ring goes away when there is nothing to pick — off the paper, mainly. Over
bare canvas it shows the paper colour, because that is what a click there takes.

### The pointer also tells you what you are about to grab

Nothing floats over your drawing in Lightbox — there is no row of buttons on the
canvas and no handles except the ones a gizmo puts there — so the pointer is what
says a thing under it can be taken hold of, and which way it will go.

- **A double-headed arrow means this drags a size.** It lies along the direction
  the handle actually travels: across a corner of the transform box, straight up
  or straight across from an edge, up and down on the top rung of a height chart.
  Turn the canvas and the arrows turn with it, because they describe the movement
  your hand makes rather than the one the document would see.
- **A four-way arrow means this moves.** Inside the transform box, on a guide, on
  the transform box's pivot, on a reference sheet's box, and on a corner of a
  perspective warp — a warped corner goes wherever you put it, so there is no
  direction to point along.
- **A curved arrow means this turns.** Outside the transform box, on the camera
  frame's rotate handle, and on a bone you are posing.
- **A hand means you are holding the canvas itself** — a middle-button drag, or
  the pan tool.

Two things this promises. It **keeps the shape for the whole drag**, so a scale
that started at a corner does not turn into a move cursor when your hand crosses
the middle of the box; and it **changes the moment you hold a key**, without
waiting for you to move, so a modifier that borrows another tool says so straight
away.

The pointer is also honest about what it cannot offer: a guide only shows a grab
when a guide *can* be grabbed — with a brush in hand, or with the rig locked or
hidden, it is scenery and the pointer stays the brush's.

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
