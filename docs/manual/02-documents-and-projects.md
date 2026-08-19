# Documents and projects

## Documents and projects

### A document

One drawing or one animation: layers, frames, and the strokes on them. Saved as
a single `.lightbox.json` file. **File → New** makes one.

A document **reopens where you left it**: the frame the playhead stood on at
save is restored on open, so a scene put down mid-shot shows the same picture
it showed — a posed rig stands in its pose instead of snapping back to the
start of the timeline. A document saved at the first frame writes nothing
extra to the file.

The New dialog asks what the document is *for* — Illustration, Animation, Game
art, Storyboard, Comic, Asset library, or **None**. **None is the default**, and
it means exactly what it says: a single file, no project structure. The choice
only affects which panels you are offered.

The **⇅ button** beside the size fields trades width for height — portrait to
landscape in one press, without retyping either number. The **background** is a
colour swatch that opens the same picker as every other colour in the app:
wheel, sliders, and hex at the bottom for pasting a value in.

### Changing the size

Two operations, on the **Image** menu, and the difference between them is the
whole point.

**Resize canvas** (`Ctrl+Alt+C`) changes how much paper there is. Nothing you
have drawn moves — add 200px on the left and every line stays exactly where it
was, with the new paper appearing beside it. The **anchor** grid says which way
the paper grows: anchor top-left and it all appears on the right and below;
anchor centre and it splits evenly. Crop by giving a smaller number, and the
anchor decides which edge is kept.

**Resize image** (`Ctrl+Alt+I`) scales the artwork itself. Everything moves and
everything scales with it — line positions, brush sizes, textures, guides,
symbol placements. **Keep proportions** is on by default here, because scaling
the two axes differently distorts the drawing and is almost never meant. This
is also where **PPI** lives: it says how large these pixels print, so it belongs
to the artwork rather than to the paper. Changing only the PPI is a valid
change, and it moves nothing.

Both modes have a **⇄ button** beside the size fields that swaps width and
height. It deliberately ignores *Keep proportions* — a swap changes the aspect
ratio by definition, and a link that "corrected" it back would make the button
do nothing.

**Why the two are separate, and why it matters more here than in most
applications.** Lightbox decides the fine detail of a mark — its scatter, its
grain, the tiny variations that make it look drawn rather than printed — from
*where the mark is*. Move a line and it comes back with a slightly different
texture. That is fine when you asked to scale the artwork: you asked for
different art. It would not be fine if adding a margin silently re-textured
every stroke in the drawing, so **resizing the canvas is guaranteed not to touch
a single mark.**

The dialog tells you which one you are about to get, in a sentence, before you
press the button — including whether the marks will change. Both are a single
undo step, however much they touched, and the view refits to the new paper
afterwards.

### Cropping

Two more on the same menu, for the times you already know where the paper
should stop and do not want to work out the numbers.

**Crop to selection** takes the paper down to the bounding box of whatever is
selected. Any selection will do — marquee, lasso, polygon, wand — because paper
is a rectangle and the *box* around the selection is what it can be. Draw the
marquee where you want the edge, then crop. A selection dragged past the edge
of the page is clamped to the page: crop never makes the canvas bigger, which
is what *Resize canvas* is for.

**Trim to drawing** finds the smallest rectangle that still holds every mark,
and puts the paper there. It measures **every frame of every layer**, not the
drawing in front of you — trimming to the current cel would quietly cut the ink
off the other two hundred. Hidden and locked layers count too, since both still
render and still export. The background layer does not: it is paper rather than
artwork, and counting it would mean the trim always found the page exactly as
large as the page.

**Neither one deletes anything.** Like *Resize canvas*, a crop moves three
numbers on the document and leaves every mark exactly where it was drawn — so
ink outside the new edge is still there, and growing the canvas back brings it
back unchanged. That is worth knowing in both directions: you can crop
experimentally and lose nothing, and you cannot use a crop to throw work away.
The reason is the same one that keeps *Resize canvas* honest — a mark's texture
is decided by where the mark is, so a deleted stroke redrawn later would not be
the same stroke.

Both are one undo step, both refit the view afterwards, and both are on
**Image**. Neither has a key by default; both are in **Configure ▸ Shortcuts**
under *Image* if you want to give them one.

### A project

A project is a body of work: **every 2D asset in a game, an animated feature,
an episode of a show** — or one character, if that is what you are making.
Lightbox does not decide which, and it does not impose a shape on it.

**File → New project…** creates a `.lbproj` **folder** containing almost
nothing:

```
Production.lbproj/
  project.json               the index
  palettes/                  the palette you start with
  unassigned-documents/      the drawing you had open, if you had one
```

`unassigned-documents/` is where a drawing goes when it belongs to the project
and to no folder in it. Nothing else lives there, and it stays empty once you
have filed everything — a project written before this was renamed keeps its
`documents/` folder, and nothing moves on its own.

**No folders you did not ask for.** There is no `characters/`, no `scenes/`, no
`assets/` waiting to be filled in. You build the structure, and the structure is
whatever suits the work.

#### Folders

**＋ New ▸ Folder** makes one, named whatever you type — *Episode 2*,
*Act 1*, *Sc 014 — Rooftop*. Folders nest to any depth, and a new folder or
document goes **inside whatever is selected**, so building a tree is a run of
clicks rather than a create-then-file.

The chevron on a folder row shows or hides what is in it, and stays that way
while you work — saving does not spring everything open. Each level is indented
one step, and a document sits one step in from the folder holding it, so the
tree reads as a tree.

**Wherever you last clicked is where you are.** Selecting a row — with either
mouse button, on any kind of row — makes it the folder new work goes into and
the thing **🗁** shows in the file manager. Opening a folder with its chevron
counts too: expand *Knight* and the next document you make is in *Knight*.

Drag a folder or a document onto a folder to move it. Dropping a folder onto
something inside itself does nothing, because there would be no way back to it.

**A move moves the file.** Drag a drawing into a folder and the file goes with
it — there is no copy left in the folder you dragged it out of. If the file
cannot be moved — open in another program, a permission, or something already
there under that name — the move is **refused** whole, the panel is unchanged,
and the status line says so. Nothing changes by half.

The name is yours and the folder on disk is a tidied version of it: *Act 2 —
Interiors* becomes `act-2-interiors`. What you typed is what the panel shows.

#### Naming and renaming

**Everything asks for a name before it exists.** Nothing arrives as *Untitled
(3)* to be corrected later — the box is prefilled with what it would have been
called, so Enter is the fast path and typing is the considered one. Cancel and
nothing is created.

**The name starts from where it is going.** Make a document inside *Knight* and
the box says `Knight - ` with the cursor after it, so typing `walk` gives you
*Knight - walk* and the file `knight-walk.lightbox.json`. It is an ordinary text
box: select it and type to replace it entirely. Accept it untouched and you get
*Knight*, not *Knight -*.

An animation is named after the character it lands under, the same way. Folders
are not prefixed — they are structure, and *Knight - Locomotion* would compound
into *Knight - Locomotion - Knight - Walk* one level down.

**Rename…** on the right-click menu, or **double-click the name** — in the
Project panel and in the project manager's Structure tab alike. (Double-click
elsewhere on a row still opens it; the name is the part that edits, the same
split the Layers panel uses.) The rename reaches disk: renaming a folder moves
the folder and everything under it.

**The project itself renames too** — double-click the project row's name in
the panel, or **✎ Rename project…** in the project manager. The `.lbproj`
folder on disk is renamed with it (type the name with or without the suffix;
the folder keeps whichever it had), the panel, the manager's title and
**File ▸ Recent** all follow, and a name already taken beside the project
refuses whole.

If the name is already taken, or the file cannot be moved — open in another
program, or a permission — the rename is **refused**, the box stays open so you
can fix it, and the panel's status line says which of those it was. Nothing
changes by half.

#### Removing and deleting

Two operations on the right-click menu, and they are not the same thing:

| | |
| --- | --- |
| **Remove from project** | Takes it out of the panel. **The file stays on disk.** Removing a folder puts what was inside it back at the project root, so no drawing disappears with it. |
| **Delete permanently…** | Removes it *and* deletes it from disk. |

Deleting a folder that has anything in it asks first, and says how much — *"Delete 'Art' and the 1 folder and 1 document inside it?"* — because *are you sure?* tells you nothing you can weigh. An empty folder goes without asking.

A removed document stays removed: reopening the project does not claim the file back, even though it is still on disk.

#### Which palettes a document paints from

A palette declared on a folder is offered to **everything under it, at any
depth** — put the knight's palette on the knight folder and every animation
inside it paints from it, whether they sit directly there or three folders down.
Rearranging the folders afterwards changes nothing.

Palettes accumulate rather than replace: a document sees the studio palette on
the project, the show's on the show folder, and the shot's extra swatches on the
shot, all at once. Where two declare the same swatch, **the nearest one wins** —
so a character can override one colour without copying the whole palette.

A palette that everything should see, wherever it is filed, can be **published**
to the whole project. A nearer palette still beats a published one, so publishing
never takes an override away from somebody.

Projects made before this existed are unchanged: every palette is offered to
every document, exactly as before, until you declare a scope.

**To share one:** open the **project manager** (▦ at the top of the Project
panel, or **File ▸ Project manager…**), go to **Assets**, and either drag the
palette from the asset library onto the folder's row or select the row and pick
it from **share something here…**. Everything under that folder paints from it,
and the status line says exactly what it now feeds. A project that has never
shared anything keeps offering every palette to every document, so nothing
changes until you say so — and taking the last one back returns you to exactly
that.

#### Everything a folder can decide

A palette is one of several, and they all work the same way: give the folder
the thing in the **project manager's Assets tab**, and everything underneath it
— at any depth — gets it. These gestures used to live in the Project panel's
right-click menu, one submenu per kind; they moved when that menu passed twenty
entries, and **Share & assets…** in the menu is the door to where they went.

| Give a folder… | What it decides |
| --- | --- |
| a **Palette** | What everything under it paints from |
| a **Gradient** | Which gradients it can reach |
| a **Symbol** | Narrows which symbols are offered under it — see below, because this one takes away rather than adds |
| a **Brush tip** | Narrows which of the project's tips are offered under it — narrows, like symbols |
| **Guides** | Guides drawings under it can pull in — a character height guide is this and nothing else |
| a **Template** | What a new drawing made here begins as |
| an **Export** preset | Its export settings, *and* where one file ends — see below |
| a **Reference** | A drawing or sheet everything under it draws against — see the next section |

The shares **add up**: share two palettes and the folder offers both. Template
and export **replace**, because a drawing starts from one template and exports
one way, and offering two would be offering a choice nobody made.

**What a folder decides is worn on its row** in the Assets tab, as chips — its
own declarations, not what it inherits from above. The **✕** on a chip takes
one back; **⤒** publishes it to the whole project and **⤓** takes it back to
*this folder and everything under it* — project-wide reach is what an
environment layout that backgrounds and characters both work from needs.

**Symbols are the one that narrows.** Everything else on that list starts as
*nothing shared* and grows; a symbol is available to the whole project from the
day it is made. So the first time you share one, the rule flips: from then on a
folder is offered the symbols declared on it and on the folders above it, and
nowhere else gets them. The status line says so at that moment, because a picker
that quietly loses most of its contents is a bad way to find out.

Take the last symbol declaration back and the project returns to project-wide —
not to *scoped to nothing*, which would empty every picker.

**It narrows what you are offered, never what is already drawn.** A drawing that
already places a symbol keeps drawing it after you move it into a folder that
does not declare it. Scoping is the picker's business; a placement resolves by
its own id and always will.

**Brush tips work the same way**, and for the same reason: a tip is offered to
the whole project until you say otherwise, so declaring one takes it away from
everywhere else. Only the project's own tips — your own library and the built-in
shapes are never narrowed, because they follow you between projects rather than
belonging to this one.

Global symbols — the ones in your own library, marked ◈ — are never narrowed.
They are yours in every project, and placing one copies it into the project,
where it can then be declared like anything else.

A declaration whose thing was deleted still appears, showing its id instead of a
name. That is deliberate: otherwise a palette quietly missing from a picker has
no visible cause and nothing to clear.

#### Character sheets in a project

A **character sheet** — several views of a subject on their own canvases:
Front, Side, Back, Expressions — belongs to the **project**, not to whichever
drawing happened to create it. Add one from the **Reference sheets** panel and
it is filed on the **top folder above the document you are in**: make a sheet
while drawing in *Knight ▸ Locomotion* and it is the knight's, visible from
every drawing under *Knight* — combat as well as locomotion. A sheet made from
a drawing filed nowhere is project-wide, and every document sees it.

On disk it is its own file, `<name>.sheet.json`, inside the folder it is filed
in — so it travels with the character in a file manager and diffs in git like
anything else.

You can see and move a sheet in two places:

| | |
| --- | --- |
| **The Project panel** | The sheet is a row (▤, marked *Reference*) under its folder, above the drawings that consult it. Double-click opens it to draw on; drag it onto another folder to re-file it. |
| **The project window** | Select the sheet's row in **Structure** and pick a destination under **file sheet in…** — or drag the sheet from the **Assets** tab's library onto a folder's row. Both include the project itself, which every document sees. |

Re-filing moves the file on disk too, the same way moving a document does, and
changes who sees it: a sheet filed on *Goblin* stops appearing under the
knight's drawings.

**Outside a project nothing changed:** a standalone document keeps its sheets
inside itself, and making one on an unsaved document still offers the save
first — the sheet needs the document to live somewhere. A document from before
this change that carries sheets inside it moves them into the project the first
time it is opened there, filed on its top folder.

The panel is called **Reference sheets** rather than Character sheets, because
nothing about it was ever specific to characters.

#### References a document draws against

Two mechanisms, both real:

- **A sheet filed on a folder** reaches every drawing under it, at any depth —
  that is the whole sharing model for reference art, and the Reference sheets
  panel is where it lands. File a sheet by making it there, or drag it onto a
  folder in the project manager's Assets tab.
- **View → Reference** — further on in *Onion skin, references and the camera*
  — imports a picture, slices it into frames and lays it under your drawing,
  per document.

There used to be a third: a *reference declaration*, shared onto a folder the
way a palette is. It was recorded and never read — nothing ever showed a
drawing a reference because of one — and it was retired as **B133** rather
than kept as a control that does nothing. Any such declarations in an existing
project are cleaned up the next time it opens. If you want a drawing visible
to other drawings as reference today, put it on a sheet or file the sheet
beside them.

#### How a folder is exported

Give a folder an export preset — **Export · the preset** in the project
manager's Assets tab. That does two things at once, and the second is the one
worth knowing: it sets the settings, **and it says where one file ends**.

| The preset produces | What you get |
| --- | --- |
| **One file** | Everything under the folder packs into a single sheet |
| **One file per folder inside it** | Shared settings, one sheet per character |
| **One file per document** | A file each — what you get before you declare anything |

So declaring on *Knight* makes the knight one sheet; declaring instead on
*Knight ▸ Locomotion* makes locomotion its own. The status line says which.

**To run it**, right-click the folder and pick **Export this folder…**. It counts
first — *"2 files from 47 documents, 3 held back by status"* — and asks, before
it opens a folder picker and before it writes anything. A number tells you
whether you picked the right folder in a way that *are you sure?* cannot, and it
is worked out without reading a single drawing, so asking is cheap even when the
answer is no.

**It also says when what it is about to rebuild has drifted.** If a folder was
exported and its drawings have changed since, the same sentence adds *"1 artifact
moved on since it was last built (2 documents changed)"*. That is the same
question one moment earlier — is this worth exporting — so it goes where you are
already reading a number rather than on a badge somewhere else.

Each artifact is written independently: one that fails names itself in the
status line and the rest still land. A drawing that has gone missing from disk is
named too rather than stopping the run.

Two targets cannot hold several documents, and say so instead of quietly
exporting the first: a **PNG sequence** is one animation's frames numbered into a
folder, with nothing to say where one ended, and a **GameMaker sprite** is one
animation with one origin and one image speed. Sheets, Unity, Godot and Unreal
all take a folder's worth — one file, one clip per document, named after the
document.

**Test export** is next to it, on a drawing rather than a folder: that one
animation, written to `test-exports/` beside the project. It ignores grouping and
the status filter, and it can never overwrite a deliverable — so looking at one
cycle cannot break the build.

A preset can also be told which **statuses** are allowed out, so work in progress
stays out of a shipped sheet, and which status **rebuilds** it — mark an
animation Ready and the sheet holding it is rebuilt, not just that one drawing.

Exporting a folder includes everything nested inside it.

#### Rows that have no file behind them

Two of them, and they mean different things:

| | |
| --- | --- |
| **not saved yet** | You made it and the project has not been saved since. It is in the project, it is not on disk, and **Save** writes it. Ordinary — every new folder and document says this until you save. |
| **not on disk** | This *was* written and its file cannot be found now — deleted in a file manager, on a drive that is not mounted, or lost to a branch switch. Worth looking at. |

Either way the row is dimmed, so you can see at a glance which of your work exists as files. Nothing is removed from the project on your behalf: *"this is in your project and I cannot find it"* is the true statement, and taking it out stays your decision.

**File → New inside a project makes a project document.** It gets a row like any
other, filed in whatever you have selected, saying *not saved yet* until you save
— and the project's **Save** writes it along with everything else.

The one thing that *is* removed for you: **close a document you never saved and
its row goes with it.** There is nothing to keep — no file was ever written — and
leaving the row would mean the panel pointing at something that does not exist
and never will. Save it once and that stops applying: closing it afterwards
leaves it in the project, and if its file later goes missing the row stays and
says so.

**Where you save it decides whether it stays.** Save As into the project and the
row follows the file — its name, its folder, wherever you put it. **Save As
outside the project and it leaves the project**, because you have given it a home
somewhere else; the file is yours and nothing is written inside the project for
it.

#### What a folder is

**There is one kind of container, and it is the folder.** A character is a
folder holding a character's work; a scene is a folder holding a scene's. There
is no separate character and no separate scene to make, and nothing is filed
outside the tree.

That is not only tidiness. A folder is what decides which palette, references,
guides, template and export preset a drawing can reach, and what a whole-project
export contains. While a character was a second kind of container, none of that
reached a character's animations — most of the content of an animation project
resolved nothing and exported nowhere.

**A folder is described by what it carries, not by a kind.** Any folder can have:

| | |
| --- | --- |
| **a reading** | what the AI understands the subject to be — see *AI assistance* |
| **a pivot** | where a game engine positions it from, for sprite-sheet export |
| **variants** | versions that reuse its work — Winter Armour, Player Two |
| **a running order** | the sequence its contents play or list in |
| **notes** | what it is, in your words |
| **shared resources** | a palette, references, guides, a template, an export preset |

Each is absent until you add it, and any combination is allowed — a folder with
a reading *and* a running order is both, and nothing has to decide which it
"really" is. Select a folder and the bar under the panel lists what it carries.

**You say what a folder is with its glyph.** Right-click ▸ **Glyph** offers a
grid of common ones — 🎬 🧍 🐾 🗡 🏠 🌳 🚗 ✨ and more — and **Glyph ▸ something
else…** takes anything you type. A production has props, environments, effects,
crowds and vehicles, and no fixed list of kinds would name them; the grid is a
starting point rather than a vocabulary. Nothing in Lightbox reads the glyph, so
it can mean exactly what you want it to.

Plain JSON throughout, and every drawing is an ordinary document — so an old
loose file *is* one, and a project is readable in a text editor.

A project opens by reading its index only. Forty drawings open without loading
forty files; each is read when you open it.

### The running order

Folders list what is in them by name until you arrange them. **↑** and **↓**
move the selected row within whatever contains it — a drawing among its folder's
drawings, a folder among its siblings — and the first move is what gives that
folder an order. A folder nobody arranged carries none.

**The order is partial.** Pin the three opening shots and leave the other forty
alone; what you named comes first, in the sequence you set, and the rest follow
by name. An order that names something you later deleted simply skips it — an
ordering is a preference, not a claim about what exists.

One order per folder, read twice: it arranges the folder's drawings and its
sub-folders both, so a scene containing shots *and* sub-scenes has one running
order rather than two that can disagree.

Each folder row shows how long it runs — `0:04.5 · 108f` — summed from what is
in it. The lengths are recorded when each drawing is saved, so one you have
never saved shows nothing rather than zero: a running time that quietly counts
unmeasured drawings as empty is the number somebody schedules against. The
panel's total covers the folders you arranged, because those are the ones where
a running time means something.

Deleting a folder keeps its drawings — they come back to the project root.
Reorganising a film must not be the fastest way to delete it, and the files on
disk are never touched either way.

### The project window

**File ▸ Project manager…**, **Ctrl+P**, or the **▦** button at the top of the
Project panel. The docker is what you use while drawing — find it, open it,
move it. This is what you use *between* drawings, and it is a separate window
so it can sit on a second monitor while the canvas keeps the first.

Five tabs, and a footer on all of them saying what the project holds and what is
wrong with it: *47 documents · 12 Ready · 3 Reopened · 5 unassigned*.

**Structure** is the tree with the columns the docker has no width for — glyph,
name, tags, status, who is on it, how long it runs, and what each folder
carries. Select several rows and a bar appears: set the status of nine drawings
at once, tag a folder and everything under it, assign a sequence to somebody.
The docker has no multi-select on purpose; a bulk edit is exactly the thing you
do between drawings rather than during one.

**And you can leave by way of a row.** Two gestures, on the bar above the tree
and on any row's right-click, and they are deliberately different things rather
than one with a guess in it:

- **⏵ Open** opens the selected **documents in Lightbox**, as tabs — a document
  is artwork, and the application that owns artwork is this one. Several at
  once if several are selected, and a document already open is brought forward
  rather than opened twice. Folders in the selection are skipped rather than
  refused, so selecting a sequence and asking for its drawings does what you
  meant. **It closes the project manager**, because the window is modal and a
  tab opened behind it is a tab you cannot see. The one thing that keeps it
  open is a document missing from disk: the window stays and says which, or the
  sentence would leave with the window.
- **🗀 Show on disk** hands the selected row to your **file manager** — a folder
  opened, a document selected inside its folder where the platform can do that
  (Windows and macOS can; Linux has no portable way to select a file, so it
  opens the folder). One row rather than the selection, because revealing five
  rows means five file manager windows; it takes the first and says which it
  showed. **🗁 Project folder** does the same for the project's own folder
  whatever is selected.

The Assets tab's right-click offers the same pair on the same rows, and its
project row is the second way to the project folder. Double-click is *not*
either of these here — on this window it renames a row, which the docker does
not do, and an open that was told apart from a rename by a few pixels of
horizontal position would take the window down by accident.

**You can build the structure here too.** **＋ New ▾** above the tree — or
right-click any row — makes a **folder** or a **document**, with the docker's
rules unchanged: it lands where the selection is, the name is asked first with
the folder's stem already typed, and cancelling creates nothing. What the
window makes is **saved at once**, so the docker and a file manager both show
it without a second gesture — and a document made here arrives as **Draft**,
because every new document enters the pipeline on its first save.

**And rearrange it.** Drag any row — in Structure or in the Assets tab — and
the tree shows where it would land: a **line** above a row means *placed
before it, in its folder's running order*; a **tint** on a folder means
*filed inside it*. Drop on the empty space below the rows (or on the Assets
tab's project row) to move something to the **project root**. Reordering
writes the folder's running order, the same one the docker's ↑↓ nudge; moving
a document moves its file, refused whole if the disk says no. Double-click a
name to rename it in place.

**And take things out of it.** Right-click, in Structure and in the Assets
tab alike, offers the panel's two operations, which are deliberately not one:
**Remove from project** takes the row out of the index and leaves the file or
folder on disk — cheap to undo by hand — while **Delete permanently…** removes
it *and* deletes it, asking first for any folder holding anything. Removing a
folder returns its documents to the project root rather than losing them.

**Select exactly one folder and a panel opens on the right** — its notes, its
pivot, its reading and its variants, all editable. One folder rather than
several, because "the notes of nine folders" is not a thing. This is where a
reading is marked as **yours**, so a re-read refuses instead of overwriting your
corrections, and where clearing one tells you first what stops meaning anything:
*"Clearing this discards the reading you corrected by hand, its pivot, 2
variants."* Clearing takes the reading and nothing else — a pivot on a folder
nothing has read is still a pivot.

**There is no undo here, and nothing here is destructive.** Status, tags and who
is on something are notes *about* a drawing rather than part of it — none of
them touches a stroke or needs the drawing open — so setting one back is the
same gesture as setting it. Every bulk edit says what it did, and says so when
it did nothing.

**Status** is the same documents as columns: the six statuses in order, and
*No status* last on its own. "Nobody has said" is not the same as *Design*, and
folding them together would invent a pipeline stage for every file you imported.
**Drag a card between columns** to change one. A new document becomes **Draft**
on its first save, so fresh work starts in the pipeline by itself; imported and
pre-existing files keep *no status* until you say, and clearing a status back
to *no status* sticks.

**Assets** is the one thing a right-click menu cannot be. A menu declares on one
scope at a time and shows nothing about the others, so *why is this drawing
painting from the studio palette* is answerable only by working it out. Here the
project, every folder and every document are rows of one table, and what each
declares is visible at once. Nearest wins: a document beats its folder, a folder
beats the project, and the project beats your own defaults.

Select a row and the bar underneath offers everything that row could be given —
palettes, gradients, guide sets, symbols, tips, templates, export presets and
references — with the kind in front of the name, so you pick the thing rather
than first picking which of eight words it files under. The **✕** on any chip
stops sharing it there; **⤒** publishes it project-wide and **⤓** takes it back
to the subtree.

**The asset library** sits beside the table: everything the project *has* —
reference sheets, palettes, gradients, brush tips, symbols and templates —
each wearing an automatic designation and a glyph unique to its kind
(▤ Reference, 🎨 Palette, ◧ Gradient, 🖌 Brush tip, ❖ Symbol, 📄 Template), so
an asset is recognisable as what it is before you read its name. **Drag one
onto a row** to give it to that scope, and the status line says what it now
feeds: *"Palette 'Knight warms' shared with Knight — it feeds every document
under 'Knight'."* Whatever a scope has shows as a **pill on its row** — every
kind, the same way: a palette declared there, a template default, and a sheet
filed there all read alike, though a sheet's pill has no ✕ (it reports filing;
move the sheet by dragging it). Guide sets are the one kind the table knows
about that nothing can create yet — *Planned*. Two kinds land differently: a **sheet** is filed on the
folder rather than declared, because for sheets filing *is* what feeds the
documents below; a **template** becomes the scope's one default — the drop
replaces, and the status line names what it displaced.

**Right-click makes assets**, the way Structure's right-click makes structure:
a **reference sheet**, a **palette**, a **gradient**, or a **template** (a
blank document already wearing the flag). Same rules as everything else —
named first, cancelling creates nothing, saved at once — and one more that
follows from where you clicked: made on a folder's row, a palette or gradient
is **shared there at once**, because that is what making it *there* means. A
sheet is filed on the folder; a template is filed there and offered for
dropping wherever it should become the default.

**And unmakes them.** The ✕ on a declaration chip stops sharing an asset at
that scope without touching the asset. Right-click a library entry for
**Delete permanently…**, which deletes the asset *and* every declaration of
it — sheets and templates are files, so their files go too, after asking.
Symbols are the one refusal: their instances live in documents, so the
Symbols panel owns that delete.

Two moments the panel says something you would otherwise have to notice: sharing
the **first** thing of a kind is what switches that kind from *everything
applies everywhere* to *only what is declared*, and taking back the **last** one
switches it back. Both are one click, and both change what every other drawing
in the project sees.

**Export** is what exporting the whole project would write, standing still: one
row per file, what is in it, which preset it uses, and how many drawings are
held back by their status. That last number is how you find out most of a scope
is excluded *before* wondering why the sheet came out half empty. It shows the
plan and does not run it — exporting is the Export window's job.

**People** is names. Add somebody and they can be assigned; rename them and
every row they are on changes at once, which is why it is a list rather than a
name typed per drawing. Removing somebody says how many documents they are on
before it unassigns them.

**No roles and no rights, deliberately.** A project is plain JSON in a folder —
that is what lets it live in git and be read by anything — so a permission
Lightbox enforced would be one a text editor defeats. Rather than claim
something it cannot keep, Lightbox stays out of it: share the project the way
you share any other files, and if your studio runs ShotGrid, Kitsu or Flow, that
is the system that owns who does what.

#### Tags

**Type one and it exists.** There is no list to maintain and no vocabulary to
set up first — the tags offered are the ones the project already uses, and a new
word joins by being used. `Rough` and `rough` are the same tag.

Tags go on folders and on documents, and **a folder's tags reach everything
under it**. Tag `characters/` as `hero` once and every animation in it answers
to `hero`, without listing them. The tag column shows inherited tags as well as
a row's own, so it is always clear why something matched.

The **TAG** and **WHO** pickers at the top narrow every tab at once — filtering
the tree and then finding the status board showing something else would be two
projects on one screen.

### Versions

A version is a **kept copy of a document or character sheet as it was saved**,
with a label and optional notes, stored inside the project's `versions/`
folder. Undo remembers keystrokes within a session; a version is authored —
"roughs approved", "before the redesign" — and survives across sessions and
machines, because it travels with the project folder.

Three ways a version comes to exist:

- **File ▸ Save version…** (`Ctrl+Alt+S`) keeps one of the active document or
  sheet, after saving it. The offered label continues the numbering; the notes
  are yours.
- **Promoting a document to Review or Ready** — in the project window's status
  column or board — keeps one automatically, tagged with that milestone. That
  is what makes "export the version that was approved" answerable after
  somebody keeps drawing: the Ready bytes are in the history even if the file
  has moved on.
- **Reverting** keeps one of the state being replaced, labelled *Before revert
  to …* — so a revert can never lose work, and a second revert undoes the
  first.

**File ▸ Version history…** (`Ctrl+Alt+H`) lists them newest first — also
reachable from a right-click on any document or sheet row in the project
docker. Select a version and **Revert to selected** to put its file back; an
open tab reloads to show it. Greyed lines in the list record an action (a
revert) rather than a state, and cannot be reverted to.

Versions belong to a project. A loose document has no `versions/` folder to
keep them in, so the menu items stay greyed until the document is saved into a
project. A project that never uses versions never grows the folder.

Branching a version into its own line of work is *Planned*; today the history
is a single line per document.

### Templates

**A template is an ordinary document with a flag set.** Not a new file type, not
a folder, not a list built into the app. That is the whole of it, and everything
useful follows from it: you can turn work you have *already done* into a
template, which is where real templates come from — the third walk cycle you set
up the same way.

| | |
| --- | --- |
| **Make one** | **File ▸ Use as template** on any open document. It does not move and does not change; it gains a flag and starts appearing in one more list. |
| **Start from one** | **File ▸ New from template…** lists the project's templates. Pick one and you get a **copy** — yours from the first stroke. |
| **Edit one** | Open it and draw. It is a document; every tool works on it. |
| **Stop being one** | Untick the same menu item. Nothing else changes. |

**A template is an asset, and looks like one.** From the next save its row —
in the Project panel and in the project manager — wears 📄 and the word
*Template*, automatically, the way a sheet wears ▤ *Reference*. It appears in
the project manager's **asset library**, and dragging it onto a folder there
says *new documents in this folder start from it*. That drop **replaces**
rather than adds — a scope starts new documents from one template — and the
status line names what it displaced.

**A template is copied, never referenced.** That is the difference from a symbol,
which *is* a live link. Because the copy has no link back, editing a template can
never reach into an animation somebody already started — so there is nothing to
lock, nothing to version, and no dialog asking whether to propagate. It is also
why you can change a template whenever you like: your edits apply to copies made
*after* them and to nothing made before.

A template carries what a document carries: the layer stack with its names,
blend modes, opacities and locks; the exposure sheet; guides and grids; the
camera if it has one; the canvas size and frame rate; and any drawing you want in
it — a pivot cross, a ground line, construction guides on their own locked layer.

Without a project none of this is needed: a standalone template is a file you
Open and then Save as, which already works. What a project adds is being able to
*list* them, so the menu items only appear when one is open.

#### Updating a document from its template

You fixed the template. **File ▸ Update from template…** rolls that forward into
a document that came from it — one document at a time, when you ask, as a single
undoable step. Nothing ever travels the other way, so a finished shot cannot
change under you.

It shows what would change and you tick what you want:

| Can be pulled | |
| --- | --- |
| **New layers** | Added in the template's position, with anything drawn on them. A layer you have is never removed. |
| **Layer properties** — name, blend mode, opacity, lock | Matched by layer identity, not by name, and **skipped for any layer you have drawn on** unless you tick it yourself. |
| **Guides and grids** | Replaced wholesale. They are aids, not art. |
| **Frame rate** | Applied. Canvas size is *not* — changing a canvas under finished drawings is a different operation with its own questions. |
| **Camera** | Added if the document has none. An existing one is never overwritten. |

**It never pulls your drawings or your timing.** Those are the work. A template's
frames were superseded the moment you drew, and re-timing is what a timing preset
is for.

The menu item is greyed out unless this document came from a template and that
template still exists. Deleting a template cannot break anything that was made
from it — the option simply is not there.

### Changing what a project is for

**File → Project type** converts an open project between Illustration,
Animation, Game art, Storyboard, Comic, Asset library and unset.

It is a change of intent, not a migration. **No artwork is read, rewritten or
recreated** — an illustration that becomes an animation is the same file, byte
for byte — and nothing already authored is dropped. A camera keyframed under
Animation is still there under Game art: the new type ignores it, it does not
erase it, so converting back finds everything where you left it.

Afterwards you are told what changed and offered the new type's panels. Offered,
not applied: which panels you want is a preference, converting is a decision
about the project, and rearranging your screen as a side effect of a menu item
is not something a tool should do.

**The Project panel** lists your folder tree, each folder's drawings underneath
it, and below them any drawings that belong to the project rather than to a
folder. Double-click one to open it as a tab.

**＋ New** offers two things, because there are two — creating work inside a
project should not be followed by a second step that files it:

| | Where it goes |
| --- | --- |
| **Folder** | Inside the selected one |
| **Document** | In the selected folder, or the project itself when nothing is |

There is no *Character*, no *Scene*, no *Animation* and no *Shot* on this menu.
A character is a folder you have read, a scene is a folder you have arranged,
and all four of those words named the same two things.

**Right-click a row** for everything else:

| | What it does |
| --- | --- |
| **Open** | As a tab in Lightbox — the same as double-clicking |
| **Open with default app…** | Hands the file to whatever application your desktop associates with it. A `.lightbox.json` usually lands in a text editor. |
| **Show in file manager** | Reveals it in Explorer, Finder or your Linux file manager. A folder shows its directory; a drawing shows its file, selected where the platform can do that. |
| **Copy path** | The absolute path, on the clipboard |
| **Duplicate** | Copies a drawing, art and all, into the same folder. A walk you want to turn into a limp starts here. The copy reaches disk on the next save. |
| **Rename…** | Edits the name in place. Enter commits, Escape cancels. |
| **Glyph** | What this folder is, in your words. A grid of common ones, or type anything. |
| **Remove from project** | Takes it out of the index. The file stays on disk. |

The **🗁** at the right of the panel's header shows **whatever you have
selected** in your file manager — the same thing as **Show in file manager** on
the right-click menu, without the right-click.

**The project itself is the first row**, named after its folder on disk —
`Production.lbproj` rather than *Production*, because that is the part you
cannot see anywhere else. Select it and 🗁 opens the project folder, which is
what the button used to do whatever you had picked. With nothing selected at
all, it opens the project folder too.

You can select it, drop a document onto it to take that document out of every
folder, and copy its path. You cannot rename it, remove it or delete it from
here: that is a folder your whole project lives in, and renaming it is a thing
to do with the application closed.

**The panel keeps up with the folder on its own.** Lightbox watches the project
directory, so a document you delete in a file manager, a folder another program
writes, or a branch you switch in git all show up without reopening anything.
A row whose file is no longer there is marked **not on disk** rather than
removed: "this is in your project and I cannot find it" is the true statement,
and taking it out of the project stays your decision.

**⟳** in the header, or **F5**, re-reads on demand and says what it found. You
should rarely need it — it is there for the places a directory watch cannot be
set up, a network share being the usual one, where a project still opens
normally and this is how you get a current view. Like every shortcut in
Lightbox, F5 is rebindable in **Edit ▸ Configure ▸ Shortcuts**.

**Drag a document or a folder onto another folder** to re-file it, or onto a
project-level row to take it out of every folder. It keeps its identity, so a
tab already showing it stays bound to it — and **the disk moves with it**: the
file, or the folder's whole directory, is moved first and the project records
it only if that worked. The project's directory and the panel's tree are the
same thing, which a save keeps true even for a project rearranged before this
rule existed — any file recorded outside its folder's directory is brought
home, and a directory no layout explains is removed once nothing lives in it.
(A stray directory *with* something in it is reported, never deleted — the
same reasoning as **－**, which removes a row from the index and never deletes
a drawing.)

**Which file am I actually editing?** Two answers, both always on: the tab
wears a small violet **P** badge before its name when the document belongs to
the open project (a loose file has none — they save to different places), and
the panel row whose document is on the canvas carries a violet bar and tint.
That mark is not the selection: selecting a row aims the next command, and the
bar stays put while you do.

**A file inside the project folder is in the project.** Save a loose document
into the project's directory and it joins the project on the spot — the panel
grows its row (filed in the folder you saved it into), the tab gains the P
badge, and it arrives as **Draft** like any other new document. Save a project
document *outside* the project and the reverse happens: it leaves the project
and its row goes. The badge, the panel and the disk always tell the one story.

### Variants

A variant is a version of a character that **reuses its animations** — Winter
Armour, Damaged, Player Two. It owns overrides, not copies: it names a palette,
and it may point specific animations at different documents. Everything it does
not override comes from the character, so a walk cycle drawn once is the walk
cycle of every variant, and fixing it fixes all of them.

The mechanism is the live palette. A variant is mostly a different set of
colours behind the *same swatch ids*, so switching variant repaints the same
drawings — no second copy of the art.

Shape differences colour cannot express (a helmet the base character does not
have) are what animation overrides are for: one animation replaced wholesale,
the rest still shared.

### The character library

A project whose type is **Asset library** offers its characters to other
projects. Importing one copies it — animations, variants and the palettes they
depend on — keeping swatch ids so the imported art still paints correctly.

Import copies rather than links. A linked character that edits in place is a
real feature and it is *Planned*; copying is honest about what this does and
does not quietly create a link that later breaks.

---
