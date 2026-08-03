# Lightbox — user manual

Lightbox is for **frame-by-frame animation** and **digital painting**, with AI
assistance throughout — most visibly filling in the inbetweens.

This manual describes what the application does **today**. Anything not yet
built is marked *Planned*, with no promise of when. Nothing here is aspirational
prose about a feature that does not exist: if a section describes a button, that
button is in the build.

> **Keeping it true.** This file is part of the definition of done. A change
> that alters what an artist sees or does updates the relevant section in the
> same commit. A feature moving from *Planned* to real means deleting the
> *Planned* marker and writing how it actually works — not how it was going to.

**Contents**

1. [First run](#1-first-run)
2. [The window](#2-the-window)
3. [Documents and projects](#3-documents-and-projects)
4. [Workspaces](#4-workspaces)
5. [Drawing](#5-drawing)
6. [Colour](#6-colour)
7. [Layers](#7-layers)
8. [Selections and transforms](#8-selections-and-transforms)
9. [Animating](#9-animating)
10. [Camera](#10-camera)
11. [AI assistance](#11-ai-assistance)
12. [Saving, exporting and recovery](#12-saving-exporting-and-recovery)
13. [Symbols](#13-symbols)
14. [When the canvas feels slow](#14-when-the-canvas-feels-slow)
15. [Keyboard](#15-keyboard)
16. [Working with an agent (MCP)](#16-working-with-an-agent-mcp)
17. [Planned](#17-planned)

---

## 1. First run

Lightbox opens on an untitled document — 960 × 540 at 12 fps, on white paper —
with a brush selected. You can draw immediately. Nothing has to be created,
named or configured first.

A **start screen** appears over that document offering three things: the New
file fields, the New project fields, and what you had open last. It is asked
over the blank page rather than instead of it, which is what makes **Escape**
a complete answer — press it and you are on the blank page you would have had
anyway. *Don't show this again* turns it off, and *Edit → Ask what to open on
start-up* turns it back on.

Double-click a recent entry to open it, or select one and press Create. Recent
holds files and projects together, newest first — "what was I working on" does
not sort itself by kind. The same list is under **File → Open recent**, with
*Clear the list* at the bottom. Anything you open or save for the first time
joins it; anything that has since moved is simply not offered.

The blank-page start is deliberate, and it is the rule the whole application
follows:
**optional means absent, not disabled.** A project, a camera, a palette, a
gradient and six of the eight panels do not exist until you ask for them, and
until then they cost you no screen, no keys and no thinking.

---

## 2. The window

From the top:

| Strip | What it is |
| --- | --- |
| Menu | File, Edit, View |
| Tool options | Controls for the tool you have selected. Changes with the tool; never changes height, and never scrolls — anything that does not fit goes into the **▾** at the end. On the right, the workspace picker. |
| AI bar | Inbetween, a prompt box, and AI Draw |
| Tabs | One per open document |
| Work area | Tool column, canvas, and whatever panels you have docked |
| Info strip | Document size, layer and drawing counts, and how much headroom the machine has |

### Panels

Eight panels: **Project**, **Layers**, **Color**, **Character sheets**,
**Palette**, **Gradient**, **Reference**, **Timeline**. Open and close them
from **View**.

Each panel's header is three things at once:

- **A title.**
- **A switcher.** Click it and pick another panel: the two *trade places*. No
  panel is ever open twice, so "where is the palette" always has one answer.
  The timeline has no switcher — it has nowhere else to be.
- **A grip.** Press and drag the header to move the panel.

While you drag, a highlight shows where it would land:

- Near an **edge with nothing on it**, the highlight grows to the size of the
  area that would open.
- Over an **existing panel**, the highlight is a band above or below it (or
  left/right in a top or bottom strip) showing where it would slot in.
- Let go **over the canvas** and the panel floats in a window of its own. Drag
  its header back to a dock zone to put it away again.

Dragging the last panel out of an edge collapses that edge — no empty gutter.

Panels are sized in pixels, and a strip scrolls when it holds more than fits.
This is on purpose: five panels each too short to use is a worse outcome than
five panels at their proper size with a scrollbar.

Most panels are capped in width, because they hold fixed-size controls and
stretching them just adds whitespace. The **Layers**, **Project** and
**Timeline** panels are not capped — they hold lists as long as the work.

### Bars on the canvas

Two small bars float on the canvas itself rather than taking a strip:

- **View bar** — zoom, rotate, mirror, reset.
- **Shortcut bar** — onion skin on the layer you are drawing on, view-through-camera
  if the document has a camera, and one play/pause button.

Both are listed under **View** separately from the panels, because they are a
different trade: a panel takes room away from the drawing, a bar sits on top of
it. Somebody who wants no panels at all may still want the zoom readout.

**Drag a bar by its ⠿ grip** to any edge of the canvas. It follows the pointer
as you drag, goes to whichever edge you are nearest, and stays where you left
it along that edge as a fraction, so resizing the window does not send it
wandering. On a left or right edge it stacks downwards so its length runs
*along* the edge instead of jutting out over the drawing — the icons stay the
right way up. The zoom readout is the one thing that turns, because "100%" does
not fit across a narrow bar; it turns so its feet face the canvas.

**▾** rolls a bar up to its grip. **✕** hides it; View brings it back. Edge,
position, collapsed and hidden are all part of the workspace, so they save,
reset and switch with it.

What a bar offers depends on the work: an Illustration project is not going to
be played, so it is not given a play button, and no document shows the camera
toggle until it has a camera.

---

## 3. Documents and projects

### A document

One drawing or one animation: layers, frames, and the strokes on them. Saved as
a single `.lightbox.json` file. **File → New** makes one.

The New dialog asks what the document is *for* — Illustration, Animation, Game
art, Storyboard, Comic, Asset library, or **None**. **None is the default**, and
it means exactly what it says: a single file, no project structure. The choice
only affects which panels you are offered.

### A project

A project is the container Lightbox is really built around: **a character is
the unit of work, not a folder of files.** A character's animations share one
palette, one set of references and one pivot — which is the thing a folder of
loose files cannot express.

**File → New project…** creates a `.lbproj` **folder**:

```
Knight.lbproj/
  project.json                    the index
  characters/knight/
    character.json                palette, pivot, animation list
    animations/walk.lightbox.json a document, in today's ordinary format
    references/front.png
  palettes/palettes.json
  gradients/gradients.json
  assets/
```

Plain JSON throughout, and the animations are ordinary documents — so an old
loose file *is* an animation, and a project is readable with a text editor.

A project opens by reading its index only. A character with forty animations
opens without loading forty documents; each is read when you open it.

### Scenes

A character groups drawings by **who**; a scene groups them by **when**. They
cross — one scene holds several characters, one character appears in several
scenes — so neither is a folder inside the other, and the Project panel shows
both, characters first.

**＋ New → Scene** makes one; **Shot** adds a drawing under the selected scene,
making the first scene if there is none. Scenes are for the *shots* output
target: a film or a show, where the canvas is a world and a camera frames part
of it. A project making sprite sheets never needs one and, until you make one,
has no scene rows, no running order and no reorder buttons.

Each scene row shows how long it runs — `0:04.5 · 108f` — summed from its shots.
The lengths are recorded when each shot is saved, so a shot you have never saved
shows nothing rather than zero: a running time that quietly counts unmeasured
shots as empty is the number somebody schedules against. **↑** and **↓** move
the selected scene or shot in the running order.

Deleting a scene keeps its shots — they become project documents. Reorganising
a film must not be the fastest way to delete it, and the files on disk are never
touched either way.

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

**The Project panel** lists characters with their animations underneath, and
below them any documents that belong to the project rather than to a character.
Double-click one to open it as a tab.

**＋ New** offers what to make, and each lands somewhere specific — creating
work inside a project should not be followed by a second step that files it:

| | Where it goes |
| --- | --- |
| **Animation** | Under the selected character |
| **Character** | A new character, with its own animations and palette |
| **Document** | The project itself — a background, a colour test, a one-off |

**Right-click a row** for everything else:

| | What it does |
| --- | --- |
| **Open** | As a tab in Lightbox — the same as double-clicking |
| **Open with default app…** | Hands the file to whatever application your desktop associates with it. A `.lightbox.json` usually lands in a text editor. |
| **Show in file manager** | Reveals it in Explorer, Finder or your Linux file manager. A character shows its folder; an animation shows its file, selected where the platform can do that. |
| **Copy path** | The absolute path, on the clipboard |
| **Duplicate** | Copies an animation, art and all, into the same character. A walk you want to turn into a limp starts here. The copy reaches disk on the next save. |
| **Rename…** | Edits the name in place. Enter commits, Escape cancels. |
| **Remove from project** | Takes it out of the index. The file stays on disk. |

The **🗁** at the right of the panel's header opens the project folder itself —
the one path that is always there, however little of the project has been
created yet.

**Drag a document onto another character** to re-file it, or onto a
project-level row to take it out of every character. It keeps its identity, so
a tab already showing it stays bound to it. The file on disk is not moved
until the next save writes it to its new path, and the old one is left alone —
the same reasoning as **－**, which removes a row from the index and never
deletes a drawing.

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

## 4. Workspaces

A workspace is a named arrangement of panels. Workspaces are **global, never
stored in a project** — a layout is a property of you, not of the artwork, and
opening someone else's file must not rearrange your screen.

Seven ship with the app: one per project type, plus **Default**. Switch with the
picker at the top right of the tool options bar.

- **View → Workspace → Save current workspace** updates the selected one.
- **Save as new workspace…** stores the arrangement under a new name.
- **Reset to saved** discards your changes.

Saved workspaces carry a bin in the picker. The built-ins do not: a built-in is
what *reset* falls back to, so deleting one would take the fallback with it.
Saving over a built-in forks it instead of overwriting it.

The picker marks a workspace you have since rearranged with a `*`.

When you create a project, you are asked whether to keep the arrangement you
are in or take that project type's defaults. It is a question at that moment,
not something the project remembers.

---

## 5. Drawing

### Tools

Down the left: **Brush** (B), **Eraser** (E), **Fill**, **Picker**,
**Gradient**, **Select** (S). Press Select again to cycle its variants, or hold
it for the list: Freehand, Polygon, Box, Circle, Magic wand.

Hold **Ctrl** at any time to pick a colour off the canvas without changing tool.

### Brushes

The tool options bar carries the controls you reach for constantly — brush,
size, hardness, opacity, stabilizer. **⚙** opens every parameter, grouped:
General, Effects, Medium, Pen pressure, Presets.

#### Finding a brush

The brush button opens a flyout, not a dropdown: once you have forty brushes,
scrolling is the wrong verb.

- **Search** matches names and tags.
- **Tag chips** across the top narrow the list. Pick several and you get all of
  them — "inking *or* roughs" — because asking for a brush that is both is
  almost always an empty list.
- The chips only appear once you have tagged something.

Tag a brush on the **Presets** page: a comma-separated list, whatever you would
look for it under. There is no fixed vocabulary, because the categories worth
having are the ones your work has.

#### Changing a brush and keeping the change

An **●** next to the brush name means the settings have drifted from the brush
they came from. It compares values, so putting a setting back clears it.

The **Presets** page then gives you three moves:

| | |
| --- | --- |
| **Update** | Writes your changes back over the brush you started from. |
| **Save as new** | Keeps both — the original untouched, your version under a new name. |
| **Delete** | Removes a brush you made. |

**You can update the brushes that ship with Lightbox.** Tweak Pencil, press
Update, and it stays tweaked across restarts. Nothing is lost doing it:
**Revert** gives you the original back whenever you want it, and on a shipped
brush the Delete button *is* Revert — it is not yours to delete, and "delete"
on one plainly means "give me back the one that came with the app".

Effect brushes (**Smudge**, **Blur**) swap the bar for their own controls —
strength, radius, and for smudge how much of its own colour it adds. A smudge
has no opacity in the usual sense, so showing you one would be a lie.

**Flow on an effect brush is not flow on a paint brush,** and it ships an order
of magnitude lower for that reason: Smudge 0.08, Blender 0.06, Blur 0.10. On a
paint brush flow is how much pigment a dab lays; on these it is how hard each
dab *pulls*, and because dabs overlap roughly ten deep the pulls compound along
the stroke. A value that looks like a gentle nudge on one dab is a shove by the
time ten have landed — which is why these tools used to feel impossible to
steer. If you want a stronger effect, prefer a slower hand or a second pass over
raising flow; that is what gives a smudge somewhere to go.

**These defaults are deliberately conservative and you may well want to raise
them.** They were chosen while an effect brush still stacked opacity with every
dab, which turned a pale wash opaque black. That is fixed: flow no longer touches
opacity at all — it only decides how much colour moves — and it now responds
evenly across its range rather than saturating. Measured on a wash, carried
colour runs 0, 17, 34, 47, 64 as flow goes 0.08 → 0.85, with the wash's own
opacity unchanged at every setting. Raise it until the tool feels right; it can
no longer run away from you.

Every numeric field can be **dragged sideways** to scrub its value. Hold
**Shift** for fine, **Ctrl** for coarse. Click without dragging and you get a
caret, as before.

**Shift + drag** on the canvas resizes the brush.

#### Stabiliser

The **Per brush** box beside the stabiliser decides what those controls belong
to. Off, they set one value for the whole application — how it has always
worked. On, this brush keeps its own and takes it along in its preset.

That is what the setting is actually for: an inking brush wants heavy
lazy-mouse so a long confident line comes out clean, and a pencil wants none,
because the shake *is* the texture and smoothing it makes roughs look dead.
Ticking the box copies whatever is already in effect, so it never changes how
the brush draws — only what the controls are pointed at.

#### Blend mode

On the **General** page. It decides how the finished stroke lands on the layer
— Multiply to shade, Screen to glow, and every other mode the layer docker
offers, because they are the same operation.

It is applied **once, where the stroke meets the layer**, not to each dab. So a
Multiply brush that crosses itself does not go black at the crossing, which is
almost never what you meant. The eraser ignores it: erasing takes paint away,
and no blend mode does that.

#### Choosing a tip

Also on **General**, as a grid of thumbnails rather than a list of names —
nobody knows what a "Cut nib" looks like until they have seen one. **Round** at
the top is the brush's own dab and the default. **Brush tips…** at the bottom
opens the workshop.

Painting with a tip copies it into the drawing, so the file keeps rendering
even if you later delete the tip from your library.

#### Paper texture

On the **Effects** page. Pick one of the built-in surfaces, or **Paper image…**
to use a scan or photograph of the real thing — an imported paper takes over
and the surface list goes quiet.

Two things worth knowing:

- **The image goes into the drawing, not a path on disk.** A file pointing at
  your scans folder would paint differently on somebody else's machine.
- **The grain is anchored to the canvas, not to the stroke.** Two marks
  crossing the same patch sit on the same tooth, which is what makes it read as
  paper rather than as an effect applied per stroke.

**Grain size** is how many document pixels one bit of the paper covers, and
**Depth** is how hard it bites. Depth starts at zero, so importing a paper
opens it for you — a texture you cannot see looks like a broken import.

### How the brush answers the pen

The **Pen pressure** page gives each thing pressure can drive its own curve.
Pressure runs left to right, the effect bottom to top, and the dashed diagonal
is "straight through" so you can see what you have changed.

- **Drag** a point to shape the response.
- **Click** empty space for a new point.
- **Middle-click** a point to remove it. The two ends stay — you can still drag
  them up and down.
- **Reset** puts it back to a straight line.

Seven things can be driven: **size**, **transparency**, **hardness**,
The three wet-medium brushes ship with a tip and a heading rather than a bare
circle: **Oil** uses the bristle tip turned to the stroke, **Gouache** a chisel
turned the same way, and **Watercolor** an irregular wash edge with a little
size and roundness variation and no heading at all — a wash edge is not
directional. All of that variation is seeded from where each dab lands, so a
mark is varied and still replays identically.

**scatter**, **roundness**, and for a smudge, **colour rate** and **smudge
length**. Untick one and pressure stops touching it entirely.

A curve does what no single number can. An exponent can only make the response
gentler or fiercer; it can never rise and then fall, which is what an ink brush
that spreads and then floods actually does. Draw that shape and you have it.

A brush you made before curves existed opens showing the response it already
had, not a straight line — so touching the page never quietly flattens a brush
you had tuned.

**Use pen pressure** at the top is the master switch. Off, the tablet is
ignored entirely and every curve on the page with it.

### Physical media

Watercolour, gouache, oil and ink are simulated, not imitated with a texture:
wetness, viscosity, absorbency, edge pull, pigment density, granulation, paper
grain. The simulation is **deterministic** — the same stroke always produces
the same mark, on reload, after undo, and when the inbetweener replays it.

That determinism is not a detail. An effect that varies subtly between similar
strokes looks fine on one image and *boils* at 12 fps.

**Body** and **relief** give thick paint its height. Body is how much the paint
stands up off the paper; relief is how hard the light rakes across it. Together
they are impasto — a raised edge on a gouache or oil stroke catches the light
from the upper left and shadows on the other side. The light is fixed, and
deliberately so: two strokes on one canvas must not disagree about where it is
coming from.

Each stroke is modelled from its own paint, so crossing two of them does not
yet build a ridge where they meet.

**Paint load** is how much paint the brush starts with. At 1 it never runs
out. Below that the mark begins full and fades as you draw, and at low values
it is gone within a short scrape — that is dry-brush, and it works whether or
not a medium is switched on. The length scale follows the brush size, so
resizing a brush does not change how far its paint goes.

**Wetness** is how far the paint travels. A wet mark spreads past where the
brush went, more so the longer the flow runs — a 40-pixel stroke reaches nearly
60 at full wetness. The extra room costs a little to paint, so a dry medium
does not pay for it.

**Edge pull** is the wet edge: pigment carried out to the rim of the wash as it
dries, so the mark ends up darker at its border than in the middle. At 0 the
wash dries flat. Turn it up and the border darkens and the middle pales — the
paint is being moved, not added, so a strong wet edge is paid for out of the
centre. That is what a real one costs too.

**Flow steps** decide how far the paint travels, not how much of it there is.
Turn them down for a mark that stays where you put it, up for one that spreads
and pools; the stroke carries the same pigment either way, and at zero it is
simply the mark you drew. How strong the paint is comes from **pigment
density** — a watercolour is meant to be transparent, so raise that rather than
the flow if you want a darker wash.

### Brush tips

**Edit ▸ Brush tips…** opens the tip workshop — its own window, like Configure,
because making a brush is not something you do mid-stroke.

Three pages. **Library** is what you have: your own tips, the project's above
them when a project is open, and eight built-ins below. **Generate** bakes a
shape, with only the controls that shape actually reads. **From a scan** turns a
photographed or scanned stamp into a tip: set the black and white points once
and they apply to every image in the batch, which matters because a series that
will be blended has to match exactly.

A tip is baked once and then only looked up. Nothing in this window is
recomputed while you draw.

#### The eight that are already there

| Tip | What it is |
| --- | --- |
| **Soft round** | The default. A disc with a long shoulder. |
| **Hard round** | Full to the edge, one pixel of feather. A pen. |
| **Paintbrush** | A flat brush seen head-on. Turn on *angle follows direction* and it reads as a loaded brush rather than a nib. |
| **Bristle round** | A round brush whose hairs have parted — fine scratches through a solid middle. Dry-brush without a simulation. |
| **Marker nib** | Squarish with rounded corners, the shape a chisel marker lays down. |
| **Cut nib** | Six flats and six corners. The only tip here with a point. |
| **Spatter** | Grains with a size, not fog: a sponge, a stipple, a rough charcoal edge. |
| **Wet edge** | Pale in the middle, dark at the rim — the mark a puddle leaves when it dries, stamped rather than simulated. |

Built-ins cannot be deleted or renamed, because drawings refer to them by name
under the hood. **Edit a copy** puts one back on the Generate page so you can
change it and bake your own.

#### Shapes the generator can bake

Hard circle, soft circle, ring, chisel, hatch, bristle, superellipse, polygon,
spatter and halo. Three controls do different jobs depending on the shape and
are relabelled to say which: *Count* is bristles, polygon sides or grains
across; *Sharpness* is channel depth, squareness, corner sharpness, coverage or
rim strength; *Flatness* squashes a chisel or a superellipse across its short
axis.

Two things worth knowing:

- **Painting with a tip copies it into the drawing.** Deleting it from the
  library afterwards cannot change a picture you have already made.
- **A scan whose mark runs off the crop is refused, not fixed.** A tip like
  that stamps a faint box down every stroke. Re-crop with clear paper all the
  way round.

### Fast brushes and expressive ones

Brushes come in two kinds, and the picker tells them apart:

- **Fast** — stamps dabs and stops. Predictable cost at any canvas size, and
  what almost every brush is. Pencil, ink, soft round, airbrush.
- **Expressive ◈** — reads the canvas back, simulates a medium, or blends the
  layers underneath. The mark behaves like a material instead of like paint
  being placed. Slower, particularly on a large canvas.

The **◈** marks the expressive ones, and the list is grouped so the two kinds
sit apart. Hover a brush and the tooltip names what it is paying for — "reads
the canvas back as it goes", "simulates gouache" — because that is the thing
you can turn off if you want the speed back.

It is a price tag, not a warning. These brushes exist because the coupling is
what makes a mark expressive, and an artist reaching for one has decided the
trade is worth it. The badge is so that decision is made knowingly rather than
discovered at frame 180.

Nothing about a brush *declares* which kind it is — it is worked out from the
brush's own settings, so turning the medium off moves it to the fast group and
turning it on moves it back. Every simulated medium also has a **(flat)**
counterpart that gets close to the look for none of the cost.

### Brush importers

**.abr** (Photoshop), **.gbr** / **.gih** (GIMP) and **.kpp** (Krita) import
directly. What comes across is what those formats actually carry.

### What a stroke is

**A frame is a list of strokes; the pixels are derived.** Nothing paints except
through the stroke record, so a reload renders exactly the same image, undo is
exact, and the inbetweener has real geometry to work with rather than pixels to
guess at.

The same is true of fills (a fill is a stroke with contours) and selections (a
selection is an entry in the document, referenced by the strokes clipped to it).

Settings that reach pixels — anti-aliasing, pressure curves — are recorded **on
each stroke**, so changing a preference never alters art you have already made.

### Drawing fast

A pen reports its position at a fixed rate, so the faster you draw, the further
apart the points it records. Lightbox lays the brush along the **curve** through
those points rather than along the straight lines between them, which is why a
quick arc drawn with a fat brush comes out as an arc instead of a row of flat
facets with the tops of the stamps showing on the outside of the bend.

Corners you meant are kept: turn sharply enough and the stroke stays sharp
there, so a drawn rectangle has square corners and a flick still has a point.

---

## 6. Colour

The **Color** panel offers a hue wheel with the value slider beside it, and
HSV, HSL, RGB and CMYK slider sets.

The swatch at the bottom does two things, told apart by whether you move:

- **Click** it for the numbers — hex, HSV and RGB — in a flyout.
- **Drag** it onto the canvas to fill with that colour.

### Foreground and background

Two colours, shown one over the other in the tool options bar, shared by the
brush, the fill and the gradient. **X** swaps them; **D** resets to black over
white. They are global on purpose — reaching for the same colour in three tools
and finding three different answers is what this prevents.

The swatch link travels with the swap, so trading to a palette colour and back
leaves your strokes still following that swatch.

Either half does two things:

- **Click** it to open its own picker — the same wheel, value slider, readouts
  and palette the Color panel shows, editing that half of the pair.
- **Drag** it onto the canvas to fill with that colour.

The **▾** beside the pair opens the foreground picker directly. It is there
because the swatches themselves are a press-and-maybe-drag gesture, and a hand
that moves on the way down should get a fill rather than a panel.

### Choosing a colour anywhere else

Every other place a colour is set — a palette swatch, a gradient stop, the
brush's secondary colour — is a **swatch you click**, and it opens the same
wheel, the same value slider and the same readouts.

Hex is at the bottom of that flyout, under the wheel, in the same order the
Color panel uses. It is a readout you can also type into, which is the right
rank for it: typing `#c04a2f` is transcribing a colour you already found, not
choosing one.

A checkerboard swatch means **no colour**, which is a different answer from
black. The brush's secondary colour is the one place that matters, and it has
a ✕ to get back to it.

### Keeping a colour you found

Every picker has a **＋** beside the word *Palette*. It puts the colour on the
wheel into the palette the Palette panel has selected, and makes a palette
first if the document has none — finding a colour and keeping it should be one
gesture, not a trip to another panel and back.

The new swatch is then the one you are painting with, so the stroke that
follows *references* it. A colour you went to the trouble of writing down would
otherwise be the one colour in the drawing a later palette edit could not
reach. Adding the background colour, or a gradient stop's colour, leaves the
brush where it was.

**A colour already in the palette is not added twice.** The same colour arriving
twice is almost always a slip — the wheel moved a little and came back — and a
palette full of near-identical entries is a palette nobody can use. What happens
next depends on which wheel you used:

- **Foreground or background** — the swatch already there is *selected* instead.
  That is the useful answer: the point of adding was to paint with a live
  colour, and that swatch already is one.
- **Anywhere else** — nothing is added, and it says which swatch already holds
  the colour.
- **The wheel in the Palette panel** — the copy is made. Somebody working in the
  palette who asks for a second copy wants one.

When you do want two of a colour — the same grey filed under two characters, say
— use **Duplicate** in the Palette panel. It makes an independent swatch with a
new identity, so recolouring the copy leaves art painted with the original
alone. That is the whole reason to have two.

### Palettes

Every document starts with a palette holding **pure black and pure white**,
with black selected.

This is the one place the "absent unless asked for" rule does not apply, and
deliberately. A swatch is not a feature you opt into — it is the difference
between a stroke that carries a colour and one that carries a *reference*, and
only the second can be recoloured later. Starting empty would mean the first
hour of work is painted in literals that can never follow a palette edit.

The palette appears in **every** colour picker, not just the panel. Picking
from it links the swatch, so the recolour still reaches the art.

The **Palette** panel manages named palettes. Import and export **.gpl** (GIMP)
files.

Palettes are **live**, Toon Boom style. Paint with a swatch and the stroke
remembers *the swatch*, not the colour. Edit the swatch and every stroke that
used it repaints — across every layer and every frame at once. A run of edits
collapses into one undo step.

Choosing a colour any other way breaks the link, which is what you want: a
colour picked off the canvas is a colour, not a palette entry.

In a project, palettes belong to the project, so all of a character's animations
paint from the same one.

### Filing palettes

The Palette panel's top half is a tree. **🗀** makes a folder, **＋** makes a
palette, and both land inside whatever is selected — where you were looking,
not at the bottom of the list. **✕** deletes whichever is selected.

Move things by **dragging** a row onto a folder, or by **right-clicking** it and
choosing *Assign to*. The two do the same thing; the menu lists every folder by
its full path, which is what tells two folders called "Knight" apart. Right-click
also has *Rename* and *Delete*.

Deleting a folder keeps the palettes in it — they come back one level up. A
folder can hold folders, and can sit empty: filing before there is anything to
file is the normal way round.

With a project open the tree has two headings, **Document** and **Project**, and
nothing moves between them. A document palette travels with its file and a
project palette is shared by every animation in the project, so dragging one
into the other is not filing — it is a change of ownership, and it would leave
the strokes that reference the palette pointing at nothing. Without a project
there are no headings at all, only the document's palettes.

A project's hierarchy is saved with the project, so a project you filed last
week opens filed. A document that has never had a folder carries no filing
system in its file.

### Gradients

Pick the gradient tool and its options appear in the bar, with the ramp itself
as the preview. **Click the ramp** to edit it.

The editor has two rows of markers, and they are independent:

- **Above the ramp: opacity.** Click to add a stop, drag to move it, select one
  to set its value.
- **Below the ramp: colour.** Same, and selecting one gives you the colour
  picker.

Middle-click a marker to remove it. A colour ramp always keeps two stops; an
opacity track keeps two or none, because one stop holds its value everywhere
and that is a flat opacity wearing the costume of a gradient.

The two rows exist because opacity genuinely changes in different places from
colour. A sky fading out at the top while going orange in the middle needs two
stops in one place and one in another, and tying them together would force you
to author a colour you did not want in order to place an opacity you did.

A gradient with no separate opacity track is the ordinary case and writes
nothing extra to the file.

Drag on the canvas to lay one down; the drag sets the axis, or the centre and
radius for a radial. If you have no gradient yet, picking the tool makes a
black-to-white one.

Gradients are live in the same way palettes are: edit the definition and the
art follows.

The **Gradient** panel shows the same editor, for when you want it open
permanently rather than behind a click.

---

### Guides

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

#### Grid settings

**Edit → Configure → Guides and grid** holds the cell size a new grid is made
with and how close a point has to come to a guide to be pulled onto it. It also
lists the grids already on the document, where their pitch, angle, drawing and
snapping can be changed after the fact — each one an undoable step.

Changing the default cell size never touches a grid that already exists. Once a
grid is placed its spacing belongs to the document, and a preference must not
reach back into work you have already done against it.

### Drawing on a hold

A cel that holds an earlier drawing is not a drawing of its own, so a mark on
one has two honest readings — and which you mean depends on how you work.

By default the cel **becomes a drawing of its own** and the mark lands on it.
That is what every animation tool does, and it is what makes the timeline show
a drawing where you made one. The alternative silently edits the frame being
held, so your stroke turns up on the earlier frame too and the cel you drew on
stays empty and dark.

**Edit → Configure → Timeline** switches it to *Edit the held drawing*, which
is right when the hold is deliberate and you are still working on that one
pose — touching it up without breaking the hold.

Keying is a separate undo step from the mark that prompted it: one undo takes
the stroke back and leaves the new drawing, a second takes the drawing away and
restores the hold.

### The timeline's size

**Frames** on the timeline's own bar sets how wide a frame cell is. Narrow
enough to see the shape of the timing on a two-hundred-frame scene, wide enough
to read the thumbnails on a twelve-drawing cycle — it depends entirely on what
you are doing, so it is a slider rather than a constant. The same number is in
Edit → Configure → Timeline.

### The playback range

The scrub bar carries the loop bounds. **Hover it** and two grips appear — one
at each end of the range, with a bar between them showing what will play.
**Drag either** to move that bound; it settles onto whole frames as you go, so
what you see while dragging is what you get. The two cannot cross: push one
past the other and it pins a frame away.

With no range set yet the grips sit on the ends of the whole timeline, so the
first drag narrows it rather than starting from one frame somewhere arbitrary.

**Alt-click** a grip, or anywhere between the two, to give the range back —
between them is the bigger target and the region the range is actually about.
**Right-click** the scrub bar for the same thing as a menu item.

Grabbing a bound and moving the playhead are nearly the same gesture in nearly
the same place, so the grips win where they are and clicking anywhere else on
the bar still scrubs.

The **Set playback start / end** items on a cel's context menu still work and
do the same thing; the grips are the version you can aim.

### Looping

Playback loops, because a cycle is usually what you are watching and stopping
after one pass means reaching for the button every time. **🔁** on the
timeline bar turns it off, and it then plays the range once and stops on its
last frame.

### Shapes

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

## 7. Layers

Raster and vector layers, folders, blend modes, per-layer opacity, visibility,
lock and alpha lock. Thumbnails show what is actually on the layer.

A new document opens with a locked **Background** layer holding the paper, and a
paintable layer above it. On a transparent document there is no paper layer —
just an ordinary unlocked layer.

**Ctrl+click** a layer thumbnail to select its opaque pixels.

---

## 8. Selections and transforms

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

## 9. Animating

### The timeline

One row per layer, one cell per frame. Click a cell to go there; the current one
is highlighted. A **keyed** cell holds a drawing; a **hold** repeats the drawing
before it, which is what animating on 2s and 3s is made of.

Right-click a cel for: insert frame, extend or reduce exposure, clear, delete
(which pulls the rest of the row back), copy, cut, paste, markers, and the
playback range.

Drag a cel along its row to move it. Shift-click for a range, then apply
exposure changes to all of it at once.

### Timing presets

A **timing preset** is a pattern of hold lengths — how long each drawing in a
run is held. `2` is on 2s. `1, 1, 2, 3, 4` is a slow-in: two snappy frames, then
progressively longer holds.

The picker and **Re-time** are on the timeline bar. Shift-click a range first,
or use **Re-time to …** in a cel's right-click menu to do one cel.

Applying one **re-spaces the drawings that are already there**. It never makes a
drawing and never deletes one — the worst it can do to your art is change its
timing, and one undo puts that back.

**The pattern decides the length, not your selection.** Twelve drawings put on
2s take twenty-four frames, so the row gets longer and the rest of it moves
down; the same twelve put back on 1s take twelve, and it gets shorter. The
status line says which way it went. If you want to *thin* a range — keep every
second drawing and discard the rest — that is a different, deliberately
destructive operation.

Six patterns are built in: on 1s, 2s, 3s, 4s, and a slow-in and slow-out. Behind
the **⚙** beside the picker you can save your own: type a name and a pattern
(commas or spaces, both work) and it is there next time you open the app. Your
own patterns can be deleted; the built-ins cannot.

| Compared with | |
| --- | --- |
| A **symbol** | carries *drawings*. A timing preset carries their *spacing* — the half a symbol cannot express. |
| A **template** | gives a new document its shape, once, at creation. A preset re-times drawings you already made, any time, as often as you like. |

### Onion skin

Previous drawings tint red, next drawings blue. The checkbox on the timeline bar
turns it on; the two number fields beside it are how many drawings to show
**before** and **after** the playhead, asked separately because working forwards
usually means two behind and none ahead. Ghosts sit directly under the layer they
belong to, so multi-layer onion reads correctly, and they are off during
playback — the one thing playback has to show is the animation.

Everything else is behind the **⚙** next to those fields:

| Setting | What it does |
| --- | --- |
| Mode | **Frames** ghosts this layer's other drawings. **Light table** instead dims the *other layers* at this frame, as sheets under the one you are drawing on. The paper is left alone. |
| Opacity | How visible the nearest ghost is. |
| Falloff | What each further ghost is worth. At **1** they are all equally visible — right for checking registration across a sequence. At **0.5** each is half the one before — right when you are drawing an inbetween. |
| Keyed drawings only | Step by drawings rather than by frames. On 2s or 3s the frame before is a hold of the drawing already on screen; this walks to the real one. |
| Draw over the current drawing | Ghosts on top instead of underneath. Under is how a lightbox works and is what you want while drawing; over is for checking a line you have just made against the one it should match. |
| Before / After tint | The two ghost colours. Red and blue by convention. |

A ghost counts **drawings, not frames**: on 3s the drawing three frames back is
still the *first* ghost, and gets the strongest opacity.

Per-layer, the **◉** in the Layers panel opts a layer out of ghosting entirely.

**Ghost poses.** *Pin as ghost* keeps the current frame ghosted from everywhere
else in the sequence — the extreme you are animating towards, however far away it
is. Depth cannot express that, because the distance is the point. *Clear pins*
removes them all. Pins are saved with the document; a document that never pins
anything stores nothing for it.

Onion settings belong to you, not to the artwork: they are kept across sessions,
survive rearranging or switching workspaces, and opening another document does
not reset them.

### Animation references

**View → Reference** opens a panel for importing an image of an animation — a
sprite sheet, a strip of frames, a contact sheet, a run cycle you photographed
off paper — and laying it against the timeline. The **＋** in the panel's header
picks the file.

Lightbox finds the frames in it by reading the gaps between the drawings, and
puts the first on the frame you are on, the second on the next, and so on. The
timeline grows to fit if it is shorter than the reference. If the document has
no drawing on those frames yet, that is the point: they are what you are about
to draw.

The frames it finds are **windows onto the sheet**, not crops of the drawing
inside each one. A runner who travels across their cell still travels when you
step through the reference — cropping each frame to its own artwork would put
the character in the same place every time and delete the animation.

| Setting | What it does |
| --- | --- |
| Show on canvas | The reference sits over the paper and under every drawing, like a photograph taped to a lightbox. It is never exported, and it is never in the artwork. |
| Follow timeline edits | On, inserting a frame moves the later references along with the animation, and the new frame gets no reference — you are drawing an inbetween, and there is no reference drawing for it. Off pins the reference to absolute timing, for matching a shot frame for frame. |
| Scale | One scale for every frame. Per-frame scale would put the character at a different size on each drawing, which is the one thing a size reference exists to prevent. |
| Opacity | How strongly it reads under your drawing. |

**What detection actually looks for.** Not gaps in the whole image — the
drawings themselves. It finds every connected mark on the sheet and then throws
away what is not a drawing: specks and watermarks go because they are tiny next
to a figure, and a title banner goes because a line of text is a fraction of the
height of a row of figures. What survives is grouped into rows, and each row is
cut into cells of equal width. So a page with a heading and a signature on it
works, not only a clean sprite atlas.

**When detection cannot find the frames.** Two rows that touch have no boundary
in the pixels, and no amount of looking finds one that is not there. Set **Cols**
and **Rows** and press **Apply grid** and the image is not consulted at all.
**Detect** goes back to reading it. Or edit the boxes by hand — see below.

### Editing the grid by hand

The **⊞** in the shortcut bar turns on grid editing. Every box on the sheet
appears at once, the canvas stops being a place to draw, and **Esc** leaves.
The mode is only there when a reference has been imported.

- **Drag inside a box** to move it.
- **Drag a corner** of the selected box to resize it. The box is a window onto
  the sheet, so growing it shows more of the drawing rather than scaling it.
- **Drag on empty canvas** to draw a box of your own — the way out when
  detection cannot see a boundary.
- **Delete** removes the selected box. The sheet is untouched; only the window
  goes.

Every box carries a **pivot**, drawn as a red cross: the point that should sit
still from frame to frame — the contact foot, the hips. Until you place one it
sits at the middle of the box's foot, which is where a standing figure's contact
point usually is. It is recorded on the sheet, so nudging or rescaling the
reference afterwards leaves it on the same part of the drawing.

**Generate keyframes** does two things at once, because they are one intention:
the timeline grows until every box on the sheet has a frame, and the boxes are
registered so their pivots land at the same place. An eight-frame reference on a
one-frame document is not a state anybody asked for, and neither is a run cycle
that has to be nudged into place eight times. Pressing it twice changes nothing —
everything is measured against the first box, so the sheet cannot wander off the
canvas one press at a time.

Registering on the pivot takes the travel *out* of a cycle, which is the
opposite of what tiling the sheet does, and both are wanted: tiling keeps a
runner travelling, and aligning lets you see the pose change underneath. Which
you want depends on whether you are matching the walk or the drawing, so it is a
button rather than a rule.

**Alignment.** **Align on canvas** turns dragging into lining the reference up
rather than drawing: drag to move *this frame's* reference, hold **Shift** to
move the whole sheet. The **This frame X / Y** fields do the same numerically,
and they drag horizontally like every other numeric field. Alignment is per
reference frame, because it is a property of that drawing — a frame shown twice
is lined up the same way both times. **Clear all alignment** undoes every nudge
on the sheet. Nudges are undoable.

Each reference is saved inside the document, image and all, so it cannot break
by having a file move out from under it.

### Playback

Space plays and pauses. Transport buttons step, jump and loop. Set fps and a
speed percentage. Holds resolve properly, so a drawing exposed on 3s stays up
for its full three frames.

---

## 10. Camera

**A camera is optional and absent until you add it.** A document that never
adds one shows no camera UI, serializes no camera keys and pays nothing for it.
That matters because Lightbox has two output targets and neither is the default:

- **Assets** — sprite sheets and cycles. The canvas *is* the output. No camera.
- **Shots** — the canvas is a world and the camera frames part of it. The
  deliverable is what the camera saw.

Add one from the timeline bar. Pan, zoom and roll are keyframed and interpolated;
set a key at the playhead, or clear it. **Through camera** previews the framing.

The camera is the one transform that is not view-only: it is authored, saved and
exported. It still never touches a stroke — it only decides what part of the
record a render shows.

---

## 11. AI assistance

### Inbetweens

Set the number of inbetweens and an easing, then **＋ Inbetween** interpolates
between this key and the next. Because a frame is a stroke record, the
inbetweener matches *strokes*, not pixels.

### AI drawing

**✦ AI Draw** paints onto the current frame from a prompt. **✦ AI Inbetween**
asks the model for the frames between two keys. Both need a provider; until one
is chosen the AI controls are disabled and say where to choose it.

Anything the AI produces arrives as ordinary strokes — undoable, editable, and
subject to every rule your own strokes are.

### Turning AI off

**Edit ▸ Configure ▸ AI ▸ Use AI assistance.** On by default. Off removes the
AI bar rather than greying it out — a row that can never do anything is worse
than no row.

Everything below the switch keeps working while it is off, so a provider can be
set up and tested before AI is turned on. That is the useful order, and
refusing to test until the switch is on would invert it.

### Choosing a provider

**Edit ▸ Configure ▸ AI.** Pick a service from the dropdown and the fields
below it change to what that service needs — a key and a model for a hosted
one, a URL for a local one, a command line for an agent of your own.

| Provider | What it needs | Notes |
| --- | --- | --- |
| **Claude (Anthropic)** | API key, model | What Lightbox is tuned against, and the strongest inbetweener here. |
| **GPT (OpenAI)** | API key, model | Strict JSON schema, so replies parse by construction. |
| **OpenRouter** | API key, model | One key for many vendors' models. |
| **Ollama** | Model | Local, no key, no network. Weaker inbetweens — good for working offline. |
| **Custom (OpenAI-compatible)** | Endpoint, model | LM Studio, vLLM, llama.cpp's server, your own gateway. The key is optional. |
| **Custom agent (MCP)** | Command, tool | An MCP server you supply that owns the model. |

### Testing it

**Test connection** draws rather than pings, because most of the ways this
fails are not reachability. There are two depths:

| | What it does | Cost |
| --- | --- | --- |
| **Quick test** | Asks for one short line on a small canvas | Seconds; a few hundred tokens |
| **Test with a drawing** | The quick test, then a real inbetween between two keyframes | Minutes on a local model |

Both check that the *output* is usable, not just that it parsed: strokes with
fewer than two points, or every point in the same place, are reported as a
problem rather than counted as a pass. The thorough test adds the one check
that separates a working connection from a working inbetweener — the frame it
returns has to land **between** the two keys. A small model that answers in
perfect JSON and copies a keyframe fails there, and nowhere else.

The verdict comes in three colours, because "unreachable" and "reachable but
drawing nonsense" need different fixes:

- **Green** — connected, and what came back is usable.
- **Amber** — connected, but the output is not usable. The connection is fine;
  the model may be the wrong one.
- **Red** — nothing answered, or the key, endpoint or tool name is wrong.

A test shows a progress bar and an elapsed clock while it runs, and says which
stage it is on. Past two minutes it says so explicitly rather than sitting
silent, and **Cancel** stops it — a thorough test against a local model
genuinely takes that long, and silence for that long is indistinguishable from
a hang.

A field left empty is not necessarily unset. Its placeholder says what it
resolves to: a default, or an environment variable that is already supplying it
(`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `OPENROUTER_API_KEY`,
`LIGHTBOX_OLLAMA_URL`). What you type wins over the environment, which wins
over the default — and only what you type is saved, so a rotated key is not
shadowed by a stale copy.

Changing provider takes effect immediately; there is no restart and no Save
button.

### An agent of your own, over MCP

The **Custom agent (MCP)** provider launches a server you name and calls one
tool on it:

```
tools/call { name: <tool>, arguments: {
  system: string,   // the role and the rules
  prompt: string,   // the task, with the keyframes as JSON
  schema: object    // JSON Schema the reply must match
}}
→ { content: [{ type: "text", text: "<json matching schema>" }] }
```

Anything behind that contract works — your own agent, your own retrieval, a
model with no public API. If the tool is named something else, Test connection
lists the tools the server actually offers.

This is the opposite direction from Lightbox's *own* MCP server, and the two
are independent. Here Lightbox calls out to a model. There, an agent you
already run calls in and works the document directly — no provider needed on
this page at all.

---

## 12. Saving, exporting and recovery

| Command | What it does |
| --- | --- |
| **Save** | Writes in place. With a project open, writes the project and only the documents that changed. |
| **Save as…** | Picks a new path. |
| **Export document…** | Writes a standalone `.lightbox.json` with every referenced swatch, gradient, brush tip and clip region **inlined**. |
| **Export PNG / sequence / sprite sheet** | Frames as images; sheets with trimmed bounds and a pivot. |

**Export document** is the escape hatch that matters. Inside a project, a
document refers to shared resources by id, so a lone file is no longer
self-contained. Export inlines them, and the exported file renders identically
with the project gone — which is checked by comparing pixels, not shapes.

### Autosave

Under **Edit**. Choose off, 30 seconds, 1, 5 or 15 minutes. Zero is a real
answer, not a mistake to guard against.

Autosave writes a **recovery copy** to your app data folder, not over your file.
Recover by opening it. If you would rather it wrote over the real file too,
there is a checkbox — off by default, because silently rewriting the file you
opened takes away the ability to close without saving.

---

## 13. Symbols

A **symbol** is a drawing stored once and placed many times. Edit the sword,
and every animation holding it changes — that is the whole point, and it is why
a placement refers to the symbol rather than copying it.

Symbols belong to the **project**, not to one animation, because a prop lives
above the animations that use it. **View → Symbols** opens the panel; it does
nothing without a project open, so it is not offered until there is one.

### Making one

Draw something, then **Make symbol** in the panel's footer and give it a name.
The strokes leave the drawing and a placement of the new symbol takes their
place. Nothing about the picture changes at that moment — the mark is the same
mark, in the same position.

### Placing, moving, and letting go

- **Place** puts the selected symbol in the middle of the current drawing.
- **Dragging a tile onto the canvas** puts it where you drop it, which is the
  point of dragging rather than pressing Place.
- The **Move tool** drags a placement the way it drags anything else. A placed
  symbol under the cursor is picked up before the drawing underneath it is; the
  symbol itself is not touched, so the other placements of it stay where they
  are. Hold Shift to keep the move to one axis.
- **Break link** turns a placement back into ordinary strokes on that drawing.
  It is the honest way to get something you can edit stroke by stroke, and it
  is a one-way door: the result is a drawing, not a symbol.

A placement can be moved, scaled, rotated, faded and time-offset. It cannot
have one of its strokes nudged — that would be a different drawing, and
pretending otherwise is how a symbol quietly becomes a copy.

### Finding one

The panel filters by **kind** — prop, pose, expression, hand, face, FX,
background — and searches names *and* tags. Tags are a plain comma-separated
line rather than folders, because a sword is a prop, and it is also "knight",
and also "act two"; filing it once makes the other two searches fail.

### Cycles

A symbol can hold several frames — a walk, a flicker, a blink. A placement of
one advances with the timeline, and its **frame offset** shifts where in the
cycle it starts, so one stored walk can carry two characters half a stride
apart.

### Editing one

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

### Knowing what changed under you

When a symbol is edited, placements made before the edit are marked as such.
Nothing is broken — they already show the new drawing — but the app can tell
you *which* of the drawings in front of you changed while you were elsewhere.
**Acknowledge**, in the Symbols panel, clears the marks; it changes nothing
about the picture. The bar is not there at all when there is nothing to
report.

There is deliberately no "put it back the way it was when I placed it". The fix
for an edit nobody wanted is to undo it in the symbol, once — not to pin two
hundred placements to old copies of it.

### Deleting, and exporting

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

### What smudge and blur read

Smudge and blur move pixels that are already there rather than laying down
colour. **Edit → Configure → Drawing** decides which pixels:

| | |
| --- | --- |
| **This layer** | Only the layer you are painting on. The default. |
| **All layers (baked)** | Everything you can see, frozen as it was when you made the mark. |
| **All layers (live)** | Everything you can see, and it keeps following. |

The setting applies to the *next* mark. Every stroke remembers what it was made
with, so changing this never alters something already drawn.

The difference between the two shows up later. Repaint the background under a
**live** smudge and the smudge re-blends against the new background; a **baked**
one keeps the colours it picked up when you made it. Baked is what you want once
a mark is finished and you would rather nothing touched it again; live is what
you want while a painting is still moving underneath you.

Changing brush does not reset this. It is a setting, not part of a brush.

A live smudge on the bottom layer has nothing to follow, so it reads its own
layer, exactly as **This layer** would.

### Where the brush lives

**Edit → Configure → Drawing** also decides whether the brush belongs to the
tool or to the work:

| | |
| --- | --- |
| **Follow the project** | The default. Illustration, comic, game art and asset libraries keep the brush with the project; animation and storyboards keep one brush for the tool. |
| **Global** | One brush, carried between projects and sessions. What Photoshop and Krita do. |
| **Per project** | The project remembers the brush you paint with and gives it to every document in it. |

The point of **per project** is the break between sessions. Come back to a
comic or a set of game assets after a fortnight and the tool bar says whatever
you last used on something else — but the work was drawn with something
particular, and where the character of the stroke is part of the style that
matters.

The project rather than the file, because the answer has to reach the pages
that do not exist yet: page one remembering its own brush would leave page
eleven starting from scratch. It is the same reasoning as the shared palette —
a character's work has one set of colours and one set of marks.

It is recorded when you make a mark, not when you save, so a session that ended
without saving still remembers. A project with nothing recorded — an older one,
or one worked on under **Global** — leaves your brush alone rather than
resetting it. With no project open there is nowhere to keep a brush, so the
setting reads as **Global** whatever it says. Strokes an AI or an agent adds
never change it: they are not what *you* were painting with.

## 14. When the canvas feels slow

The info strip along the bottom reports what the app is actually doing:
how much memory the rendered frames are using, and whether the canvas is being
put on screen by the **GPU** or by the **CPU (software)**.

Software means no graphics context was available — usually an out-of-date
driver, a virtual machine, or a remote desktop session. It is not a small
difference. Showing the canvas means rescaling the whole document for every
frame, and in software that becomes the most expensive thing the app does,
outweighing the drawing itself.

**Canvas quality** is the lever, in **Edit → Configure → Performance**:

| | |
| --- | --- |
| **Display** | Matches the screen — full detail zoomed in, less zoomed out. The default. |
| **Full** | Always the document's own resolution. Sharpest, slowest. |
| **Half** | Half of what the screen shows. Softer while you work, fastest. |

It only changes what you see while working. **The drawing, the exports and the
thumbnails are always full resolution**, whatever this is set to.

### When the app changes it for you

If the canvas cannot keep up, Lightbox turns the quality down to Half once and
says so in the status line. That happens when the graphics backend comes back
as software, and also when the measured frame time says the canvas is
struggling — which catches a machine that has a GPU and is still too slow, on
a big canvas with a lot of onion skin.

It will not do this if you have set a canvas quality yourself. Choosing one —
including choosing Display, the default — settles the matter, and the app will
not overrule it however slow the machine gets. You can change it back at any
time in Configure, and it will stay changed.

**Measured now**, at the bottom of that page, shows the current cost per
repaint and per frame, the headroom left, and what is worth changing about the
document if there is none.

## 15. Keyboard

Every shortcut is editable in **Edit → Configure…**, which is searchable.
Shortcuts are context-aware: the same key can mean different things over the
canvas, the timeline and the Layers panel.

| Key | Action |
| --- | --- |
| B / E / S | Brush, Eraser, Select (press again to cycle variants) |
| Ctrl (hold) | Pick a colour |
| Ctrl+Z / Ctrl+Y | Undo, redo |
| Ctrl+T | Transform |
| Ctrl+A / Ctrl+D / Ctrl+Shift+I | Select all, deselect, invert |
| Space | Play / pause |
| ← / → | Previous, next frame |
| X / D | Swap foreground and background / reset to black over white |
| M | Mirror the view |
| 0 | Reset zoom, rotation, mirror and pan |
| Shift + drag | Resize the brush |
| Wheel / Shift+wheel | Zoom / rotate the view |

Zoom, rotation, mirror and pan are **view-only**. They never touch the document.

---

## 16. Working with an agent (MCP)

Lightbox runs an MCP server, so an agent can work the document directly: read
the scene, add strokes and frames, request inbetweens. Everything it does goes
through the same stroke record as everything else, so its work is undoable and
indistinguishable in kind from yours.

---

## 17. Planned

Not built. Listed so the gap is visible rather than implied.

**Projects and workflow**
- Scene management
- Project conversion (Illustration → Animation → Game) with no artwork recreated
- Linked characters that update everywhere when edited, rather than copied

**Drawing**
- Pixel-perfect mode
- Brush symmetry
- Perspective rulers, vanishing points, grid and snapping
- Shape tools and vector guides
- Pattern fills

**Layers**
- Layer masks, clipping masks, adjustment layers
- Non-destructive filters

**Editing**
- Liquify, clone stamp, healing brush

**Animation**
- A pose library, and reusable animation cycles

**Interop**
- PSD import and export

**The application**
- This manual, in the app
