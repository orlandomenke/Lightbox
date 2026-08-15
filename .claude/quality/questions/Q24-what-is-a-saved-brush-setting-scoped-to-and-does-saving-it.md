# Q24 · What is a saved brush setting scoped to, and does saving it need a button? — **answered: automatic**

**Answered 2026-08-04: automatic persistence, no button.** Brush tuning survives
a restart on its own; there is no explicit *save settings* action and therefore
no second mechanism with a different lifetime competing with the first. The
reported pain was losing settings on restart, and that needs no new concept.

The scope question the button would have forced is deferred with it. `BrushScope`
already feeds a new document the project's brush
(`ANewDocumentInTheProjectIsFedThatBrush`), so per-project exists; per-file does
not, and nothing now requires choosing between them. **B71** is therefore the
whole of the work, and it keeps the rule that a brush left at its defaults writes
no keys.

**Blocks:** nothing.

Reported: "individual brush settings need to be cached for the duration of the
session… when brush settings are changed, present the user a save settings
button next to the all brush settings. This is stored per file and/or per
project."

Two decisions are tangled here and only the first is required by the bug.

**Automatic or explicit.** B71 as filed makes tuning survive a restart
automatically. An explicit *save* button is a different promise — it says the
tuning is a named thing an artist commits to, and that unsaved changes are
discardable. Both are defensible; shipping both without deciding gives an artist
two mechanisms with different lifetimes and no way to tell which one is holding
their brush.

**And what the scope is.** `BrushScope`/`BrushScopeDefaults` already exist —
a project feeds a new document its brush, guarded by
`ANewDocumentInTheProjectIsFedThatBrush` — so *per project* is built. *Per file*
is not, and the report asks for "per file and/or per project", which is the part
that needs a person: the two disagree the moment a document in a project is
opened, and something has to win.

**Recommend automatic persistence (B71) first and the button deferred**, because
the reported pain is losing settings on restart and that needs no new concept.
If the button still seems wanted afterwards, it is a small addition to a
mechanism that exists rather than a second one competing with it.
