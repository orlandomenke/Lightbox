# Q30 · When do characters and scenes become folders? — **answered (a), 2026-08-06**

Raised 2026-08-05 when `ProjectFolders` landed and parked at the owner's
request. Asked properly on 2026-08-06 with a recommendation, and the answer went
**against** the recommendation on two of three parts — recorded here as given,
with the one consequence of the combination named rather than argued.

### The answer

**Shape: (a), one hierarchy, now.** Character and Scene stop being separate
kinds of thing. The recommendation was to cascade resources first and merge
later; the owner took the merge directly, which is the stronger reading of
*nothing rigid, everything fluid, but with rules*.

Four consequences the owner specified, and they are the actual design:

| | |
| --- | --- |
| **Character library and asset library** | Become **project-based**, not character-based. Creating into them and saving to them is available **project-wide** rather than being a property of a character or of the Asset Library project type |
| **Pivot, colliders and the like** | **Folder-based**, and *enableable on a file*. Absent unless switched on — the camera's rule again, so a folder that never needs a pivot writes no key and shows no control |
| **Character sheets** | **Folder-based**, so a sheet can serve every drawing under a folder rather than one document. The owner made this conditional on it not colliding with the character library |
| **Resolution** | **Accumulate, nearest wins ties.** Every declaration in the chain registers; where two name the same swatch id the nearest one wins |

**The character-sheet condition is met — checked rather than assumed.** There is
no collision, and the reason is that Q25 already put sheets in the right place
*(as of Q25's re-answer on 2026-08-12 that place changed: a project's sheets are
now their own files filed by folder — which is this row's "folder-based" wish
finally built — and the no-collision reasoning below still holds because there
is still no `Character.Sheets` anywhere)*:
a `ReferenceSheet` lives in `Doc.ReferenceSheets`, so it belongs to a *document*
and there is no `Character.Sheets` for a library import to carry.
`CharacterLibrary.Import` copies `entry.Character.Animations` and their
documents, so a document's own sheets travel with it either way. Adding a folder
scope is purely additive: a folder-scoped sheet stays with the folder it was
declared on, which is correct — it belonged to the source project's structure,
not to the character being imported.

**Migration: new projects only.** Existing `.lbproj` files keep character
palettes and the character/scene layout; the new shape applies to projects made
after the change.

### The one thing the combination costs, stated once

*One hierarchy now* and *new projects only* pull against each other, and it is
worth writing down because neither option said it alone: if existing projects
keep characters, the `Character` and `ProjectScene` records — and every code
path that reads them — **can never be removed**. "One hierarchy" is then true of
new projects and false of the codebase, which keeps two of everything, which is
the cost this question was opened to remove.

Recorded as the owner's decision and not re-litigated. The narrow amendment that
would resolve it later, if it becomes annoying rather than theoretical, is to
read the old shape and write the new one — an old project stays readable and
adopts the new layout the first time it is saved. That is a one-line change of
policy in the loader, not a redesign, so deferring it costs nothing except the
duplicate paths in the meantime.

### What this does not change

`ProjectIo.Flatten` must keep inlining everything a document references
regardless of where it is filed, and `AProjectWrittenBeforeFoldersKeepsItsPaths`
must keep passing. Both are invariant 1 at the boundary where a file leaves the
app, and neither was ever a preference.

---

<details>
<summary>The deliberation this replaced, kept because the options were weighed
and (b) and (c) were rejected for reasons worth not re-discovering.</summary>

**The state of things.** The project now has two hierarchies. The folder tree is
arbitrary — any name, any depth — and `ProjectFolder`/`DocumentRef.FolderId`
describe it. Beside it, `Character` and `ProjectScene` still build paths from
fixed words in `ProjectIo`: `characters/<slug>/animations/<slug>.lightbox.json`
and `scenes/<slug>/shots/<slug>.lightbox.json`. Those constants are the last of
the naming convention.

Two hierarchies is a real cost and it is worth naming rather than tolerating
quietly: every surface has to render both, every operation has to ask which
kind of thing it is holding, and "move this into that" has four cases instead
of one.

**What makes it more than a refactor.** A character is not only a folder. It
carries a palette, a pivot, variants that inherit animations, and a
`character.json` that `CharacterLibrary` reads across projects. A scene carries
running order and a running time. Whatever replaces them has to keep all of
that, and has to open every `.lbproj` already written.

The shapes worth weighing when it is time:

**(a) A character *is* a folder with character data.** `ProjectFolder` grows a
nullable `Character` (and `Scene`), so one tree holds everything and a plain
folder simply has neither. One hierarchy, one set of operations, and the
migration is mechanical: read the old lists, emit folders, keep the ids.
Riskiest at the seam — `CharacterLibrary`, variants and `SymbolScopes` all
resolve characters by identity today.

**(b) A character *has* a folder.** The character record keeps its own life and
gains a `FolderId`; the tree is where it appears and the character is what it
is. Cheaper and reversible, and it leaves two records describing one thing —
which is the state that produced this question.

**(c) Leave them.** Characters and scenes stay a fixed top-level convention and
folders are for everything else. Honest for a game project with a flat asset
pile; wrong for a feature, where "Episode 2 / Act 1 / Sc 014" is the structure
and a character is one leaf of it.

**What does not need deciding either way**, so it should not hold the question
up: `ProjectIo.Flatten` must keep inlining everything a document references
regardless of where it is filed, and `AProjectWrittenBeforeFoldersKeepsItsPaths`
must keep passing. Both are invariant 1 at the boundary where a file leaves the
app, and neither is a preference.

</details>
