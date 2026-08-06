# Exporting to a game engine

## Export for a game engine

**File ▸ Export for a game engine…** asks two things in order — the settings,
then where to put it. Settings first on purpose: choosing a filename before a
format is how you end up with a `.png` holding a folder name.

| | |
| --- | --- |
| **Format** | PNG sequence, sprite sheet, or sprite sheet + Unity. |
| **Trim** | None, union (the default — one box for the whole sequence, so the character cannot jitter), or per frame. |
| **Layout** | Grid, or packed. Packed is tighter on ragged frames and only readable through the sidecar. |
| **Padding** | Transparent gutter around each cell, against an engine's filtering bleeding one sprite into the next. |
| **Background** | See below. |

Controls that do not apply are **not shown** — a PNG sequence has no cells and no
atlas, so it gets no layout picker.

#### Not exporting at all: mark it Ready

The shortcut through this whole section. Right-click a document in the **Project**
panel → **Status**, and set where it is: Design, Draft, In development, Review,
Ready, or Reopened. A coloured dot appears on the row.

Switch on **Configure ▸ Export ▸ Export automatically on a status change**, point
it at a folder, and reaching that status exports the asset there. Finish it, mark
it Ready, and the sheet and its sidecar land where your engine is already looking.
Nobody has to remember to export — and the export nobody remembered is the one
that makes a designer think you have not started.

| Setting | |
| --- | --- |
| **Which status fires it** | Ready by default. A studio that reviews in engine sets Review. |
| **Export preset** | The same presets as the export window. Save one there and it appears here. |
| **Output folder** | Absolute, or relative to the project — `../Game/Assets/Sprites` keeps everything portable. |

Three things worth knowing:

- **It is off until you switch it on.** This writes files into another project, so
  turning it on is your decision, not a default.
- **The status is always saved first.** If the folder is gone or your engine has
  the file locked, you keep the status and get a message. A production field
  should not be hostage to a network share.
- **Re-picking the status something already has does nothing.** Opening the menu
  to check must not re-export.

Status lives in the project index, not in the drawing — so marking something Ready
does not change the artwork file and does not need it open. **Reopened** is kept
separate from In development on purpose: it means *this was Ready and is not any
more*, which is the state a straight-line pipeline cannot express.

#### Normal maps

Tick **Also write a normal map** and the export writes
`<name>_normal.png` beside the sheet — a tangent-space normal map, so an engine
can light your sprites. Off by default: it doubles the texture memory for the
asset, and most 2D games do not light their sprites at all.

It works from the silhouette. The alpha channel says where the drawing is, and
the edge is bevelled inward:

| | |
| --- | --- |
| **Bevel** | How far in from the edge the surface finishes rising, in pixels. |
| **Strength** | How steeply it tilts. 1 is a natural rounded edge. |
| **Green points** | **OpenGL** for Unity and Godot, **DirectX** for Unreal. |

**Get "green points" wrong and the lighting looks inverted** — a character lit
from above reads as lit from below, and every bevel reads as a groove. Nothing
about the result will point at the setting, so it is worth knowing which your
engine wants. The mnemonic: on an OpenGL-convention map, green is bright at the
*top*. Choosing the Unity format does not change this for you, because a flipped
green is something you should see and set rather than have swapped underneath
you.

The map is generated from the finished sheet, so it lines up with it exactly —
trim, padding and packing included. And no preview light is ever baked into it:
what ships is the surface, not how it looked while you were judging it.

*Reading a drawing for real shape — knowing a cheek is round and a sleeve fold is
a crease — is a later tier. This one bevels the silhouette, which is what makes a
flat sprite catch a light at all. An interactive panel with a draggable preview
light is Planned.*

#### Presets, which are the actual point

"One click" is a claim about the *second* export. Three are built in:

- **Character sprites** — union trim, grid, background detection on.
- **Packed atlas** — per-frame trim, packed, one pixel of padding.
- **Backdrop** — no trim, keep everything including the paper.

Type a name and press **Save** to keep your own; they are marked **◈** and only
yours can be deleted. The preset you exported with last is selected next time.

When an export finishes, the status line says what came out — frame count, sheet
size, layout — **and names every layer it left out**.

#### Which engines this already covers

| Engine | What you need |
| --- | --- |
| **MonoGame**, **Raylib** | Nothing extra. Both load a PNG and take source rectangles you supply, and the sidecar is that list. |
| **Unity** | The Unity format below, which adds an importer script. |
| **Godot** | The Godot format below, which adds a GDScript importer. |
| **Unreal** | The Unreal format below, which adds a Python importer. |
| **GameMaker** | The GameMaker format below, which needs no importer at all. |

**For Unity** the exporter writes the sheet, the sidecar and a small importer
script. Drop them under `Assets/`, then **Assets ▸ Lightbox ▸ Import selected
sheet**: it slices the sprites, sets each pivot, and builds an animation clip per
tag with the right frame durations and any events you marked. It needs Unity's
**2D Sprite** package, which every 2D template already has. Lightbox never
touches Unity's own `.meta` files — Unity owns those.

**For Godot** you get the sheet, the sidecar and `lightbox_import.gd`. Put all
three anywhere under `res://`, open the script in Godot's script editor and run
it (**File ▸ Run**, or Ctrl+Shift+X): it finds every Lightbox sheet in the
project and writes a `SpriteFrames` beside each one, with an animation per tag,
the right frame timings, and looping set as you tagged it. Point an
`AnimatedSprite2D` at that resource and it plays.

Two things worth knowing. **Lightbox writes no `.tres` itself** — the resource
is built by Godot's own API inside that script, which is why it stays correct as
Godot changes rather than being a format we guessed at. And if you have a pivot
set, the sidecar carries a per-sprite offset for it, because Godot measures a
sprite's offset from the middle of the region rather than as a fraction of it.
Godot 4; on 3.x the frame timings would be dropped without saying so. Lightbox
never touches `project.godot` or the `.godot/` cache — Godot owns those.

**For Unreal** you get the sheet, the sidecar and `lightbox_import.py`. Put them
anywhere under the project's `Content` folder, enable the **Python Editor Script
Plugin** and **Paper2D**, then run the script from **Tools ▸ Execute Python
Script…**. It imports the sheet as a texture, makes one Paper Sprite per frame
and one Flipbook per tag, and tells you in the Output Log what it built.

Three things worth knowing, and the first will bite you if you skip it:

- **The canvas height is in metres, and that matters more for Unreal.** An Unreal
  world unit is a *centimetre* where Unity's is a metre, so the same number means
  two very different sizes. Lightbox does that conversion for you — which is why
  the field says metres — but it means a figure you copied from a Unity project's
  pixels-per-unit will not do.
- **If the script cannot set something, it says so.** Paper2D's scripting surface
  is officially experimental and property names have moved between engine
  versions. Rather than quietly producing a sprite with nothing in it, the script
  collects everything it failed to set and finishes with an error naming each one.
  If you see that, the assets are incomplete — check your engine version before
  using them.
- **Asset names are cleaned up.** Unreal will not accept spaces or brackets in an
  asset name, so `Hero sheet (v2).png` becomes `Hero_sheet_v2`, with sprites
  numbered after it and one flipbook per tag. Two tags with the same name get
  numbered rather than overwriting each other.

Mipmaps are turned off and filtering set to nearest, because a mipmap on an atlas
averages across the edges of your sprites and puts a sliver of the neighbour on
each one. Lightbox writes no `.uasset` — it cannot, they are binary, which is why
this is a script rather than a file.

**For GameMaker** there is nothing to run. GameMaker slices a strip whose filename
ends in `_strip8` into eight frames on import, so that is what you get: **one
strip per animation**, named for it. Drag `Knight_run_cycle_strip4.png` into the
IDE and you have a four-frame sprite called `Knight_run_cycle`. Tags become
separate files, because a GameMaker sprite holds one animation and not a list of
them; a document with no tags is a single strip.

Set the sprite's speed to the fps in the sidecar and make sure the editor's units
are **Frames per second** — it offers frames-per-game-frame too, and the same
number means something different under each. `image_speed` multiplies that and
starts at 1, so nothing else needs setting. Holds need no special handling: a
drawing held on 2s is two identical frames in the strip, which is how a strip says
"hold".

Two things this target decides for you rather than asks:

- **Packing, columns and padding disappear from the dialog**, because GameMaker
  works out a frame's width by dividing the image's width by the number in the
  filename. One pixel of gutter or a packed layout and every frame comes out
  sliced through the middle. A per-frame trim is refused for the same reason and
  the export tells you it did.
- **What GameMaker cannot do, the export says out loud.** A sprite has one speed,
  no reverse and no ping-pong — so a ping-pong tag or a tag you marked as
  not-looping is reported in the status line, with what to do about it in code,
  rather than exported as though it worked.

No `.yy` files are written. They are JSON and writable in principle, but the
schema moves between releases and carries IDs the IDE expects to own, so a file
written for the wrong version gives you a project that will not open. The strip
convention is a naming rule instead, and naming rules do not go out of date.

## Leaving the background out

A background you added so you could see the line should not end up in a sprite
sheet. Three modes, and the sheet says which layers it left out and why — nothing
disappears silently:

| Mode | What goes |
| --- | --- |
| **Paper only** | The document's Background layer. This is the default and always has been. |
| **Detected** | Also any layer that covers the whole canvas on **every** drawing it shows. |
| **Everything** | Nothing. For the asset whose background *is* the point — a backdrop, a sky, a tiling floor. |

Detection reads **pixels, not names**. A layer called "grey" that floods the
canvas is caught; a layer called "Background sketch" with a drawing on it is not,
because a rule that removed a layer on the strength of its name would eventually
ship a sheet with your artwork quietly missing. Names still get a mention: a kept
layer whose name reads like a background is reported as worth a look.

"Every drawing" is the part that matters. A layer that goes full-bleed for two
frames is a flash, not a background, and stays in.

Any layer can override the mode. Right-click it in the Layers docker, **In
exports**:

- **Decide automatically** — the default; the export's mode decides.
- **Never export this layer** — the reference photo, the colour check, the note
  to self. Left out of every export, in every mode.
- **Always export this layer** — keeps a full-canvas fill in when detection is
  on, which is how a backdrop gets exported without turning detection off for
  the whole document.

It is one undo step, and it marks the document changed: what leaves the app is
part of the document, not a preference.

*Per-folder defaults — characters never need a background, environments might —
arrive with the project asset folders. Today the choice is per export, with the
per-layer override above.*

**Stray strokes are not solved by this**, and honestly cannot be: a mark on the
wrong layer is a drawing, and nothing can tell it from one you meant. What the
export does give you is the report — every omitted layer named, so the sheet you
get is one you can account for.

## What the sidecar carries besides the frames

The `.json` written beside a sheet is the contract with your engine, and it holds
more than rectangles. Everything positional in it is measured **inside the
frame's own cell**, so tightening the trim can never move a pivot, an attachment
point or a collider:

| In the file | What it is |
| --- | --- |
| `pivot` | Where the drawing is anchored, per cell. |
| `anchors` | Named attachment points — a hand, a muzzle, a hardpoint. |
| `shapes` | Collision rectangles, each with a role: `hurtbox`, `hitbox` or `physics`. |
| `frameTags` | Your tags, as clips, with direction and a loop flag. |
| `events` | Only the markers you ticked as engine events. |

A collision rectangle exists **only on the frames you put it on**, and that is
how a hitbox becomes active for part of a swing: place it across the contact
frames and nowhere else. There is no separate on/off switch to keep in step,
so re-timing the animation moves the active frames with the drawings.

For Unity the same rectangles also arrive as a collider's `offset` and `size`, in
world units and measured from the sprite's pivot — the two numbers a
`BoxCollider2D` takes. The importer hands them to you rather than attaching them:
a sprite is an asset and a collider is a component, so there is nothing on the
sliced sprite to set. Filter on the role to decide which layer each one belongs
on.

#### Placing them: the rig overlay

Turn it on with **View ▸ Rig ▸ Edit anchors and hitboxes**, or **Ctrl+K**. It is
a mode rather than a held key: while it is on, dragging on the canvas moves rig
marks instead of drawing, so you cannot lay down half a stroke while reaching for
a socket. Turn it off and the overlay goes away completely — nothing sits over the
drawing when you are not editing the rig.

The same submenu adds a mark in the middle of the canvas — **Add anchor** or
**Add collision shape** — and deletes the selected one. Both arrive draggable.

Sockets and collision rectangles are placed on the canvas, with one set of
gestures for both — an anchor is a point and a shape is a rectangle, and the
overlay treats a point as a rectangle with no size.

Anchors draw as blue crosses and shapes as orange rectangles, so the two kinds
never blur together; the selected one turns white. An anchor inside a shape is
drawn on top of it, which matches what a click picks up.

- **Click** a mark to select it. A selected rectangle grows corner handles.
- **Drag** the body to move; drag a corner to resize. The opposite corner stays
  exactly where it is, and dragging past it flips the rectangle rather than
  turning it inside out. Marks follow the pointer as you drag, so you place them
  by eye rather than by letting go and looking.
- **Several at once**: select marks with the Select tool and the Move tool drags
  the whole group together, as one undo step. Anchors and hitboxes are selected
  separately — picking a rectangle drops the sockets — because they are two
  different jobs and a drag that moved both would be one you did not mean.
- **Push across** copies the selected mark's geometry over a range of frames —
  place a physics body once and send it down the whole cycle instead of dragging
  it twenty-four times.
- **Clear here** takes the mark off this drawing but keeps it declared. That is
  how a hitbox stops being active partway through a swing: it exists on the
  frames that connect and nowhere else.
- **Delete** removes the declaration and every placement of it.

Everything is one undo step, and **edits land on the drawing rather than on the
frame number.** Drag a socket while parked on a held frame and it moves the
drawing being held — the hold stays a hold. Re-time the sequence afterwards and
every mark travels with its own drawing.

Handles stay the same size to your hand at every zoom, and an anchor has a
slightly larger catch radius than a corner, because a point has no body to grab.

A sprite sheet is laid out as a **uniform grid** by default, because equal cells
are what consistent trimmed bounds produce anyway and every engine importer reads
a grid. There is also a **packed** layout that gives each frame its own size and
fits them together — on ragged frames it is around a fifth smaller, measured. It
is only readable through the sidecar's per-frame rectangles, so a packed sheet
reports no grid at all rather than a column count that would look right and be
wrong.

**Export document** is the escape hatch that matters. Inside a project, a
document refers to shared resources by id, so a lone file is no longer
self-contained. Export inlines them, and the exported file renders identically
with the project gone — which is checked by comparing pixels, not shapes.
