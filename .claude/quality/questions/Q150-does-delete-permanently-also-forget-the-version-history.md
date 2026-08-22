# Q150 · Does "Delete permanently…" also forget the version history? — **answered 2026-08-22**

Found while the adversarial pass on the project-window versions branch (PR
#391) was probing the facts cache: `ProjectVersions.ClearHistory` existed with
a doc comment naming its purpose — "for when the resource itself is deleted" —
and nothing called it. So **Delete permanently…** removed a resource from the
project and from disk while its kept version bytes stayed under
`versions/<resourceId>/` forever: unreachable (the id is gone from the
manifest, so no surface can list or revert them) and unaccounted (no UI, no
footer, no cost anywhere).

Three options were prompted:

- **Clear the history with the delete** — *chosen.* The confirmation already
  says "from the project and from disk" and is the artist meaning it; kept
  bytes nothing can ever show are a leak, not a safety net. This is also the
  reading `ClearHistory`'s own remarks intended.
- **Keep the bytes as a last-resort safety net** — rejected: there is no
  restore path (restoring needs the manifest id the delete removed), so the
  "net" catches nothing, and the confirmation's sentence stops being the
  whole truth.
- **Keep, but say so in the confirmation** — rejected for the same reason:
  honesty about accumulating unreachable data does not make it reachable.

The line this draws, worth keeping: **Remove from project keeps the history.**
The file survives, re-adding is meant to be cheap, and a re-added document
keeps its id — so its history is still *its* history. Deletion is the one
gesture that already asks first, and it is the one that forgets.

Applies to the direct document and sheet rows and to documents inside a
permanently deleted folder. Sheets filed in a deleted folder are a separate
pre-existing defect (nothing re-homes or detaches them — see the bug filed
alongside this answer) and their history follows whatever that fix does with
the refs.
