# Layers, selections and guides

## Layers

Folders, blend modes, per-layer opacity, visibility, lock and alpha lock.
Thumbnails show what is actually on the layer.

**There is one kind of layer, and it holds everything.** Strokes, imported
pixels, symbol placements — a layer is not committed to one of them. Lightbox
used to ask you to pick Raster or Vector when you added a layer, and marked each
row **R** or **V**; the choice never changed what you could draw, because both
kinds took the same tools and made the same marks. All it did was decide what a
layer would later *refuse*. Documents saved before this still remember which they
were, so nothing about your files changed — the question is simply gone.

A new document opens with a locked **Background** layer holding the paper, and a
paintable layer above it. On a transparent document there is no paper layer —
just an ordinary unlocked layer.

**Ctrl+click** a layer thumbnail to select its opaque pixels.

**The everyday layer verbs are on the Layer menu too**, addressed to the layer
you are on: new layer, new folder, move up and down, merge down, the three
checkboxes (visible, locked, lock transparency), select contents, blank
content and delete. The docker keeps the things a menu is wrong for — the
blend mode and opacity controls, drag-reordering, and the right-click menu's
row-targeted extras (linking, exports, the rig).

### Working on several layers at once

Click a row to make that layer active. **Ctrl+click** another row to add it to
the selection — or Ctrl+click a selected one to drop it. **Shift+click** takes
every row between the last one you picked and this one; Shift+click again
somewhere else re-ranges from the same starting row rather than adding a second
run, so overshooting is corrected with one more click.

The selected rows are tinted, and one of them — the last you clicked — is
tinted more strongly. That one is the **active** layer, and it is where the next
brush stroke lands. There is always exactly one, which is why Ctrl+clicking the
only selected row does not deselect it: the app would have nowhere to paint.

Anything you then do to a row in the selection is done to all of them, as **one
undo step**:

- delete, and blank the content
- move up and move down — the block keeps its order and stops as one against
  the top or bottom of the stack
- the eye, the lock and the alpha lock
- **In exports** — never, always, or leave it to the export
- **New folder from layer**, which puts the whole selection in one folder

Anything you do to a row that is *not* in the selection is done to that row
alone. Right-clicking a layer you have not selected is not a trap.

### Reordering by dragging

Rows also **drag**: pick a layer up and drop it where it should go. Dropping on
the upper half of a row lands above that row, the lower half lands below it.
The layer takes the folder of wherever it lands — dropping between a folder's
members joins the folder, dropping beside a loose row leaves it — and the whole
drop is one undo step. A drag moves the one row you picked up; the ▲/▼ buttons
remain the way to move a multi-selection as a block.

**A folder header drags too, and takes the whole folder with it.** The block
keeps its own order and lands above or below wherever you drop it. Folders do
not go inside other folders, so a folder in your hand always lands *beside* what
is under the pointer, never in it.

**A folder header is three targets, not two.** Its middle files a layer into the
folder — the common case, so it is the part you get by aiming at the row. The
top and bottom quarters put the layer above or below the whole folder instead,
which is how you get a layer to sit just outside a folder without hunting for
whatever row happens to be next to it.

**While you drag, two things tell you what will happen**: a small label under
the pointer naming what you picked up, and a line on the row you are over
showing which side it will land on. A folder about to swallow what you are
carrying lights up instead of drawing a line, because "inside" is not an edge.
If neither appears, the drop will not do anything — dragging a folder onto one
of its own rows, for instance.

**The onion-skin toggle (◉) is the exception: it is always one layer**, even
with several selected. The eye and the locks describe the drawing, and picking
five layers means all five. Onion skin describes what you are *looking through*
while you work on one of them, and which layers are in the stack is something
you tune as you go — the background off, the layer under your hand on. Sweeping
it across a selection would clear that arrangement and give nothing back.

The keyboard and the shortcut bar say "active layer" and mean it: **Toggle
lock**, **Toggle alpha lock** and the bar's ◉ act on the active layer alone,
never on the selection. The docker's own rows are the selection-aware ones.

Clicking a cel in the timeline, walking the stack with the arrow keys, or
opening a document all set the active layer the ordinary way, and that starts
the selection again from the one layer.

### Linking layers — one drawing across several

A character is rarely one layer. Lines, colour, details, effects: four layers
that are really *one drawing*. **Linking** says so once, and what the link
carries then reaches all of them — on every frame, including the ones you have
not drawn yet.

- **Ctrl+Shift+right-click** links a layer to the one **above** it.
- **Ctrl+Alt+right-click** links it to the one **below**.
- **Shift+right-click** takes it back out.
- Right-click and open **Linking** for the same options by name, plus the
  switches below.

Linking to a layer that is already in a link **joins that link** rather than
starting a second one, so a four-layer character is three gestures and ends
with one link.

Linked rows are indented and carry a bracket down their left edge, so a set
reads as one at a glance.

**What travels a link is up to you, per property.** A link can carry several
things, one, or nothing at all:

- **Share the rig** — every layer follows the bone any one of them names. On
  by default when you make a link, because rigging is usually why you made it.
- **Share alpha lock**
- **Share visibility** — hide the lines and the colour goes with them.

Nothing else travels. A link made to rig a character does not quietly start
sharing anything you did not ask it to.

The gestures read the neighbour **once**, at the moment you make them. After
that the layers know each other by name, so reordering the stack afterwards
never breaks a link or attaches it to something else.

### Making a layer follow the rig

Right-click a layer and open **Follows the rig**:

- **The selected bone** — every stroke on the layer follows the bone picked in
  the Bone tool.
- **The whole skeleton** — bound to the nearest bones by distance, the way
  auto-bind does it.
- **Nothing** — off the rig.

This is per *layer*, so it covers drawings you have not made yet and frames
you have not reached. Scrub the timeline and every drawing on the layer follows
the pose, in playback and in every export. Painted weights on an individual
line still win over it — that is what the weight brush is for.

Your lines are never changed by this. A rigged layer decides where marks are
*drawn*; the drawing stays exactly as you made it, so unrigging the layer
returns it untouched. **Baking** a drawing freezes the pose into it and leaves
the layer rigged, so the drawings you make afterwards still follow.

A layer's own choice beats its link's, so you can rig the effects layer to a
different bone from the lines it is linked to.

### Merging a layer down

**Ctrl+E** merges the active layer into the one below it — Photoshop's and
Krita's key. The row's right-click menu has it too (**Merge down**), for
merging a layer that is not the active one. The merged layer keeps the lower
layer's name, opacity, blend mode and folder, and the merge is one undo step.

It walks the exposure sheet drawing by drawing: where both layers hold, the
merged layer holds, so animating on 2s survives a merge. Where either layer
keys a new drawing, the merged layer gets one combined drawing.

**Your strokes survive wherever the merge can be exact without pixels.** Two
plain drawings merge by keeping both stroke records, still editable and still
readable by the AI inbetweener. What cannot stay strokes is flattened to
pixels instead, so the merge always looks exactly like the two layers did:

- a blend mode or opacity on the layer being merged — it is applied into the
  pixels, the way merging in Photoshop applies it;
- erasers or clear-regions on it — kept as strokes they would start erasing
  the lower layer's marks, which they never touched;
- alpha-locked or smudge strokes, and imported pixels — all of them read what
  is underneath, and "underneath" changes when the layers become one.

The decision is per drawing, not per layer: one blended drawing does not cost
the other eleven their strokes. When AI assistance is on and a merge would
flatten any drawing, Lightbox asks first, because a flattened drawing is one
the inbetweener can no longer read. As in Photoshop, a blend mode is applied
against the layer directly below — if it was interacting with layers deeper
in the stack, that part of its look can shift.

Merging a masked or clipped layer applies the mask and the clip into the
merged pixels, so the result looks exactly as the pair did; a mask on the
lower layer is applied and removed by the merge, since it is now in the
drawing itself.

### Masking a layer

A layer mask hides part of a layer without touching its drawing. In Lightbox
a mask is not a special grayscale image — it is an ordinary drawing, painted
with your own brushes: **where the mask has paint, the layer shows; where it
has none, the layer is hidden.** Erasing on the mask hides again. There is no
white-reveals, black-conceals convention to remember.

Right-click a layer and open **Mask**:

- **Add — painting hides.** The layer stays fully visible and what you paint
  on the mask conceals it. This is the vignette workflow: soften an edge,
  knock a hole in a background.
- **Add — painting shows.** The layer starts fully hidden and your marks
  reveal it — for building a reveal by hand.

The two are the same mask one **Invert** apart, and inverting is a flag, not
a repaint. Adding a mask drops you straight into painting it: a small **chip**
appears beside the layer's thumbnail, outlined while your strokes are landing
on the mask. Click the chip (or use **Mask → Paint the mask**) to switch
between painting the mask and painting the drawing — while you paint a mask,
the canvas shows exactly what your mark reveals or hides, live, and every
mask stroke is a normal undo step. **Disable** switches the mask off without
losing it (the chip goes hollow); **Delete** removes it entirely.

The mask is one drawing that holds across the whole timeline — it does not
animate per frame. For an animated matte, use a clipping mask instead: the
matte is an ordinary layer, and ordinary layers animate.

Blur and smudge do not work on a mask, because a mask is coverage rather than
pixels to rework; paint or erase it instead. A soft-edged brush gives a soft
mask edge, exactly as it gives a soft mark.

### Clipping a layer to the one below

**Ctrl+Alt+G** (or right-click → **Clip to layer below**) clips the active
layer: it composites only where the layer below has content — Photoshop's
clipping mask, and the standard way to shade or texture a flat colour without
leaving its silhouette. A clipped layer's row shows an accent bar, and
several clipped layers in a row all clip to the first unclipped layer
beneath them, so lines / colour / shading stacks work as they do elsewhere.

Because the base layer is an ordinary layer, **an animated matte is just a
clipping mask over an animated base**: animate the base's cels and everything
clipped to it follows, with holds and exposure exactly as the sheet says. If
the base has a mask of its own, the clipped layers respect it too. A clipped
layer with nothing below it, or whose base shows nothing on this frame,
simply shows nothing there.

Both masks and clipping reach every render the same way: the canvas, exports,
thumbnails and the AI's view of the document all show the carved result.

### Adjustment layers and effects

Open the **Effects** panel (Window → Effects) to filter without repainting.
Three filters ship — **Levels**, **Hue / Saturation** and **Gaussian blur** —
alongside five **layer styles** — **Drop shadow**, **Outer glow**, **Inner
glow**, **Stroke** and **Bevel** — and every one is a setting, never baked
pixels: your strokes are untouched, sliders re-render, and undo steps back
through every change.

An effect can sit in two places, chosen at the top of the panel:

- **Layer** — the stack on the active layer, applied to that layer's own
  drawing before its blend mode and opacity. Blur the shadow layer, grade the
  ink.
- **Scene** — one grade over the whole composite, everything the camera sees.

Hue / Saturation is the one effect the Layer scope does not offer: its true
colour-wheel maths runs on the composite path, so on a single layer's own
drawing it would do nothing. Add it as an **adjustment layer** and — if you
want it on one layer only — **clip it to the layer below**; that is the same
result, and the panel's add row keeps the distinction honest by only listing
what will actually change pixels where you are.

One honest caveat: Hue / Saturation's **Lightness** lifts and lowers
everything equally, black ink included — a positive nudge greys linework.
For real value work reach for **Levels** (its gamma and output sliders are
the tool built for it); Lightness is for a quick wash.

### Layer styles

The styles are the reverse case: a glow, a stroke, a shadow or a bevel
decorates a layer's **own silhouette**, so they live on the Layer scope only —
the scene grade and adjustment layers, which work on the composite beneath
them, have no silhouette to read and do not offer them. Each style's colour
sits in the panel as a swatch and a hex field.

- **Drop shadow** — distance, size, angle and opacity; the angle is the light
  direction, so the default 120° throws the shadow down and right.
- **Outer glow** and **Inner glow** — size, spread (outer) and opacity.
- **Stroke** — width, opacity, and a position slider: **0** outlines outside
  the silhouette, **1** paints the rim inside it, **2** centres the line on
  the edge.
- **Bevel** — size, depth, light angle, and a direction slider: **0** shades
  inside the edge (raised), **1** builds a ridge around it. Highlight and
  shadow colours are yours to change. This is the smooth bevel only —
  contour and gloss curves arrive with the timeline's curve editor.

**Styles follow what you actually see.** Mask half a drawing and the glow
hugs the half that is left; a blur on the same layer is trimmed by the mask
instead, because a blur is part of the drawing where a glow decorates it.
Styles apply after the layer's filters, whatever order the stack lists them
in. A live stroke grows its layer's glow as you draw it, and merging a
styled layer down bakes the style into the merged pixels — exactly what the
canvas showed.

### The filters

Six filters on the **Colour** and **Detail** shelves do the everyday
Photoshop jobs, and every one works wherever you put it — a layer's own
stack, an adjustment layer, or the whole scene:

- **Sharpen** — an unsharp mask. *Amount* is the strength, *Radius* the size
  of detail it works on. The radius starts at 2 pixels because anything
  smaller has no effect at all.
- **Find edges** — keeps where the picture changes and drops the flats,
  drawn as dark lines on white.
- **Invert** — the negative, and its own undo.
- **Threshold** — everything to black or white either side of a *Level*.
  It reads brightness, so a saturated colour comes out one flat tone rather
  than breaking into a primary.
- **Posterize** — flattens to a chosen number of *Levels*, keeping true
  black and true white at the ends.
- **Gradient map** — replaces tone with colour: the darkest parts take the
  *Shadow* colour, the lightest the *Highlight*, and *Midpoint* decides how
  much of the picture sits in each. A quick duotone, and a fast way to
  colour-key a whole shot.

*Emboss is Planned* — the honest version needs machinery that arrives with a
later change, and a half-working one is worse than none.

### Effects that move: wiggle, flicker and grain

Three effects vary **by frame** rather than sitting still, and they are what
the panel's **Animation** shelf is for.

- **Wiggle** shifts the layer by a random-looking amount that changes every
  few frames — the boil a drawing gets when it is traced again and again.
  *Amount* is how far it can stray, in pixels.
- **Flicker** dips the layer's strength, never above full — a failing light,
  a strobe, a shape that stutters in and out.
- **Film grain** lays a fine noise over the picture. It is a **Colour**-shelf
  effect and works on what is beneath it, so put it on the **Scene** scope
  (the whole composite, which is where grain belongs) or on an adjustment
  layer. *Size* is the grain's own size in pixels.

**Hold says how often each one changes**, in frames — the same unit the
exposure sheet uses. A wiggle on a hold of 2 moves on 2s, alongside a
sequence animated on 2s; grain defaults to a hold of 1, because film grain
moves every frame. **Nothing here is random in the sense that matters**: the
same frame always looks the same, so scrubbing back and forth, exporting
twice, or reopening the file next year all give exactly the picture you
approved.

**A held drawing still moves.** Wiggle and flicker read the *playhead*, not
the drawing, so three frames of one held cel boil for all three — which is
the reason to reach for them.

**Seed** is what makes two layers differ. Every effect starts with its own,
so two wiggling layers do not move in lockstep; change it to re-roll a
motion you did not like, and leave it alone to keep the one you did.

**The quickest way in is the layer row itself**: right-click → **Layer
style** and pick a style — it lands on that layer and the Effects panel
opens with its sliders in hand. A layer carrying any effects wears a small
***fx* chip** beside its thumbnail; click it to jump to the panel.

**Everything here is a setting, and everything switches off.** Each effect
has its own checkbox in the panel, and the whole stack has a **master
switch** — the checkbox at the top of the panel, or **Layer style →
Disable effects** on the row. One click silences every effect on the layer
while keeping each one's own on/off and every slider exactly as you tuned
them; the fx chip goes hollow, the way a disabled mask's chip does. There
is no destructive version of any of this — the only thing that ever bakes
an effect into pixels is *Merge down*, which says so first.

**Adjustment layers** are the third place, and the one Photoshop users will
reach for: right-click a layer → **New adjustment layer** (or use the
buttons in the Effects panel) and a layer with no drawing of its own lands
above the active one, applying its effect to everything beneath it. It is an ordinary layer everywhere it counts — **mask
it** to grade part of the picture, **clip it** to the layer below to grade
one silhouette, fade it with its opacity, hide it with its eye, and it holds
across the whole timeline like any layer. Delete it and nothing was ever
touched.

Effects on layers you paint under update live, exports and the AI's view show
exactly what the canvas shows, and a document that never opens the panel
never stores or pays for any of it. An effect saved by a newer version of
Lightbox than yours shows in the stack marked *from a newer build* — it is
kept exactly as saved and simply does nothing here.

Keyframing effect values over time is *Planned*: the file format already
stores animated parameters, but the editor for placing those keys arrives
with the timeline's curve-editing work.

---

## Selections and transforms

Marquee, freehand, polygon, ellipse and magic wand, with **Shift** to add and
**Alt** to subtract — the crosshair wears a small **+** or **−** while the
modifier is down, so which way the press will combine is visible before you
make it. Grow, shrink and feather. A selection clips painting, and
the clip is part of the record, so a reload paints the same shape. The wand
shows the region it would take as a faint, still dashed outline before you
click — faint and still so it cannot be read as a selection you already made.

**The polygon shows its working.** The first click puts a small ring where the
vertex landed, and from then on a dashed band follows the pointer from the last
vertex you placed, so between clicks you can see the segment you are about to
commit to. The ring stays on the first vertex because that is the one you
double-click back to when you close the shape. **Escape** abandons a polygon
you have started; so does reaching for another selection shape, since none of
the others can finish it.

**Ctrl+D deselects, whatever tool you are holding.** Worth saying plainly
because a selection clips painting: if the brush seems to have stopped working,
the usual reason is a selection still up somewhere off screen, and Ctrl+D is
the answer. **Ctrl+A** selects the whole canvas — except with one of the arrows
in hand, where "all" means the objects on the canvas rather than the canvas.

**Delete clears what is inside the selection; Backspace fills it with the
background colour.** Both leave the outline up, because the next thing you
usually do with an emptied region is put something else in it, and both are
ordinary undo steps. With no selection up, Delete falls back to deleting
whatever lines the Arrow has picked.

**A selected guide takes Delete first**, ahead of the selection — the thing
wearing the selection marks is the guide, so that is what the key acts on.

**Every one of those is on the Select menu too**, next to Image in the menu
bar: *All*, *Deselect*, *Invert*, *Grow*, *Shrink*, *Delete contents* and
*Fill with background*. The keys are faster once you know them; the menu is
where you find out they exist, and where you check what a key does without
pressing it. Entries that need a region to work on — Grow, Shrink and Fill with
background — grey out until there is one. *Deselect* and *Invert* never do:
Deselect also clears lines and symbols picked with the Arrow, and inverting
nothing is how you select everything.

**A selection belongs to its document.** Switch tabs and it stays behind;
switch back and it is where you left it. A new document starts with nothing
selected.

### Copying lines out, and pasting them back

**Ctrl+C copies what you have selected; Ctrl+V pastes it onto a new layer.**
It works with whichever tool made the selection — a box or lasso from the
Select tool, or lines picked with either arrow — and the two gestures mean
slightly different things, on purpose:

- **A region copies what you boxed.** Draw a box across half a line and the
  paste shows that half. The line is not cut in two to do it: it travels whole
  with your selection carried along as its clip, so the record still holds one
  line, and a later change of mind can still reach the rest of it.
- **Picked lines copy whole.** Clicking a line with the arrow means that line,
  all of it, however much of it your last box happened to cover.

When both a box and some picked lines are up, the box wins — the same order the
transform tool uses, so the two never disagree about what "selected" means.

**The paste lands on a new layer directly above the one you are on, in the
place it was copied from.** In place because the usual reason to copy a drawing
is to carry it to another frame or another shot, where landing anywhere else
means lining it up by hand every time; move it afterwards with any of the tools
if that is what you wanted. On its own layer because a paste is not part of the
drawing underneath it until you say so — merge it down when it is.

**The clipboard reaches between open documents.** Copy in one tab, switch to
another and paste. It is Lightbox's own clipboard, not the system one, so
nothing leaves the app and nothing arrives from outside it.

**Ctrl+X cuts**: it copies, then takes the same content out. A boxed region is
cleared and lines crossing the edge keep the part that was outside; picked
lines are removed entirely.

**Ink you have erased is never copied.** A line rubbed out along its whole
length is not on the canvas, so a box drawn over where it used to be picks up
nothing — the same rule that stops it being clicked, or moved.

**Ctrl+C, Ctrl+X and Ctrl+V still copy, cut and paste a whole *cel* when
nothing is selected.** With a selection up they mean the lines. If you want the
cel while a selection is up, press **Ctrl+D** first, or use the timeline's own
right-click menu, which is always the cel. Pasting takes whichever of the two
you copied most recently. Both sets of verbs also have menu entries — the lines
on the **Select** menu, the cel on **Animation** — and *Configure → Shortcuts*
has keys for the line-only forms if you would rather they never meant the cel.

**To crop the paper to a selection**, see *Cropping* under
[Documents and projects](02-documents-and-projects.md) — it is on the **Image**
menu, with the two resizes, because it changes the page rather than the
selection.

**Hold Ctrl inside a selection and drag to move what is in it**, without
reaching for a tool. The pointer becomes the four-way arrow as soon as you cross
into the marching ants, so you can see that you have got it before you press —
and it keeps that shape for the whole drag. Shift holds the move to one axis,
guides catch it, and the whole thing is one undo step. Outside the ants, Ctrl is
still the eyedropper it always was; the boundary is the selection edge and
nothing else. It moves the *lines* the selection holds rather than cutting the
pixels out, so a stroke crossing the edge travels whole. **Ink you have erased
stays erased**: eraser marks caught by the selection travel with the lines they
carve, but what they rubbed out of the lines that stay behind does not come
back — moving a selection never resurrects anything, in the drag or on apply.
The selection takes what you can *see*: a line you rubbed out entirely is not
on the canvas, so a box drawn where it used to be does not pick it up — and a
half-erased line goes by where its surviving ink sits, so boxing the visible
end moves it even when the rubbed-out part lay elsewhere.

**Ctrl+T** starts a transform. The gizmo gives move, scale, rotate and a
draggable pivot; **Perspective** mode gives four free corners. The whole
session is also under **Edit ▸ Transform** — start it there, and apply,
cancel, mirror either way, switch perspective on or reset without leaving the
menu; the same verbs are on a right-click over the gizmo.

**Scaling holds the side you did not touch.** Drag a corner and the opposite
corner stays put; drag an edge and the opposite edge does — which is what every
other tool does, and what your hand expects when it pulls one side of a box.
Hold **Alt** to scale from the pivot instead, growing all sides at once, and you
can press or release Alt in the middle of the drag. **Shift** keeps the
proportions, as ever, and combines with Alt. The drawing
moves *with* the gizmo — you see the result while you drag, not after you
commit. **The marching ants step aside while the gizmo is up**, and come back
around the drawing in its new position when you apply — the gizmo boxes the ink
it is moving, the ants trace the region you selected, and those two are rarely
the same shape, so showing both at once reads as two tools arguing rather than
one tool working. Your selection is not lost while it is out of sight: it moves
with the drawing, so a second transform straight afterwards lines up with the
first. Cancelling brings it back where it was.

Dragging with the **Move** tool keeps its ants, because there is no gizmo there
and the outline is the only thing showing you where the pixels are going. The session's controls — scope, sampling, perspective, mirror,
Reset, **Apply** and **Cancel** — live on their own page of the **Tool
options** docker, which opens by itself when the session starts, so they are
never off screen while a transform is live. Enter applies, Esc cancels,
from the keyboard as always.

**While a transform is up, the canvas belongs to it** — every press on the
drawing goes to the handles, whatever the toolbar says. **Picking a tool ends
the session and discards the drag**, on the grounds that reaching for the brush
means you are done transforming. Nothing is written to the drawing that way:
only Enter applies, so an accidental tool press costs you the drag and never
the artwork. Holding **Ctrl** for the eyedropper is a borrow rather than a
choice and leaves the transform alone.

**Scope** decides what moves: this cel, all layers at this frame, a marked cel
range, or the whole animation.

**With a selection up, only what is inside it moves.** Draw a box across half a
line, drag, and half the line goes — the half you boxed. The other half stays
exactly where it was. It works with any of the selection shapes, and however
little of a drawing you take: a marquee over one corner of one line moves that
corner.

**The line is not cut in two to do it**, which is the same answer copying a
region gives and for the same reason. The record still holds one line, carrying
your selection as its clip, so nothing about the mark itself changes — a brush
with scatter or jitter lands its grain exactly where it always did, and a later
change of mind can still reach the rest of the line. A stroke that sits *wholly*
inside the selection simply moves, with no clip and nothing left behind.

**A gradient is treated like anything else.** Move a selection on a layer with
a gradient on it and the ramp inside the marquee travels too — so a marquee
over a gradient sky leaves a rectangle of sky that has shifted with whatever
you moved. If that is not what you want, put the gradient on its own layer and
transform the layer above it.

**An eraser travels with the ink it carved.** If you rub a bite out of a line
and then move the line, the bite goes too — and if you move only part of it, the
part left behind keeps its own edge. Rubbed-out paint never comes back, whatever
you select and wherever you drag it.

**Lines picked with the Arrow can be transformed too**, and then the scope is
this drawing, because a line lives on one. If you have both an area selected and
lines picked, the **area wins**; the transform says which it took when it starts,
so you are not left guessing.

**The handles fit the drawing you can see.** A drawing you have reworked still
remembers what you rubbed out, and the box ignores it — so the handles sit round
the art rather than round its history. **An imported drawing transforms too**,
even though it is pixels rather than strokes; with a selection up the imported
pixels stay put, so the box shrinks to the lines that are actually going to
move.

Because strokes are geometry, a transform is **lossless**: rotating and
rotating back leaves no softening.

**A line painted under a selection keeps its clip, and the clip comes with
it.** Painting inside a marquee leaves the line holding that shape as a stencil
for ever after, which is what makes a reload paint what you saw. Move or rotate
that line and the stencil turns with it, so the mark you end up with is the one
the drag was showing you — rather than the old stencil cutting the new
position, which used to leave that one line looking as though it had jumped.

---

## Guides

**View → Guides** places rulers, grids, isometric axes, vanishing points and
the construction aids — a character height scale, an eye-line, a horizon.
None exist until you place one, and a document that never uses them carries no
guide machinery at all.

| Guide | What it constrains |
| --- | --- |
| Horizontal / vertical ruler | Strokes drawn along it come out straight |
| Grid | Points snap to its intersections — corners, shapes, the starts of strokes |
| Isometric | Three axes at ±30° and vertical, from one origin you can move once |
| Vanishing point | Strokes radiate from it. One is one-point perspective, two is two-point, three is three |
| Character height scale | Points snap to its head-unit divisions — chin on the fifth head, knees on the second |
| Eye-line / horizon | A horizontal ruler that wears its name, so a shared rig reads at a glance |

They do two different things, and knowing which is which is the whole trick:

- A **grid snaps points**. Each point independently goes to the nearest
  intersection. That is what you want when you are placing things.
- A **ruler or vanishing point constrains a stroke**. The first part of your
  drag says which direction you meant; once you have travelled far enough to be
  believed, it locks to the guide that matches and holds the rest of the stroke
  on that line. That is what you want when you are drawing *along* something.

Locking once is deliberate. Re-deciding every moment would mean a slightly
wobbly hand flicking between two vanishing points mid-stroke and the line
kinking. Draw across every guide and none of them takes it — a guide that grabs
strokes you meant freehand is a guide you turn off.

The ruler decides the direction, not how far: the stroke still goes where your
hand went. **⌗** in the shortcut bar turns snapping off without removing
anything, and hiding a guide does *not* stop it snapping — those are two
switches, because hiding a rig to look at the drawing under it is something you
do constantly.

**What snaps: anything you aim, and nothing you trace.** A point you place
deliberately goes to the guides — a stroke's start, a shape's corners, a
gradient's axis, a pen node, a polygon selection's corners, a rectangular or
elliptical marquee, and a point dragged with the white arrow. A line you draw
by hand does not, past its first point: a lasso and the middle of a brush
stroke follow your hand, because a guide pulling at every sample would fight
you the whole way round.

**Moving something snaps its edges and its middle**, rather than wherever you
took hold of it — so lining a drawing up against a ruler means dragging it near
the ruler, not grabbing its exact edge first. The corners find rulers and grids;
the centre finds a vanishing point. Whichever is nearest is the one that lands,
and if nothing is within reach the drawing goes exactly where your hand put it.
This is the Move tool and dragging lines picked with the Arrow; the transform
handles do not snap yet.

Previews tell the truth about this. The pen's rubber band runs to the place the
node will actually land rather than to the pointer, so what you see before the
click is what the click does.

**Holding Shift beats a guide where the two disagree.** Shift asks for an angle
— a level gradient, a square marquee — and a guide asks for a place; if a guide
pulled the end back off the angle you had just asked for, one of the two would
be lying. So with Shift down the angle wins, and the guides keep the other end.

Guides are saved with the document, like the camera, and drawn *over* the
artwork, translucent — under it they would vanish the moment they crossed an
opaque background layer. The snapped points are what the stroke records, so
moving a guide afterwards never moves a line you have already drawn.

#### Adjusting a guide

**Pick up the Move tool or the Arrow and the rig wakes up.** Every guide on the
canvas lights faintly, the one under the pointer brighter, and the ones you
click brightest of all — and a grid or an isometric rig, which is grabbed at its
anchor rather than on its lines, shows that anchor as a small square you can
aim at. With any other tool in hand the guides go back to being scenery you
draw over, so nothing glows at you while you are drawing.

**Two tools reach guides, and they do the same things to them.** The Move tool
is the one you reach for when the job *is* the rig; the Arrow picks guides
alongside the lines, symbols and anchors it already picks. Whichever you use,
a guide behaves the same way — the same click, the same drag, the same options,
and a guide you locked is out of reach of both.

**Clicking a guide points the tool options at it**, in the quick bar and in the
Tool options docker, because moving a guide and changing one are the same
intention reached for with the same hand. Clicking the drawing lets go again.

**Shift adds a guide to the selection and Alt takes one out**, the same as
everywhere else on the canvas, and dragging any guide in the group moves all of
them together as one undo step. With more than one picked the options go quiet:
they set *a* guide's numbers — this grid's pitch, this chart's head count — and
there is no single guide for them to mean. The group still moves.

**The selection is handed between those two tools and dropped by every other
one.** Switching between the Move tool and the Arrow keeps what you picked,
because both can act on it; reaching for the brush lets it go, so nothing stays
lit over a drawing you are painting.

| The selected guide | What you can set |
| --- | --- |
| Any guide | **X** and **Y**, in document pixels — the typed half of the drag, for a horizon at exactly y=540 or two vanishing points exactly as far outside the frame as each other |
| Guide line, grid, isometric | **Angle**, in degrees clockwise from horizontal |
| Grid | **Cell size** — the pitch of *this* lattice |
| Character height scale | **Head height** and **Heads** — the same two numbers the top-rung drag changes, typed exactly |
| Vanishing point | **Rays** — how many lines are drawn out of the point |
| Any guide | **Shown**, **Snaps** and **Locked** — and **Remove**, or select it and press `Delete` |

Every one of those changes *this guide, on this drawing*, and every one is an
undo step. A locked guide ignores the fields exactly as it ignores a drag.

**The ray count is what you see, not what you can snap to.** A vanishing point
constrains every direction through it whatever the fan is drawn at — fewer rays
to see the drawing through it, more to read the perspective, and the strokes
land in the same place either way.

**"Set as default" is the other half.** It is the deliberate act that also
changes what the *next* guide of that kind is made from: a grid's cell size, a
vanishing point's fan, a height scale's proportions. Without it nothing you do
here reaches beyond the drawing in front of you, which is the point — a
preference that rewrote itself every time you nudged one guide would not be a
default. The same values live in **Edit → Configure → Guides and grid** if you
would rather type them there.

A height scale saves a *proportion* rather than a head height in pixels — "six
heads standing in seven tenths of the canvas" — so the same default still lands
as a figure on a scene of a different size. Resizing a document afterwards does
not move a chart already on it; its size is document data by then.

#### The character height scale, and the named lines

**View → Guides → Add character height scale** stands a head-unit chart on the
canvas: a post with a rung per head and a count beside the top — "6 heads".
It is how animators keep a character on model, and it
behaves like one thing because it is one:

- **Drag the top rung to resize the character.** The divisions follow — a head
  count is a proportion, and a taller character is not more heads. The whole
  pull is one undo step. (Grabbing the post anywhere else moves the chart.)
- **Its divisions snap**, all the way across the canvas: put the chin on the
  fifth head wherever the character is standing. Above the top and below the
  ground there is nothing to snap to — the chart's extent is its point.
- **Edit → Configure → Guides and grid** lists every height scale on the
  document, where one head's height and the head count can be typed exactly.

**Add eye-line** and **Add horizon** place ordinary horizontal rulers that
carry their names on the canvas. They constrain exactly what a ruler does; the
label is the feature — on a rig somebody else set up, or pulled from a guide
set, a line that says "Horizon" needs no archaeology. Any guide that has a
name wears it the same way, and a guide set keeps the names of the guides in
it.

#### Rulers, and placing a guide by eye

**Edit → Show rulers** (`Ctrl+R`) puts a ruler along the top and left of the
canvas. They count in document pixels, they mark every guide that crosses
them, and a line slides along both as you move the pointer — knowing where you
are without stopping to read a tick is most of what a ruler is for.

**Drag out of a ruler to place a guide.** Out of the top one for a horizontal
guide, out of the left one for a vertical one; the guide follows the pointer
while you aim it. Let go back over the ruler and it never existed, which is
both how you delete one and how you get out of a drag you did not mean.

**A guide on the canvas is picked up with the Move tool or the Arrow**, wherever
the rulers are. The cursor changes when you are on one and the guide itself
lights up; there is nothing floating over the drawing to click instead. The
whole drag is one undo step, not one per twitch of the hand.

**Shift holds a dragged guide to one axis** — whichever the drag has gone
furthest along, measured from where you picked it up, so a horizon slides along
without drifting off its height. It is the same promise Shift makes on the
brush and on the Move tool's content drag, and it covers the group drags too:
guides, reference boxes, anchors and hitbox shapes moved together all hold the
same way. Press or release Shift in the middle of the drag and the thing
follows — the lock is read from the keys as you move, not once at the press.

The tool is the switch for all of this, on purpose: grabbing a guide and
drawing along one are the same gesture in the same place, so something has to
say which you meant. With any other tool in hand a guide is scenery you draw
over and nothing can nudge it by accident. Hiding or locking the guides
overrides both of them, because both mean "leave the rig alone".

| Edit menu | Key | What it does |
| --- | --- | --- |
| Show rulers | `Ctrl+R` | The strips, and with them the drag-out |
| Show guides | `Ctrl+;` | Take the rig off the screen. It still snaps |
| Lock guides | `Ctrl+Alt+;` | Pin them where they are, rulers or no rulers |

**⌐** and **🔒** in the shortcut bar are the last two, and they appear there
only while the rulers do — off the rulers, neither could change anything.

Rulers, guide visibility and the lock belong to the **workspace**, not the
document: they are how your screen is arranged, so they save, reset and switch
with everything else, and opening somebody else's file never rearranges them.
They also survive a restart — rulers left up are up again next time, saved
workspace or not.

**Several at once**: Shift-click to add guides to the selection, Alt-click to
take one out, and dragging any of them moves the whole group together as one
undo step. Either guide tool does both halves — picking and dragging are the
same tool's job, not one tool's each. `Ctrl+A` with the Arrow in hand takes
every guide on the document at once. The lock still holds: locked guides stay
put whether they were selected or not, which is the point of locking them.

#### Guide sets — the same rig on every drawing that needs it

A character's height lines, a street's perspective rig, an isometric grid the
whole level shares: guides you will want again on the next drawing. A **guide
set** is a named copy of a document's guides, kept in the **project** — which
is why this needs one; a loose document has nowhere to keep it.

**View → Guides → Guide sets…** is where sets are made and managed. Place your
guides on the canvas first — the canvas is the guide editor, and a set is work
you already did, the way a template is — then name them and save. The same
window renames a set, refreshes it from the open document, or deletes it
(deleting also takes back anywhere it was shared).

**View → Guides → Add from set** pulls a set into the open drawing. The guides
arrive as ordinary document guides — one undo step, and yours from then on:
dragging one afterwards changes this drawing only, never the set, and never
another drawing that pulled from it. Refresh the set deliberately from the
editor when the rig itself should change.

**A set lands on the paper it arrives at.** Sets remember the document size
they were saved from, so a rig drawn on a 4K sheet and pulled into a 1080p one
arrives at the same place in the frame at the same share of the height — a
six-head knight that filled 70% of the tall sheet fills 70% of the small one,
still six heads, still standing on the same ground. That is what keeps a
character the same height across a project whose documents are not all one
size.

Where the two documents have different *shapes* rather than different sizes,
height wins: everything is scaled by one factor taken from the height, and only
the anchors move sideways to keep their place across the frame. A line keeps
its angle, a grid stays square, and an isometric rig stays isometric — which
scaling the two axes separately would quietly have ruined.

Sets saved before this existed have no size recorded and arrive exactly as they
always did, in the pixels they were saved at. Save one again from an open
document to teach it its paper.

**Sharing decides what "Add from set" offers.** Out of the box every set in
the project is offered to every document. In the project window, right-click a
folder to **share** a set onto it — from the first share onwards the project is
scoped, and a document is offered what its own folder (or the folders above
it) declares: the knight's height guide stops appearing in the goblin's menu.
This is the same scoping palettes, gradients and brush tips use.

#### Guide defaults, and the guides already placed

**Edit → Configure → Guides and grid** holds what new guides are made from —
a grid's cell size, a vanishing point's ray count, a height scale's head count
and its share of the canvas height — and how close a point has to come to a
guide to be pulled onto it. It also
lists the grids and height scales already on the document, where a grid's pitch
and angle, a scale's head height and count, and each one's drawing and snapping
can be changed after the fact — each one an undoable step.

Changing the default cell size never touches a grid that already exists. Once a
grid is placed its spacing belongs to the document, and a preference must not
reach back into work you have already done against it.
