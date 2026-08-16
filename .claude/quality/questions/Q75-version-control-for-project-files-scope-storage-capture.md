# Q75 · Version control for project files: scope, storage, capture, surface — **answered 2026-08-13**

The `VersionEntry`/`VersionHistoryManager` framework had sat in Core since
M-series work with tests and no store, no content and no UI — recorded as
FEAT-002 "framework-only" in `docs/development/PROJECT-STATUS.md`. Building it
out needed four decisions, prompted and answered in one exchange:

- **What gets versioned first?** — *documents and character sheets.* Both are
  single files with stable manifest ids, so one file-copy mechanism serves
  both; brushes and palettes wait until wanting history is demonstrated rather
  than assumed.
- **Where does history live?** — *in the project folder*, `versions/<resourceId>/`,
  keyed by id so B188-style re-filing moves nothing. History travels with the
  project over git or a drive, which is how projects are already shared (Q43's
  boundary: no accounts, no sync). Registered in `SystemFolders` so B83 does
  not report it.
- **When is a version captured?** — *authored plus milestones.* "Save
  version…" with a label and notes, and an automatic milestone-tagged version
  when a document is promoted to Review or Ready — the moment a studio wants
  the frozen copy, and the reading `VersionEntry.MilestoneStatus` was built
  for. Not every save (unbounded, meaningless labels, duplicates autosave);
  rolling last-N deferred as an addition that needs a retention preference.
- **Where is the UI?** — *File menu plus one shared history window*, with the
  same window reachable from the project docker's row menu. Menu rather than
  project-window-only because solo painters never open the project window.

Costs accepted with the answers: a promotion versions the file **as saved on
disk** (status is set between sessions; reaching into open editors from the
project window would couple them), and reverting an open character sheet
closes its view tabs rather than rebinding them (B98's registration dance,
backwards, is where that path leads). `CreateBranch` stays framework-only —
history is one line per file until that proves insufficient.
