# Getting started

## First run

While Lightbox loads, a plain orange panel with the application's name sits in
the middle of the screen. It stays for about half a second and then the main
window replaces it. It is deliberately plain — a placeholder, not a design —
and it appears once Lightbox is far enough along to draw anything at all, so on
a cold start there is still a moment before it shows up.

Lightbox opens with **nothing open**, and says so: an empty workspace with
*New…*, *New project…* and *Open…* in the middle of it. Nothing is created
until you choose a canvas — which means the canvas you choose is the canvas
you get, rather than an untitled 960 × 540 that arrived on its own and adopted
your first strokes.

A **start screen** appears over that offering three things: the New file
fields, the New project fields, and what you had open last. **Escape** declines
it — you are on the empty workspace, and the same choices are waiting on it and
under **File**. *Edit → Ask what to open on start-up* turns the screen off and
back on; the empty workspace's buttons are not affected by it.

Closing the last tab returns you to the same place: the workspace empties and
the same what-to-open question is asked once, rather than a fresh untitled
document being invented for you. Closing an untouched blank never argues about
unsaved changes — there is nothing in it to lose.

Double-click a recent entry to open it, or select one and press Create. Recent
holds files and projects together, newest first — "what was I working on" does
not sort itself by kind. The same list is under **File → Open recent**, with
*Clear the list* at the bottom. Anything you open or save for the first time
joins it; anything that has since moved is simply not offered.

The empty start is deliberate, and it is the rule the whole application
follows:
**optional means absent, not disabled.** A document, a project, a camera, a
palette, a gradient and six of the eight panels do not exist until you ask for
them, and until then they cost you no screen, no keys and no thinking.

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

- **A title** — or **tabs**, when more than one panel shares the slot. Click a
  tab to bring that panel forward. No panel is ever open twice, so "where is the
  palette" always has one answer.
- **A grip.** Press and drag the header to move the panel.
- **A close button.**

**Panels share a slot by being dragged onto each other's headers.** Drop a panel
on another panel's header and the two become tabs in one slot; drop it on the
body, or on an edge, and it gets a slot of its own as before. Dragging the last
tab out of a group leaves the remaining panel an ordinary panel again — a group
is nothing more than the panels currently sharing a slot.

Tabbing is how a workspace offers more than it has room for. Colour, palette and
gradient ship tabbed together in most arrangements: they are three ways of
answering one question and you want one at a time, so they cost one slot between
them instead of three. Things you use *at the same time* — the layers list, the
project tree, the timeline — are never tabbed together, and dragging one there
is the only way to make it so.

While you drag, two things tell you what is about to happen. A small **ghost**
follows the pointer naming the panel you are carrying, and a **highlight** shows
where it would land:

- Near an **edge with nothing on it**, the highlight grows to the size of the
  area that would open.
- Over an **existing panel**, the highlight is a band above or below it (or
  left/right in a top or bottom strip) showing where it would slot in.
- Over an **existing panel's header**, the highlight is that header — let go and
  the two become tabs.
- Let go **over the canvas** and the panel floats in a window of its own. Drag
  its header back to a dock zone to put it away again.

The ghost stays inside the main window: drag a floating panel out past the edge
and it stops at the boundary rather than following onto the desktop.

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
