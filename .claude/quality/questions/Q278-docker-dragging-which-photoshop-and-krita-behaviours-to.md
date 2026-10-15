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
