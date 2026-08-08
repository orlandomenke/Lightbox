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

---

## Selections and transforms

Marquee, freehand, polygon, ellipse and magic wand, with **Shift** to add and
**Alt** to subtract. Grow, shrink and feather. A selection clips painting, and
the clip is part of the record, so a reload paints the same shape.

**Ctrl+T** starts a transform. The gizmo gives move, scale, rotate and a
draggable pivot; **Perspective** mode gives four free corners. The drawing
moves *with* the gizmo — you see the result while you drag, not after you
commit.

**Scope** decides what moves: this cel, all layers at this frame, a marked cel
range, or the whole animation. With a selection active, only the strokes inside
it move — and they move whole, so connected drawings stay connected.

Because strokes are geometry, a transform is **lossless**: rotating and
rotating back leaves no softening.

---

## Guides

**View → Guides** places rulers, grids, isometric axes and vanishing points.
None exist until you place one, and a document that never uses them carries no
guide machinery at all.

| Guide | What it constrains |
| --- | --- |
| Horizontal / vertical ruler | Strokes drawn along it come out straight |
| Grid | Points snap to its intersections — corners, shapes, the starts of strokes |
| Isometric | Three axes at ±30° and vertical, from one origin you can move once |
| Vanishing point | Strokes radiate from it. One is one-point perspective, two is two-point, three is three |

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

Guides are saved with the document, like the camera, and drawn *under* the
artwork — a ruler on paper is something you draw over. The snapped points are
what the stroke records, so moving a guide afterwards never moves a line you
have already drawn.

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

#### Grid settings

**Edit → Configure → Guides and grid** holds the cell size a new grid is made
with and how close a point has to come to a guide to be pulled onto it. It also
lists the grids already on the document, where their pitch, angle, drawing and
snapping can be changed after the fact — each one an undoable step.

Changing the default cell size never touches a grid that already exists. Once a
grid is placed its spacing belongs to the document, and a preference must not
reach back into work you have already done against it.
