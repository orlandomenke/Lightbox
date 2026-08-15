# Q44 · Is a bulk edit undoable? — **answered: no, and nothing is destructive**

**Answered 2026-08-07: no undo.** Status, tags and assignee are manifest
metadata rather than artwork — changing one touches no pixel, needs no document
open, and setting it back is the same gesture as setting it. The window says
what it did.

A second undo stack was rejected: `DocumentEditor`'s is per-document and holds
document state, so this would be a whole new system, and it would pre-empt *the
undo record becomes data* — unbuilt roadmap work that would want to own it.
A confirmation on every bulk edit was rejected as the friction that stops people
using bulk edits at all.

The accepted cost: a mis-drag on the status board is corrected by hand, and
Ctrl+Z will feel like it ought to work there.
