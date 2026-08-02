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
13. [Keyboard](#13-keyboard)
14. [Working with an agent (MCP)](#14-working-with-an-agent-mcp)
15. [Planned](#15-planned)

---

## 1. First run

Lightbox opens on an untitled document — 960 × 540 at 12 fps, on white paper —
with a brush selected. You can draw immediately. Nothing has to be created,
named or configured first.

That is deliberate, and it is the rule the whole application follows:
**optional means absent, not disabled.** A project, a camera, a palette, a
gradient and five of the seven panels do not exist until you ask for them, and
until then they cost you no screen, no keys and no thinking.

---

## 2. The window

From the top:

| Strip | What it is |
| --- | --- |
| Menu | File, Edit, View |
| Tool options | Controls for the tool you have selected. Changes with the tool; never changes height. On the right, the workspace picker. |
| AI bar | Inbetween, a prompt box, and AI Draw |
| Tabs | One per open document |
| Work area | Tool column, canvas, and whatever panels you have docked |
| Info strip | Document size, layer and drawing counts, and how much headroom the machine has |

### Panels

Seven panels: **Project**, **Layers**, **Color**, **Character sheets**,
**Palette**, **Gradient**, **Timeline**. Open and close them from **View**.

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

The tool options bar carries the controls you reach for constantly — preset,
size, hardness, opacity, stabilizer. **⚙** opens every parameter, grouped:
General, Effects, Medium, Pressure.

Effect brushes (**Smudge**, **Blur**) swap the bar for their own controls —
strength, radius, and for smudge how much of its own colour it adds. A smudge
has no opacity in the usual sense, so showing you one would be a lie.

Every numeric field can be **dragged sideways** to scrub its value. Hold
**Shift** for fine, **Ctrl** for coarse. Click without dragging and you get a
caret, as before.

**Shift + drag** on the canvas resizes the brush.

### Physical media

Watercolour, gouache, oil and ink are simulated, not imitated with a texture:
wetness, viscosity, absorbency, edge pull, pigment density, granulation, paper
grain. The simulation is **deterministic** — the same stroke always produces
the same mark, on reload, after undo, and when the inbetweener replays it.

That determinism is not a detail. An effect that varies subtly between similar
strokes looks fine on one image and *boils* at 12 fps.

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

---

## 6. Colour

The **Color** panel offers a hue wheel with the value slider beside it, and
HSV, HSL, RGB and CMYK slider sets.

The swatch at the bottom does two things, told apart by whether you move:

- **Click** it for the numbers — hex, HSV and RGB — in a flyout.
- **Drag** it onto the canvas to fill with that colour.

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

### Palettes

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

### Gradients

The **Gradient** panel edits gradients — stops, linear or radial, pad/repeat/
mirror. Drag on the canvas with the gradient tool to lay one down; the drag sets
the axis (or the centre and radius, for radial). If you have no gradient yet,
picking the tool makes a black-to-white one.

Gradients are live in the same way palettes are: edit the definition and the art
follows.

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

### Onion skin

Previous frames tint red, next frames blue. Adjustable depth, per-layer opt-out,
and off during playback. Ghosts sit directly under the layer they belong to, so
multi-layer onion reads correctly.

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
asks Claude for the frames between two keys. Both need an API key
(`ANTHROPIC_API_KEY`); a local Ollama model works too. Without either, the AI
controls are disabled and say why.

Anything the AI produces arrives as ordinary strokes — undoable, editable, and
subject to every rule your own strokes are.

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

## 13. Keyboard

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
| M | Mirror the view |
| 0 | Reset zoom, rotation, mirror and pan |
| Shift + drag | Resize the brush |
| Wheel / Shift+wheel | Zoom / rotate the view |

Zoom, rotation, mirror and pan are **view-only**. They never touch the document.

---

## 14. Working with an agent (MCP)

Lightbox runs an MCP server, so an agent can work the document directly: read
the scene, add strokes and frames, request inbetweens. Everything it does goes
through the same stroke record as everything else, so its work is undoable and
indistinguishable in kind from yours.

---

## 15. Planned

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
- Persistent onion-skin settings across sessions
- Onion skin from keyframes only

**Interop**
- PSD import and export

**The application**
- This manual, in the app
