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
the upper half of a row lands above that row, the lower half lands below it,
and dropping on a folder header files the layer into that folder. The layer
takes the folder of wherever it lands — dropping between a folder's members
joins the folder, dropping beside a loose row leaves it — and the whole drop is
one undo step. A drag moves the one row you picked up; the ▲/▼ buttons remain
the way to move a multi-selection as a block.

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

---

## Selections and transforms

Marquee, freehand, polygon, ellipse and magic wand, with **Shift** to add and
**Alt** to subtract. Grow, shrink and feather. A selection clips painting, and
the clip is part of the record, so a reload paints the same shape.

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

**A selection belongs to its document.** Switch tabs and it stays behind;
switch back and it is where you left it. A new document starts with nothing
selected.

**Ctrl+T** starts a transform. The gizmo gives move, scale, rotate and a
draggable pivot; **Perspective** mode gives four free corners. The drawing
moves *with* the gizmo — you see the result while you drag, not after you
commit. The session's controls — scope, sampling, perspective, mirror,
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
range, or the whole animation. With a selection active, only the strokes inside
it move — and they move whole, so connected drawings stay connected.

Because strokes are geometry, a transform is **lossless**: rotating and
rotating back leaves no softening.

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

Guides are saved with the document, like the camera, and drawn *over* the
artwork, translucent — under it they would vanish the moment they crossed an
opaque background layer. The snapped points are what the stroke records, so
moving a guide afterwards never moves a line you have already drawn.

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

While the rulers are up, **a guide on the canvas can be picked up and moved**.
The cursor changes when you are on one; there is nothing floating over the
drawing to click instead. The whole drag is one undo step, not one per twitch
of the hand.

Rulers are the switch for all of this, on purpose: grabbing a guide and drawing
along one are the same gesture in the same place, so putting the rulers up says
which you meant. With them down, a guide is scenery you draw over and nothing
can nudge it by accident.

| Edit menu | Key | What it does |
| --- | --- | --- |
| Show rulers | `Ctrl+R` | The strips, and with them the drag-out and the grab |
| Show guides | `Ctrl+;` | Take the rig off the screen. It still snaps |
| Lock guides | `Ctrl+Alt+;` | Pin them where they are, rulers or no rulers |

**⌐** and **🔒** in the shortcut bar are the last two, and they appear there
only while the rulers do — off the rulers, neither could change anything.

Rulers, guide visibility and the lock belong to the **workspace**, not the
document: they are how your screen is arranged, so they save, reset and switch
with everything else, and opening somebody else's file never rearranges them.

**Several at once**: select guides with the Select tool and the Move tool drags
the whole selection together, as one undo step. The lock still holds — locked
guides stay put whether they were selected or not, which is the point of
locking them.

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

**Sharing decides what "Add from set" offers.** Out of the box every set in
the project is offered to every document. In the project window, right-click a
folder to **share** a set onto it — from the first share onwards the project is
scoped, and a document is offered what its own folder (or the folders above
it) declares: the knight's height guide stops appearing in the goblin's menu.
This is the same scoping palettes, gradients and brush tips use.

#### Grid settings

**Edit → Configure → Guides and grid** holds the cell size a new grid is made
with and how close a point has to come to a guide to be pulled onto it. It also
lists the grids and height scales already on the document, where a grid's pitch
and angle, a scale's head height and count, and each one's drawing and snapping
can be changed after the fact — each one an undoable step.

Changing the default cell size never touches a grid that already exists. Once a
grid is placed its spacing belongs to the document, and a preference must not
reach back into work you have already done against it.
