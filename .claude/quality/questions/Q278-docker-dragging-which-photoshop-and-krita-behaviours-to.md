# Q278 · Docker dragging: which Photoshop and Krita behaviours to adopt — **answered 2026-10-15**

Raised by the owner, 2026-10-15: *"Analyse how Krita and Photoshop handle their
dockers. … Dragging and rearranging still feels not fully intuitive."*

The comparison, read against `DockZones`, `Docker` and the drag host in
`MainWindow.Workspace.cs`:

| | Photoshop | Krita | Lightbox before this |
| --- | --- | --- | --- |
| Move a whole group | Drag the empty strip beside the tabs | Drag the title bar | Not possible: the empty header is inert, so the colour group is four drags |
| Cancel a drag | Esc | Esc | No cancel: every release docks or floats |
| Top of a strip | Reachable | Reachable | Unreachable. Q130 chose a band above the first header, and it was never built |
| Tab into a floating panel | Yes, floats hold groups | Yes | No: a float holds one panel and is not a drop target |
| Right-click a tab | Close / Close group / Minimize | Every docker, ticked | Nothing (Q276) |
| Crowded column | Collapse a group / icon rail | Tabs | Four short slots (Q277) |

What it blocks: nothing built; each is its own branch.

| | What it costs |
| --- | --- |
| **Move a whole group** by pressing the empty header (**chosen**) | Reverses the earlier rule that the empty header is inert. That rule kept the biggest target from being a grip, and dragging a group needs exactly that target. |
| **Esc cancels a drag** (**chosen**) | Small; the drag host already tears down cleanly. |
| **Build Q130's band** (**chosen**) | Already decided; only the work remains. |
| Tab into floating panels (not chosen) | The largest: `FloatingPanelWindow` holds one `Docker`, and a float would need slots of its own. |

## What was decided

- Build **group drag**, **Esc cancel** and the **Q130 band**, one branch each.
- **Floating groups are not built**, by choice rather than for want of time.
- **Order:** the header menu (Q276) first, then collapsible slots (Q277), then
  these.

## Amended 2026-10-15: a ⠿ grip as well as the bare header

Building the group drag showed that the bare header often does not exist. Tabs
fill the header at the default sidebar width, and the colour group's overflow it
(Q281). The group most worth moving whole had nothing to grab. Asked again, with
the recommendation taken:

| | What it costs |
| --- | --- |
| **A ⠿ grip, plus the bare header** (**chosen**) | Every group can always be moved, by the glyph the canvas bars are dragged by. About 14 px of tab room in every header. |
| Bare header only | No pixels, but the colour group cannot be dragged whole at the default width. |
| Alt+drag a tab | No pixels, and nothing on screen says it exists. |

Two consequences, not asked: a group let go over nothing **stays where it was**,
because a float holds one panel (floating groups were declined above), and a
group holding the timeline has no grip, because the timeline never leaves the
bottom.

