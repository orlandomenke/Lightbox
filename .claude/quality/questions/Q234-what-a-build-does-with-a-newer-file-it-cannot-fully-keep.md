# Q234 · What a build does with a newer file it cannot fully keep — **answered: warn on open**

**Answered 2026-10-09: warn on open.** Documents carry a format version; a
build opening a file with a newer version than it writes shows a one-line
notice that saving here may drop what it cannot read.

Raised 2026-10-09 by the sensitivity review of Q230 (#660). Q230 keeps a
newer build's unknown keys on every record type a document reaches and writes
them back. Two places it cannot reach:

- **Per-point values.** Structs are not given a holder, because an extra field
  on `StrokePoint` would be paid on every point of every stroke, and Q112 sends
  new pen axes into `StrokePoint`. A newer build's new axis would still be
  dropped on a save here.
- **The project manifest and version history**, which are outside the document.

"Refuse honestly" was not available either: `Doc.Version` is checked nowhere,
and a refusal has to exist in the older build to work.

**Recommendation was the chosen one.** The alternatives:
- open read-only (Save As only) — safest for the data, but it blocks editing in
  the lagging build;
- accept the gap — keeping covers almost everything.

What it needs before it can do anything: the version must be written and
checked before the next format change, because the build that reads a newer
file is the one that has to warn.
