# Q277 · Crowded sidebars: collapsible slots rather than a lower cap — **answered 2026-10-15: collapsible slots**

Raised by the owner, 2026-10-15, after the docker comparison with Krita and
Photoshop: *"Another thing is the overview of the content. Perhaps that is due
to being able to have up to 4 rows."*

What is there: a side holds up to four slots (Q73), and they share its height
evenly (a sidebar never scrolls). Four groups means four short panels, each
scrolling inside itself, and nothing on screen says which one is getting the
room. Photoshop's answer is collapse: a group you are not using shrinks to its
tab strip and hands its height to the others. Its further step is a whole
column collapsed to icons.

What it blocks: nothing that is built. It is a new per-slot state and a change
to how `DockStrip` shares height.

| | What it costs |
| --- | --- |
| **Collapsible slots** (recommended, **chosen**) | Double-click a tab (or a chevron) to collapse the slot to its header; the others share the freed height; the state is saved with the workspace. The four-slot cap stays, and every group's name stays readable. Cost: one per-slot field in the placement record, written only when set (optional means absent), and the strip's height sharing learns about it. |
| Lower the cap to 3 | One constant. Each slot gets about a third more height, but the shape of the problem is unchanged: a panel you are not using still takes its share. |
| An icon rail per side | Saves the most space. The largest build, it needs an icon for every panel, and an artist can no longer read the panel names at a glance. |

## What was decided

- **Slots collapse to their header** (double-click a tab, and the header menu
  from Q276), and the collapsed state is part of the saved arrangement.
- Built after the header menu, on its own branch.

## Consequences found while building it

- **No splitter beside a collapsed slot.** A grid splitter resizes the two rows
  it sits between, and dragged against a header-height row it turns that row
  into a fixed one. So two open panels with a collapsed one between them have
  no splitter between each other. Not asked: the cost is one extra click (open
  the middle one), and the alternative is a splitter that resizes across a row,
  which the grid does not do.
- **The front tab of a collapsed slot stays as drawn** (asked 2026-10-15,
  recommendation taken). It is drawn rising into a panel that is not there,
  but it is the one cue for which panel expanding will show.
