# Q213 · Ctrl+click on folders: a set of folders, toggled, and the thumbnail as a canvas selection — **answered 2026-10-07**

**Answered: Ctrl+click picks folders the way it picks layers.** A Ctrl+click on
a folder already picked takes it back out, and several folders can be picked
at once, each one lit. With folders picked, the docker's verbs act on the
*folders*: Delete removes them with their contents, and the eye and the lock
cover every picked folder. Both parts follow the recommendation.

Raised by the owner, 2026-10-07, after several fixes in this area were reported
landed: *"After multiple requests that still does not work with shift/ctrl +
click."* Their own capture (alpha.117, a mouse, `diagnostics.log`) was read
before asking. Ctrl+click on four folders in turn only ever grew the selection,
a Ctrl+click on a folder already picked changed nothing, and no folder header
ever carried the `selected` class.

## The answers

1. **Ctrl+click on a picked folder removes it** (recommended; the alternative,
   add-only, meant Ctrl+clicking a folder's layers out one at a time). The owner
   added: *"Like photoshop; the thumbnail would select. Shift/CTRL + click on the
   Thumbnail would add to the selection. But otherwise it will remove it."*
   A follow-up confirmed what that meant: Ctrl+click on a layer's thumbnail
   makes a canvas selection from its drawing and Ctrl+Shift adds to it, while
   Ctrl+click anywhere else on the row toggles the row. **That thumbnail
   behaviour already exists** (`OnLayerThumbPressed` → `SelectLayerAlpha`:
   Ctrl selects, Shift adds, Alt subtracts), so no new work came out of it. The
   manual now points it out next to the row gestures.
2. **The folders themselves are picked** (recommended), rather than only their
   layers with the folders lit (smaller, but Delete would leave empty folders
   behind) or the layers alone (only the toggle fixed, and nothing on screen
   for a collapsed folder).

## Implementation choices inside the answers

- **"Picked"** means picked on its header, or every layer in it selected one at
  a time. Both count, so Ctrl+click takes either back out.
- **The selection is never empty.** The only picked folder stays picked under
  Ctrl+click, as the only selected layer does.
- **A layer Ctrl+clicked out of a picked folder unpicks the folder** (and every
  picked folder around it), because the folder is no longer whole. A
  Ctrl+click on a layer *into* the pick keeps the picked folders. Any other
  click starts again.
- **Delete with folders picked is one undo step** for every picked folder and
  every picked layer outside them. A lock anywhere in the pick (a picked layer,
  or in or above a picked folder) refuses the whole delete and names it.
- **A Shift range picks a folder header only when it holds the whole folder**
  (or the folder is collapsed, when the header stands for it). A range that
  crosses a header part-way through its layers selects those layers, not the
  folder, because Delete takes a picked folder whole.
- **A folder outside the pick is acted on alone**, as a layer row outside the
  selection is.

## Found alongside, and asked separately

The owner then wrote: *"I am not seeing any selection expanded on CTRL or SHIFT
+ click. Not on folders like you confirmed. But also not layer."* The trace
showed the layer selection *was* there. The dark theme drew a selected row at 4%
white, fainter than the 6.5% hover. That is B400, with its own answer: a bar
plus a 10% fill.
