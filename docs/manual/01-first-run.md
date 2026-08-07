# Getting started

## First run

While Lightbox loads, a plain orange panel with the application's name sits in
the middle of the screen. It stays for about half a second and then the main
window replaces it. It is deliberately plain — a placeholder, not a design —
and it appears once Lightbox is far enough along to draw anything at all, so on
a cold start there is still a moment before it shows up.

Lightbox then opens on an untitled document — 960 × 540 at 12 fps, on white paper —
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

## The window

From the top:

| Strip | What it is |
| --- | --- |
| Menu | File, Edit, View |
| Tool options | Controls for the tool you have selected. Changes with the tool; never changes height, and never scrolls — anything that does not fit goes into the **▾** at the end. On the right, the workspace picker. |
| AI bar | AI Inbetween, and what the model is doing |
| Tabs | One per open document |
| Work area | Tool column, canvas, and whatever panels you have docked |
| Info strip | Document size, layer and drawing counts, and how much headroom the machine has |

### Panels

Eight panels: **Project**, **Layers**, **Color**, **Reference sheets**,
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

## Workspaces

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
