# Q226 · Folder colour: washes the whole row, and a subfolder inherits until it is set — **answered 2026-10-09**

**Answered:** a folder's colour **washes its whole header row and, more
faintly, the rows of the layers inside it**; and a subfolder **shows its
parent's colour until it is given one of its own**, which then holds from there
down. Both were the recommended options.

Raised by the owner, 2026-10-09: *"folder color in the layer docker should
lightly color entire row, not just the pip in front. subfolders inherit the
color of the parent folder."*

Two things were the owner's to decide, asked with `AskUserQuestion`.

## When a subfolder's own colour counts

Every folder stored a colour — blue by default — so "inherit" had no meaning
until something said when a subfolder's own colour wins.

- **Inherit until set** (chosen, recommended). `LayerGroup.Color` is null, and
  absent from the file, until a colour is chosen; `FolderTree.ColorOf` resolves
  what shows. *Same as parent* on the colour menu clears it.
- *Parent always wins* — a subfolder's colour ignored while nested. Simplest,
  and two sibling subfolders could never be told apart by colour.
- *Copy on change* — setting a folder's colour writes it into every subfolder.
  No format change, but it is a one-off copy rather than a relationship: a
  folder dragged in later would not follow.

## How far the wash reaches

- **Folders and their layers** (chosen, recommended): header strongly, members
  faintly, so the folder reads as one band.
- *Folder rows only* — the layers inside stay neutral.

## What it costs

- **A file format difference, in one direction.** A folder that never chose a
  colour now writes no `color` key. An older build reading such a file shows
  that folder in its own default blue, which is what the file means.
- **Files saved before this do not start inheriting by themselves.** They
  recorded a colour on every folder, and a recorded blue is indistinguishable
  from a chosen one, so those subfolders keep it until *Same as parent* is
  used. The option as it was put to the owner said such folders "start
  inheriting"; that needed a way to tell an old file's blue from a chosen blue,
  and the only one available was a document-version migration touching every
  file on save. It was not taken for a docker colour. If old files matter
  here, that is the change to ask for.

## Implementation choices inside the answer

- **The wash is on the row's container**, by a style in `LayerRows.axaml`, not
  on the row: the row's own fills (selected, active) are translucent white and
  have to be drawn over the wash to stay the strongest thing on the row.
- **A folder header lost its opaque grey.** The wash sets a header apart now,
  and the grey would have hidden it.
- The wash spans the full row width, indent included.
- A hidden row's content dims as before; its wash does not.
