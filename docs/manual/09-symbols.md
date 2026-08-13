# Symbols

A **symbol** is a drawing stored once and placed many times. Edit the sword,
and every animation holding it changes — that is the whole point, and it is why
a placement refers to the symbol rather than copying it.

Symbols belong to the **project**, not to one animation, because a prop lives
above the animations that use it. **View → Symbols** opens the panel, and it
works with no project open too — your own library is yours, and it should be
there when you open the app to draw one picture. Placing one into a loose file
copies it into that file, so it still saves and reloads on its own. What needs a
project is *making* a project symbol, since there would be nowhere to put it.

## Two libraries: this project, and yours

| | Where it lives | Who can use it |
| --- | --- | --- |
| **Project** | in the project | everything under that project, and nothing outside it — unless you narrow it to a folder, below |
| **Global** | your own library, beside your brushes | every project you make, and one you start tomorrow |

The panel shows both in one grid, with a **◈** on the global ones and a filter
beside the kind filter when you want only one.

**Promote** copies a project symbol up into your library. It is a copy, not a
move — the project keeps its own. There is no demote, because it would not mean
anything: a global symbol you have placed is already in that project, so making
it project-only is just removing it from your library.

**Placing a global symbol copies it into the project first**, and this is the one
thing worth understanding rather than just using. It means:

- The project still renders with your library gone — move the folder to another
  machine, hand it to somebody else, open it in five years, and the art is there.
  That is why it works this way.
- **Editing a symbol in your library does not change projects that already placed
  it.** That is the price. A finished shot cannot change under you, which is the
  same trade templates make.

To roll a library fix forward, **Update from library** appears in the panel when
your library has a newer version of something this project uses. It is the
project asking; nothing in your library ever reaches into a project on its own.
Placements are untouched and need no touching — they refer to the symbol by
identity, so replacing what that identity resolves to *is* the update.

### Narrowing a project symbol to one folder

By default a project symbol is offered to every drawing in the project, which is
what the table above says and what every project means until you say otherwise.

**Share a symbol onto a folder** — drag it from the asset library onto the
folder's row in the project manager's **Assets** tab, or pick it from the
row's share picker — and that stops being true: from then on a folder is
offered the symbols declared on it and on the
folders above it, and nowhere else gets them. It is the only one of the folder
declarations that *takes away* — a palette shared on a folder adds to what is
available, a symbol shared on a folder is a decision that the rest of the
project should not see it. The status line says so the first time, because a
picker that quietly loses most of its contents is a bad way to learn a rule.

Two things it does not do:

- **It never narrows your own library.** Global symbols are yours in every
  project. Placing one copies it into the project, where it can then be declared
  like anything else.
- **It never changes a drawing.** A document that already places a symbol keeps
  drawing it after you move it somewhere that does not declare it — a placement
  resolves by its own id, and narrowing is the picker's business.

Taking the last symbol declaration back returns the project to offering
everything, rather than to offering nothing.

*The full set of folder declarations — palettes, gradients, guides, templates and
export presets alongside this one — is in [Documents and
projects](02-documents-and-projects.md#everything-a-folder-can-decide).*

## Making one

Draw something, then **Make symbol** in the panel's footer and give it a name.
The strokes leave the drawing and a placement of the new symbol takes their
place. Nothing about the picture changes at that moment — the mark is the same
mark, in the same position.

## Placing, moving, and letting go

- **Place** puts the selected symbol in the middle of the current drawing.
- **Dragging a tile onto the canvas** puts it where you drop it, which is the
  point of dragging rather than pressing Place.
- The **Move tool** drags a placement the way it drags anything else. A placed
  symbol under the cursor is picked up before the drawing underneath it is; the
  symbol itself is not touched, so the other placements of it stay where they
  are. Hold Shift to keep the move to one axis.
- **Several at once**: select them with the Select tool first, and the Move tool
  then drags the whole selection together — from anywhere on the canvas, not
  only from on top of one of them. The group is one undo step, so taking the
  move back takes all of it back.
- **Break link** turns a placement back into ordinary strokes on that drawing.
  It is the honest way to get something you can edit stroke by stroke, and it
  is a one-way door: the result is a drawing, not a symbol.

A placement can be moved, scaled, rotated, faded and time-offset. It cannot
have one of its strokes nudged — that would be a different drawing, and
pretending otherwise is how a symbol quietly becomes a copy.

## Finding one

The panel filters by **kind** — prop, pose, expression, hand, face, FX,
background — and searches names *and* tags. Tags are a plain comma-separated
line rather than folders, because a sword is a prop, and it is also "knight",
and also "act two"; filing it once makes the other two searches fail.

## Cycles

A symbol can hold several frames — a walk, a flicker, a blink. A placement of
one advances with the timeline, and its **frame offset** shifts where in the
cycle it starts, so one stored walk can carry two characters half a stride
apart.

#### Using a cycle as something to draw over

A stored cycle makes a good **underlay**: a plain, unstyled base animation you
draw the real thing on top of, updated in one place. It works today, in three
steps:

1. Make the cycle as a symbol — line only, no styling. Keep it global if more
   than one project needs it.
2. Place it on its own layer, below the one you will draw on. Drop that layer's
   opacity so it reads as a guide.
3. Right-click that layer → **In exports** → **Never export this layer**.

Now the base cycle is a live link: fix the timing or the drawing once and every
animation built over it follows, and none of them ship it. This is the
base-character workflow — one "knight, no styling, 12-frame run", drawn over per
shot.

*A proper **reference layer** — one setting that ghosts it, keeps it out of
exports and badges it in the Layers docker, instead of the three steps above — is
Planned.*

## Editing one

Select a tile and press **Edit**, or double-click it. The symbol opens in a tab
of its own with a transparent background — a symbol is drawn over something, so
there is no paper behind it — and you draw on it with the ordinary tools. A
multi-frame symbol gets a cel per frame on the timeline, so a cycle is edited
the way any short animation is.

**Every change lands in the symbol as you make it.** There is no apply step.
Switch back to an animation and every placement of that symbol is already
showing the new drawing. That is the whole promise of the feature, and it is
why a symbol is worth making instead of copying a drawing around.

A symbol tab cannot be saved as a file of its own: it belongs to the project,
and saving the project writes it.

## Knowing what changed under you

When a symbol is edited, placements made before the edit are marked as such.
Nothing is broken — they already show the new drawing — but the app can tell
you *which* of the drawings in front of you changed while you were elsewhere.
**Acknowledge**, in the Symbols panel, clears the marks; it changes nothing
about the picture. The bar is not there at all when there is nothing to
report.

There is deliberately no "put it back the way it was when I placed it". The fix
for an edit nobody wanted is to undo it in the symbol, once — not to pin two
hundred placements to old copies of it.

## Deleting, and exporting

**Usage** counts where the selected symbol is placed — how many placements, in
how many documents — across the whole project. It is a button rather than
something the panel keeps up to date, because answering it means reading every
animation in the project, and a character with forty of them should open by
reading one file rather than forty.

Deleting a symbol leaves any placements of it alone; they simply stop drawing,
and the app tells you how many were left behind. That is deliberate — a delete
that quietly edited forty animations is not one anybody could risk. Press
**Usage** first if you want to know before rather than after.

**Export document…** writes a standalone file that carries the symbols it uses,
so an exported animation renders identically somewhere else.

*Symbols cannot contain other symbols.*
