# Q204 · Layer folders: can a folder be empty, nest, and how is one created

Raised by: the owner, 2026-10-07 — "Empty folder can exist in the layers
docker. Currently a new folder always needs content or if a layer is selected
overwrites the pre-existing folder. A folder is just an container object for
grouping layers. It can be empty it can be selected separately and it is always
created above the current selection or at the top. But can be rearranged just
like layers."

What it blocks: the folder model itself. Today a `LayerGroup` has no position
of its own — its header is drawn where its first member sits in
`Scene.Layers`, members are tied to it by `Layer.GroupId`, and folders do not
nest. So an empty folder has nowhere to be drawn and vanishes from the docker
(it stays, invisible, in `Scene.LayerGroups`); and **New folder** wraps the
active layer, which pulls a layer already in a folder out of it and can empty —
and so hide — the old one. That is the "overwrites the pre-existing folder" in
the report.

## Asked, with the recommendation and the cost of each alternative

1. **Where does a folder hold its place?** Recommended: *anchor when empty* —
   folders with members stay member-positioned, an empty folder records the
   layer it sits above, written only while empty; small, no migration.
   Alternative: *folders join the stack* — one ordered list of layers and
   folders; cleanest, and the shape nesting would need; costs a format change,
   a migration and every reorder operation, several days.
2. **New folder with the selection already inside a folder.** Recommended:
   *a sibling above that folder*, no nesting. Alternative: *nested inside it* —
   needs the stack model and nesting throughout (docker tree, inherited
   visibility and lock, drop targets).
3. **Does New folder still wrap the selected layers?** Recommended: *split in
   two* — New folder is always empty; a separate "Folder from selection"
   (Ctrl+G) keeps the wrap. Alternative: always empty, grouping by dragging.

## Answered 2026-10-07 — against the recommendation on 1 and 2

1. **Folders join the stack.** A folder is an item in the stack in its own
   right, with a position whether or not it holds anything.
2. **Nested.** New folder with the selection inside a folder creates the new
   folder *inside* that folder, above the selection.
3. In the owner's words: **"I want to emulate what krita and photoshop does."**
   Both have the same pair, which settles it as the split:
   - **New folder** (Photoshop *New Group*, Krita *Add Group Layer*) creates an
     **empty** folder directly above the active item, in the active item's own
     parent — at the top of the stack when nothing is selected.
   - **Group layers** (Photoshop *Group from Layers*, Krita *Quick Group*,
     **Ctrl+G** in both) wraps the selected items in a new folder at the
     position of the topmost of them.
   A folder is selectable on its own and is dragged and dropped like a layer,
   into or out of other folders.

## Asked and answered the same day, once the design was costed

- **How "folders join the stack" is stored.** The design pass counted 280
  sites reading `Scene.Layers`, about 40 of which reorder it, so a second stored
  order would have to be kept in step by every one of them. Offered: the same
  behaviour stored lighter — layers stay one flat list, a folder records its
  parent (`ParentId`) and, only while empty, its slot (`Under`). **Chosen: the
  lighter storage.** The owner's choice was the behaviour, and it is unchanged:
  a folder is an item in the stack whether or not it holds anything. Old files
  load as they are, with no migration; a file without nested or empty folders
  writes no new key.
- **Deleting a folder with layers in it.** Recommended: ask, as Photoshop does.
  **Chosen against it: delete the contents**, as Krita does — undo recovers
  them, and Ungroup is the way to keep the layers.
- **Ctrl+G on a selection that is not contiguous.** **Chosen: gather them** to
  the topmost selected item's position, as Photoshop and Krita do; the stacking
  changes and undo puts it back.
- **X-sheet folder bands** — not asked; deferred, the X-sheet's columns stay in
  layer order.

## What this does not decide, and should be asked when it is reached

- **Group blending.** Photoshop and Krita both let a group composite as one
  image (Normal / Krita's non-pass-through) or let its members blend straight
  through (Pass Through). Today a folder never changes compositing. Nesting
  does not force the question; a folder's own opacity or blend mode would.
- **Migration of existing files** is mechanical — every `GroupId` becomes
  membership in a top-level folder at its members' position — and is not a
  preference.
