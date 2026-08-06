# Saving and recovery

| Command | What it does |
| --- | --- |
| **Save** — Ctrl+S | Writes in place. With a project open, writes the project and only the documents that changed. A drawing that has never been saved has nowhere to go, so this opens **Save as…** instead of quietly doing nothing. |
| **Save as…** — Ctrl+Shift+S | Picks a new path. |
| **Export document…** | Writes a standalone `.lightbox.json` with every referenced swatch, gradient, brush tip and clip region **inlined**. |
| **Export PNGs…** | Every frame as a numbered PNG, into a folder you pick. |
| **Export for a game engine…** | Sprite sheet, sidecar, and optionally the Unity importer. |

Both keys can be rebound like any other — they are in **Edit ▸ Configure ▸
Shortcuts** under *File*, and the menu shows whatever you set them to rather
than the factory key.

## Nothing leaves the app until the drawing is on disk

**Exporting, and marking an asset Ready, are both statements about a file.** An
export says "this is what the drawing looks like"; a status says "this is
finished, go and use it" — and with auto-export on, that second one immediately
writes a sheet for a game engine to pick up. Neither means anything if the
drawing itself was never saved.

So both check first, and there are only two answers:

- **Save file as…** — pick a path, and what you were doing carries on.
- **Revert status change** (or **Don't export**) — nothing happens, and the
  status goes back to what it was.

There is deliberately no "do it anyway". A status change is **prohibited** until
the drawing has a file: the alternative is a designer told an asset is ready,
pointing at nothing.

If the drawing has a file and you have unsaved changes, it just saves them —
no dialog. You already said where the file goes, so asking again would be a
click in the way.

One case worth knowing because the wording differs: if the file you saved to has
since been **moved or deleted**, you are asked again rather than having it
written silently back to a folder you emptied on purpose.

## Closing with work in flight

**Closing a tab** with unsaved changes offers **Save**, **Discard changes** and
**Cancel**. Save is what Enter does, because it is the only one that cannot lose
anything; Discard sits at the far end so a fast hand does not find it next to
the safe one. If Save has nowhere to write, it opens Save as… — and **cancelling
that picker cancels the close too**, because you asked to keep the work.

**Closing Lightbox** asks the same question about everything at once. One box,
listing the documents by name rather than counting them, with **Save all** as
the default. Cancel and the application stays open; cancel a file picker
part-way through and the whole close is called off, with everything still there.

Nothing is ever discarded without being offered to you first.

## Autosave

Under **Edit**. Choose off, 30 seconds, 1, 5 or 15 minutes. Zero is a real
answer, not a mistake to guard against.

Autosave writes a **recovery copy** to your app data folder, not over your file.
Recover by opening it. If you would rather it wrote over the real file too,
there is a checkbox — off by default, because silently rewriting the file you
opened takes away the ability to close without saving.

---
