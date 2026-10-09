# Q227 · Timeline and X-sheet: folders as rows, folded by their own state, with a summary — **answered 2026-10-09**

**Answered:** the Timeline and the X-sheet show **folders as rows**, as the
Layers docker does; a folder **folds there with a state of its own**, separate
from the docker's; a folded folder shows a **summary row** marking every frame
where anything inside it is drawn, and that summary is to be **draggable**, a
drag retiming every drawing inside the folder on that frame together; and
layers and folders can be **pinned**, Krita's way — a *Pinned only* switch that
shows the pinned rows plus the layer being drawn on.

Raised by the owner, 2026-10-09: *"so often times I work with multiple layers;
sketches, line art, color before merging some or all in categorized layers. But
during those early stages I do not want to see all layers in the
timeline/xsheet dockers. Krita uses a 'Pin' system. I want to use folders as in
the layer docker that can be collapsed and expanded to show all tracks/rows.
And an option to pin layers and folders as well."*

Three things were the owner's to decide, asked with `AskUserQuestion`.

## Whose fold state

- **Its own** (chosen, recommended): `LayerGroup.SheetCollapsed`, apart from
  `Collapsed`. The stated case is layers kept open in the Layers docker, where
  they are worked on, while the sheet shows one row.
- *Shared with the Layers docker* — one state everywhere, and no way to fold
  the sheet without folding the layer list.

## What a folded folder's row shows

- **A summary you can drag** (chosen, against the recommendation). The
  recommendation was the summary read-only, as the smallest honest thing; the
  owner wants the drag, which makes a folder a handle for moving a whole pose.
- *Read-only summary*, *bare header* — not taken.

## How pinning works

- **Krita's** (chosen, recommended): pin a layer or a folder, and a *Pinned
  only* switch shows just those, plus the active layer so it never vanishes. A
  pinned folder brings what is inside it. Saved with the document.
- *Hide from timeline* (the inverse flag, no switch) and *pinned stay on top*
  (nothing hidden) — not taken.

## How it lands

Three branches, in this order, because each is one objective and the later two
need the first's rows:

1. **Folders as rows, with the summary** — this file's commit.
2. **Pinning.**
3. **Dragging the summary.** Until it lands a folder's row is read-only: it
   names no key, so nothing can be selected, dragged or deleted through it.

## What it costs

- **A row index is no longer a layer index plus an offset.** Folder rows sit
  between the layer rows, so every conversion goes through
  `MainViewModel.SheetItemAtTrack`. `SheetFolderTests` pins it.
- **What is folded away is out of reach.** Folding drops the selection of cels
  that went out of sight, and a block swept across a folded folder leaves its
  layers out — otherwise a Delete would take drawings nobody could see were
  picked.
- **The active layer can be folded away.** Its row is then not on the sheet,
  though drawing still lands on it and the folder's summary shows it. Pinned
  only keeps the active layer in view; folding does not.
- **The fold travels with an undo of the stack**, as the docker's collapse
  does: it is a view preference stored on the folder, so undoing an edit that
  restores the folders restores how they were folded then.

## Implementation choices inside the answer

- `FolderTree.SheetRows` is `FolderTree.Rows` walked with a different fold
  predicate, not a second walk, so the sheet cannot order rows by a different
  rule from the docker beside it.
- One list, `MainViewModel.SheetRows`, read by both surfaces.
- The X-sheet has no indent — every row's cells must stay under the ruler — so
  membership is shown by the folder's colour wash (Q226), as in the docker.
  The Timeline indents names.
- A folder's marks are squares on the Timeline and bars on the X-sheet, not
  dots and cels: a dot is one drawing, and this is "something inside is drawn
  here".
