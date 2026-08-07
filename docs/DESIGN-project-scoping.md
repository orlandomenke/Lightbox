# One hierarchy: folders scope everything

Status: **design, nothing built.** Supersedes the container half of pillar 1's
character/scene model. `B114` is the defect that prompted it.

## The goal, in the owner's words

> *"A flexible management system where we can scope based on project, folder or
> file. A character can have a top folder, that holds assets only designated to
> that character; a project can feed assets to all folders and files. A user can
> have their own brush settings and a project could dictate which brush settings
> need to be used for that project. A scene could have project assets and their
> own assets. All interchangeable — and that's why we opted for folders. A
> specific character folder or scene folder with set asset designation makes
> this system more rigid."*

Everything below follows from the last sentence. **A character is a folder that
happens to hold a character's work.** It is not a second kind of container, and
making it one is what makes the system rigid.

## What already exists, and works

`ResourceScopes.Resolve` walks, nearest first:

1. **Folder ancestry** — the document's folder, then its parent, then upward
2. **The project** — the scope above every folder
3. **Published from elsewhere** — anything declared with `ResourceReach.Project`,
   last, so it loses every tie to something nearer

Eight kinds resolve through it: `palette`, `gradient`, `reference`, `guides`,
`template`, `export`, `symbol`, `tip`. Nearest wins; a knight's own red beats
the studio red without anything being unpublished.

**That is the mechanism the goal describes.** It is not missing. What is missing
is that half the project cannot reach it.

## What is wrong

**There are two containers and only one is wired up.** Measured (`B114`):

```
manifest.Documents            : background
knight.Animations             : walk
walk.FolderId                 : (null)
whole-project export plan     : background          <- walk is missing
palettes visible to walk      : 0
palettes visible to loose doc : 1
```

`ProjectIo.AddAnimation` files a reference under `character.Animations`, never
in `manifest.Documents`, and never sets `FolderId`. `MoveDocument` treats the
two lists as mutually exclusive. So:

- `ExportPlan.DocumentsUnder` reads `manifest.Documents` — **a whole-project
  export omits every animation in an animation project.**
- `ResourceScopes.Resolve` keys off `document.FolderId` — **no folder's palette,
  references, guides, template or export preset ever reaches a character's
  work.**

Characters and scenes are not redundant with folders. They **bypass** them.

## The model

**One container: the folder.** Every document has a `FolderId`. There is no
second list.

**Character-ness and scene-ness become attributes of a folder** rather than
kinds of container:

| | Today | Proposed |
| --- | --- | --- |
| Holds documents | `Character.Animations` / `Scene.Shots` / `manifest.Documents` | `manifest.Documents`, filed by `FolderId` |
| Subject reading | `Character.Taxonomy` | `ProjectFolder.Taxonomy` |
| Export pivot | `Character.Pivot` | `ProjectFolder.Pivot` |
| Variants | `Character.Variants` | `ProjectFolder.Variants` |
| Running order | `Scene.Shots` (implicitly ordered) | `ProjectFolder.Order` |
| Director's notes | `Scene.Notes` | `ProjectFolder.Notes` |

Every one of these is nullable and absent until used, so an ordinary folder
writes exactly what it writes today.

**A character is then a folder with a taxonomy. A scene is a folder with an
order.** Both are *derived* rather than declared — the docker groups by what a
folder has, the way `bugs.py` derives a checkbox from whether a test exists.
Nothing is locked behind a kind, which is the *every feature is reachable in
every project type* rule applied to the project tree itself.

> **Superseded in part by Q38–Q40, and the correction is worth reading before
> the rest of this section.** Deriving *two nouns* from the facets is the same
> rigidity one level down. A production has props, environments, effects,
> vehicles, layouts and crowds; under two privileged nouns every one of them is
> "just a folder", and the code has to invent a tie-break for a folder carrying
> both. So the nouns are gone: a folder has **facets**, the artist names it with
> a **glyph**, and the questions the application asks are about a facet —
> *the nearest folder above this with a reading* — never about a kind. See
> *What was decided* below.

It also makes the thing the owner asked for fall out for free: a character folder
can hold assets designated only to it, a scene folder can have its own **and**
the project's, and a folder can be both or neither.

**Decided: `Character` and `ProjectScene` dissolve entirely.** The types go; a
folder carries the attributes.

### The hazard that comes with deriving it, and the answer

Because character-ness is now *derived from having a taxonomy*, it can be lost
by an action that does not look like losing it. Under the old model "delete
character" was an explicit, obviously destructive act. Under this one, clearing
a folder's taxonomy, or deleting the folder, quietly stops it being a character
— and takes the pivot, the variants and the reading with it.

**So any action that would end a folder's character-ness or scene-ness says so
first**, naming what goes: *"This folder is Knight. Clearing its reading also
discards the pivot and 2 variants."* Not a generic "are you sure" — the specific
list, the way the export confirmation already counts what it would write.

This is the owner's addition to the decision and it is the part that makes the
derived model safe rather than merely elegant. Losing a reading an artist
corrected by hand is the same failure `Reviewed` exists to prevent, arriving
from the other direction.

## Ordering is the one thing folders genuinely cannot do

`ProjectManifest` says so explicitly: *"The order of this list is not the display
order and nothing should read it as one."* Shots play in sequence, and that
order is authored, not alphabetical.

So folders gain an order — and it is worth having beyond scenes: a character's
animations have a natural order too, and today nothing can express it. One
mechanism, absent until used:

```csharp
/// Document ids in the order the artist arranged them. Null until somebody
/// orders something, and it need not list every document — anything absent
/// sorts after what is listed, by name.
public List<string>? Order { get; set; }
```

Partial ordering matters: a scene with three pinned opening shots and forty
unsorted ones should not require sorting forty.

## Two things the goal asks for that do not exist

**1. A user tier in the resolver.** `Resolve` knows folder, project and
published. It does not know the user. `TipStore.Available` takes a user `State`,
so one kind has a user library and the mechanism does not — user scoping is
half-built. Adding it is one more `Take` at the widest end, which also gives the
right precedence for free: **folder beats project beats user**, so a project
*can* override an artist's default without anything special.

**2. Brush presets are not a scoped kind.** Tips are (`tip`), but the brush
itself is a single `manifest.Brush`. *"A project could dictate which brush
settings need to be used"* needs `brush` as a ninth kind, resolved by `Nearest`
the way the palette and export preset already are.

**Decided: build it now, scoping the preset id only.** `BrushPreset.Id` is
already a stable `Ids.NewId("preset")`, and a `ScopedResource` is only a kind
plus an id — so `Lightbox.Core` needs no knowledge of `BrushPreset`, which lives
in `Lightbox.App`. It is the palette pattern with a different string.

Scoping the whole `BrushSettings` record was rejected: it is large, it would
bloat every manifest that used it, and it would create two sources of truth for
one brush plus a new question about which wins when the preset is edited.

The known cost, inherited rather than new: a document can reference a preset
that was deleted or never shared. The palette path already has this shape, so
it wants the same answer rather than a bespoke one.

Note what "dictate" then means, because the machinery already distinguishes it:
`Resolve` **offers** a set, `Nearest` **selects** one. A project declaring a
brush and `Nearest` returning it *is* the dictate. No new enforcement concept —
and for the narrowing kinds, declaring anything already restricts, since
`VisibleTo` returns null only when nothing is scoped.

## No migration

**Decided: there is none.** The application is alpha, single-user, and nothing
has been produced in it — *"I am currently only testing and no production
whatsoever has been run."* Writing a migration for zero real projects is cost
with no beneficiary, and it would be the second code path that this whole
document exists to remove.

**The consequence, stated plainly because it must not be a surprise: project
files written before this change will not open.** That is acceptable now and
would not be a month from now, so the change should carry its own tombstone:

- Bump `ProjectManifest.Version` to **2**.
- A version-1 manifest is **refused with a sentence**, not crashed on:
  *"This project was made with an earlier alpha and cannot be opened. Its
  drawings are intact — the `.lightbox.json` files can be opened individually."*

That last clause is true and worth saying: documents are their own files in
today's format and this change does not touch them. Only the index is lost, so
the work survives even though the project does not.

**Write the migration the day a second person has a project**, not before. This
section is the record that the decision was deliberate rather than overlooked.

## What this retires

- `Character.Animations`, `ProjectScene.Shots` as containers.
- `CharactersDir` / `AnimationsDir` / `ScenesDir` / `ShotsDir` as a mandated
  layout, and the slug machinery that keeps folder names stable across renames —
  a folder's name is just its name once nothing on disk depends on it.
- `ProjectIo.MoveDocument`'s two-list dance, which becomes
  `ProjectFolders.FileDocument`.
- The special "add an animation to a character" path, which becomes "add a
  document to a folder".

**B83/B84 is the argument that this is overdue**: `NewProject` inventing a
character from the project's own name and creating `characters/` and
`scenes/` unasked was the special-cased layout leaking. With one tree there is
nothing to invent.

## Verification

1. **The bug first.** `B114`'s three named tests — a character's documents are
   in the project, a whole-project export includes them, and a folder's palette
   reaches them — written against the new model and failing before it.
2. **Migration is lossless and moves nothing**: load a project written under the
   old layout, migrate, and assert every `DocumentRef.Path` is byte-identical
   and every file is where it was.
3. **Absence holds**: a folder that is not a character writes no `taxonomy`,
   `pivot`, `variants` or `order` key. `AFolderThatWasNeverTaggedWritesNoTagsKey`
   is the pattern.
4. **Order is partial**: a folder ordering three of forty documents lists three
   ids and sorts the rest by name.
5. **Precedence**: with the same kind declared at user, project and folder, the
   folder wins and the project beats the user.
6. Four suites green; `codemap.py build`, `roadmap.py sync`, `bugs.py check`.

## What was decided — Q35–Q37, answered 2026-08-07

| | Decision |
| --- | --- |
| **The model** | `Character` and `ProjectScene` **dissolve entirely** into folder attributes — plus a warning before any action ends a folder's character-ness |
| **Migration** | **None.** Alpha, single user, nothing produced. Version bump to 2 and a refusal with a sentence |
| **Brush presets** | **Ninth scoped kind now**, scoping the preset id only |

## What was decided — Q38–Q40, answered 2026-08-07

Q35 dissolved the two records and then collapsed the facets straight back into
two nouns. These three finish the job.

| | Decision |
| --- | --- |
| **Glyph** | **The artist picks it** — a grid of common production glyphs plus free entry. Deriving it from facets forces the code to choose a winner, and it chooses wrong the first time somebody makes a prop folder with a pivot |
| **Facet summary** | **In the details panel, not on every row.** Against the recommendation; the tree has to stay scannable at forty rows |
| **The nouns** | **Gone from the code and the UI.** `IsSubject`→`HasReading`, `Subjects()`→`WithReading()`, `SubjectFor`→`ReadingFor`, and the menu says *Read this folder…* |

**The line that keeps the glyph from becoming a second designation: it is a
label, the facets are the data.** Nothing reads it. The AI path asks for *the
nearest folder above this with a reading*; export asks for a pivot; neither asks
what the icon is. An artist's vocabulary is theirs and nothing downstream
depends on it.

Two places the old nouns were quietly making decisions, both removed:

- `IsScene` excluded folders that had been read, so a character could not
  accidentally be a scene. Inventing that tie-break is what a designation costs.
- The library offered only folders with a reading, so "character" decided what
  could be shared — and a shared environment or prop set is exactly what a
  library is for.

The cost of the row decision is recorded rather than waved past: nothing tells
you a folder holds a hand-corrected reading until you select it. What keeps that
from being a defect is Q35's warning, which fires at the moment of the
destructive act. If a reading is ever lost anyway, Q39 is the entry to revisit.

One thing to check before the first line is written, because it is the risk the
first decision carries: **does anything reference a character by id?** The
cross-project character library (P1d) is the likely holder. If it does, that
reference becomes a folder id and the library's format changes with it — still
fine under "no migration", but it is a second format touched by a change that
looks like one.
