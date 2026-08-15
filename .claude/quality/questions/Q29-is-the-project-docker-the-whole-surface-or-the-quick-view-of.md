# Q29 · Is the project docker the whole surface, or the quick view of one? — **answered (a)**

**Answered 2026-08-05: (a), settle the split first and build the hierarchy
shared.** The folder hierarchy is Core model code that both surfaces read, not
a docker view model that a window later borrows.

The division: **the docker does what you do while drawing** — find it, open it,
move it, rename it — and **the window does what you do between drawings**:
bulk operations, tagging, reference binding, status across the production.

This is sequencing rather than taste. Hierarchy is the one piece both surfaces
need, so building it into the docker first is how it ends up with two
implementations — and the second one is always written by somebody who cannot
change the first.

Raised 2026-08-05 by the owner: *"the project docker is part of a larger
project window where we can do advanced operations. The docker is the quick
overview and document/hierarchy helper."*

Recorded rather than answered, because it decides where the open project bugs
land and they should not each guess. B86 (drag/drop, subfolders,
collapse/expand), B87 (permanent delete with confirmation), B64 (rename), B63
(the create menu) are all currently filed against *the docker*. If a project
window exists, some of them belong there instead — a delete that prompts about
a folder full of files is a poor fit for a sidebar, and a bulk retag is not a
docker operation at all.

The split that seems to hold, and is offered as a starting point rather than a
conclusion: **the docker does what you do while drawing** — find it, open it,
move it, rename it — and **the window does what you do between drawings**:
bulk operations, tagging, reference binding, status across the production,
whatever an artist would stop drawing to do.

The reason to settle it before B86 rather than after: hierarchy is the one
piece both surfaces need, and building it into the docker first is how it ends
up with two implementations.

---
