# Q35 · Do Character and Scene survive as records, or dissolve into folder attributes? — **answered: dissolve**

**Answered 2026-08-07: dissolve entirely.** `Character` and `ProjectScene` go.
A folder carries `Taxonomy`, `Pivot`, `Variants`, `Order` and `Notes`, each
nullable and absent until used. A character *is* a folder with a taxonomy; a
scene *is* a folder with an order. Both derived, neither declared.

**With a condition the owner added, and it closes a hazard the recommendation
missed:** *"but want the user [to know] they're about to do so."* Because
character-ness is now derived, it can be **lost by an action that does not look
like losing it** — clearing a taxonomy, or deleting a folder, silently takes the
pivot, the variants and a hand-corrected reading with it. Under the old model
"delete character" was explicitly destructive; under this one it is a side
effect.

So any action that would end a folder's character-ness or scene-ness **names
what goes before doing it** — *"This folder is Knight. Clearing its reading also
discards the pivot and 2 variants."* The specific list, not a generic "are you
sure", the way the export confirmation already counts what it would write.

**One thing to check before the first line is written:** does anything reference
a character *by id*? The cross-project character library (P1d) is the likely
holder, and if it does, that reference becomes a folder id and a second format
is touched by a change that looks like one.

**Blocks:** nothing. It is the fix for **B114**.
