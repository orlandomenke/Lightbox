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
| Title bar | The Lightbox mark, the menu, and the window's own minimise / maximise / close. Drag the empty part to move the window; double-click it to maximise. |
| Menu | File, Edit, View — lives in the title bar |
| Tool options | Controls for the tool you have selected. Changes with the tool; never changes height, and never scrolls — anything that does not fit goes into the **▾** at the end. On the right, the workspace picker. |
| AI bar | AI Inbetween, and what the model is doing |
| Tabs | One per open document |
| Work area | Tool column, canvas, and whatever panels you have docked |
| Info strip | Document size, layer and drawing counts, and how much headroom the machine has |

### Panels

The panels — **Project**, **Layers**, **Color**, **Palette**, **Gradient**,
**Channels**, **Reference sheets**, **Reference**, **Symbols**,
**Tool options**, **Timeline**, **X-sheet** and **Graph editor** — open and
close from **View ▸ Dockers**, where every panel toggle lives in one submenu.

Each panel's header is three things at once:

- **A title** — or **tabs**, when more than one panel shares the slot. Click a
  tab to bring that panel forward. No panel is ever open twice, so "where is the
  palette" always has one answer.
- **A grip — and it is the tab.** Drag a panel by its tab, the way you would
  anywhere else. The rest of the header is not draggable: pressing the empty
  space beside the tabs does nothing, which is deliberate, because that space is
  there to give the title room rather than to be the biggest target in the
  panel.
- **A close button.**

**Panels share a slot by being dragged onto each other's headers.** Drop a panel
on another panel's header and the two become tabs in one slot; drop it on the
body, or on an edge, and it gets a slot of its own as before. Dragging the last
tab out of a group leaves the remaining panel an ordinary panel again — a group
is nothing more than the panels currently sharing a slot.

Tabbing is how a workspace offers more than it has room for. Colour, palette,
gradient and channels ship tabbed together in every built-in arrangement,
Default included: they are ways of answering one question and you want one at
a time, so they cost one slot between them instead of four. Things you use *at
the same time* — the layers list, the project tree, the timeline — are never
tabbed together, and dragging one there is the only way to make it so.

**A side holds at most four slots.** A drop that would open a fifth strip
lands as a tab in the nearest slot instead — nothing is refused, and the panel
is where you dropped it, just tabbed. Reopening a closed panel follows the
same idea: it goes back to the group you closed it out of, or joins its family
(the colour panels, the timeline views) if it has never been placed by hand.
A panel whose whole family is closed opens alone, and the family finds it as
its members reopen.

While you drag, two things tell you what is about to happen. A small **ghost**
follows the pointer naming the panel you are carrying, and a **highlight** shows
where it would land:

- Near an **edge with nothing on it**, the highlight grows to the size of the
  area that would open.
- Over an **existing panel** — header or body — the highlight is the whole
  panel: let go and the two become tabs.
- At a panel's **very top or bottom edge**, a slim band shows where the panel
  would slot in between its neighbours instead.
- Let go **over the canvas** and the panel floats in a window of its own. Drag
  its tab back to a dock zone to put it away again.

Every panel also has a **⧉ button** beside its close button: it floats the
panel where it stands. On a floating panel the same button reads **⇱** and
docks it back where it came from. The timeline has neither — it never leaves
the bottom.

The ghost stays inside the main window: drag a floating panel out past the edge
and it stops at the boundary rather than following onto the desktop.

Dragging the last panel out of an edge collapses that edge — no empty gutter.

A sidebar never scrolls: its panels share the height, shrinking together as
more arrive, and each panel's content scrolls inside it while its bars stay
put. Tab panels together when a side gets crowded.
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
picker at the top right of the Quick options bar.

- **View → Workspace → Save current workspace** updates the selected one.
- **Save as new workspace…** stores the arrangement under a new name.
- **Reset to saved** discards your changes.

Saved workspaces carry a bin in the picker. The built-ins do not: a built-in is
what *reset* falls back to, so deleting one would take the fallback with it.
Saving over a built-in forks it instead of overwriting it.

The picker marks a workspace you have since rearranged with a `*`.

### The quick bar is the workspace's

What the Quick options bar carries is part of the workspace, chosen for the
work rather than fixed: the **Animation**, **Game art** and **Storyboard**
workspaces put the transport (play/pause) and frame buttons at eye level, the
single-image workspaces carry the paint kit and the marquee, and **Default**
shows every tool's own group. Two things never move whatever the workspace
says: **Size** and **Opacity** stay pinned on the bar's left.

The **⋮** button beside the workspace picker chooses the contents — tick and
untick what this workspace offers. The choice behaves like any other
workspace edit: the picker marks it with `*` until you save, *reset* undoes
it, and a saved workspace remembers it. A tool-bound group you carry still
shows only while its tool is in hand — carrying *Fill options* does not pin a
dead strip to the bar all day. Everything you untick stays reachable in the
Tool options panel, which always has the full vocabulary.

When you create a project, you are asked whether to keep the arrangement you
are in or take that project type's defaults. It is a question at that moment,
not something the project remembers.

---
