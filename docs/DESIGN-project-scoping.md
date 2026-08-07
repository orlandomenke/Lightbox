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

It also makes the thing the owner asked for fall out for free: a character folder
can hold assets designated only to it, a scene folder can have its own **and**
the project's, and a folder can be both or neither.

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

Note what "dictate" then means, because the machinery already distinguishes it:
`Resolve` **offers** a set, `Nearest` **selects** one. A project declaring a
brush and `Nearest` returning it *is* the dictate. No new enforcement concept —
and for the narrowing kinds, declaring anything already restricts, since
`VisibleTo` returns null only when nothing is scoped.

## Migration is metadata-only

**No files move and no paths change**, which is what makes this affordable.
`DocumentRef.Path` already survives independently of the tree — the model notes
that a document written before folders existed keeps its path and reports no
folder. So migrating a project is:

1. For each character, create a folder named after it.
2. Move its animations into `manifest.Documents` with that `FolderId`. Paths are
   untouched, so `characters/knight/animations/walk.lightbox.json` stays exactly
   where it is on disk.
3. Move `Taxonomy`, `Pivot`, `Variants` onto the folder.
4. The same for scenes, with `Shots` becoming the folder's `Order`.

The on-disk layout stops being *required* and becomes *what old projects happen
to look like*. New work is filed wherever the artist puts it.

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

## What needs deciding

Asked rather than guessed, per `CLAUDE.md`:

- Do `Character` and `ProjectScene` disappear entirely into folder attributes,
  or survive as thin records pointing at a folder?
- Does the migration run automatically on load, or on an explicit action?
- Are brush presets added as the ninth scoped kind now, or after the container
  work lands?
