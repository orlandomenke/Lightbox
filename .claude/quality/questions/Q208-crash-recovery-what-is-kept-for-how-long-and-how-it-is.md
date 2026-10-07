# Q208 · Crash recovery: what is kept, for how long, and how it is offered back — **answered 2026-10-07**

**Answered:** **a recovery copy per open document, offered back on the next
launch.** Each document with unsaved work gets its own copy, deleted only when
that document is saved or closed cleanly; after a crash, the next launch lists
what was left and offers to restore it, and nothing overwrites a copy until the
artist has answered. The owner chose the recommended option over two smaller
ones: per-document copies with no prompt (restore by hand from a folder), and a
rotation of the last N copies of the one slot.

Raised by a loss, 2026-10-07. The B392 playback crash killed the owner's
session at 12:37 with work unsaved. Autosave had been running every minute, so
that work was on disk — in **one** file, `%APPDATA%\Lightbox\autosave.lightbox.json`,
written for **one** tab (the save target), and never offered back. The
relaunch at 12:58 wrote over it at 13:10, and the work was gone. In-place
autosave was also on, so the same write went over the document's own file.

## What it costs

- **A prompt after a crash**, and only then. A clean exit leaves nothing to offer.
- **Disk:** one gzipped document per tab with unsaved work, for the life of the
  session — the same size as saving it.
- **A sensitivity review**: it decides what happens to an artist's work
  (`SENSITIVITY.md`, "work is never lost").

## Implementation choices inside the answer

Taken as defaults rather than asked, each for a stated reason:

- **One folder per running session**, holding a lock file the process keeps
  open. A later launch — or a second instance running at the same time — tells a
  live session from a dead one by whether it can take the lock, and writes only
  into its own folder. That is the exact failure of 2026-10-07: the new session
  wrote into the slot the dead one had left.
- **A restored document opens as "name (recovered)" and is not tied to the
  original file.** Saving it asks where, starting at the original location —
  Photoshop's rule, and the one that cannot silently overwrite a file that
  changed after the crash (in-place autosave can change it).
- **A restored document counts as work to lose until it is saved**, so closing
  it prompts even though nothing has been drawn on it since it opened.
- **"Not now" keeps the copies**, and the list stays reachable from the File
  menu; **"Discard" asks to confirm**, because it is the one permanent step.
- **The old single `autosave.lightbox.json` is left where it is**, untouched:
  it may hold the only copy of something, and it is not this change's to delete.

## Where the copies live — asked during review, 2026-10-07

**Answered: local app data** (`%LOCALAPPDATA%\Lightbox\recovery`), the
recommended option, over roaming app data beside the old single autosave file.
The sensitivity review raised it: these are full copies of every document with
unsaved work, often several MB and possibly under NDA, and on a domain machine
with roaming profiles a roaming folder syncs every one to the organisation's
profile server at each logoff. A recovery copy belongs to the machine that
crashed.
