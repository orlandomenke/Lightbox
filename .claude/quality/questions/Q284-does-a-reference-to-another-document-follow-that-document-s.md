# Q284 · Does a reference to another document follow that document's edits? — **answered 2026-10-16: live, with a stored copy**

Raised by the owner wanting a run cycle to stand as reference for the jump and
the attack beside it in the same character folder: reference *another
document*, flattened, either on the board or under the drawing frame by frame —
with the referenced file never written to.

What it blocks: the record a document reference keeps, and whether anything
watches the source file.

| | What it costs |
| --- | --- |
| **Live, keep a copy** (recommended, **chosen**) | The strip stores the source's path *and* its flattened frames. When the source file is newer than the copy, the frames are re-flattened from it. A file watch or an on-open check, and a re-render of the source when it changed. |
| **Snapshot only** | Flatten once at import, like an imported image. Nothing to watch; re-import to refresh, and the reference silently goes stale while the run is still being worked. |

**Why the copy as well as the path:** the rule references already follow — *what
Lightbox keeps is the image, not the path* — exists so a reference never breaks
because a file moved. A path alone would bring that breakage back. A copy alone
is the snapshot. Keeping both means a moved or deleted source leaves its last
frames standing, the way a missing video leaves its extracted frames and a
deleted sheet view leaves its last picture.

**The source is only ever read.** It is opened by the loader, never handed to
an editor, never saved — so referencing a document cannot alter it, which is
the "keeping the file safe" half of the request.
