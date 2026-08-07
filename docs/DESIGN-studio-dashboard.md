# The project window: what you do between drawings

Status: **built, 2026-08-07.** Answers Q29's second half — the docker is the
quick view, this is the other surface. Depended on B114's one tree, which
landed first.

## What it is for

> *"A project/studio dashboard that … can manage assets on project, folder and
> file level. This dashboard can be tabbed to make use of screen space if that
> is needed. It needs project structure, tags, status and more."*

Q29 already drew the line and it still holds:

> **The docker does what you do while drawing** — find it, open it, move it,
> rename it. **The window does what you do between drawings** — bulk operations,
> tagging, reference binding, status across the production.

The docker is 200 pixels wide beside a canvas. Everything below is a thing that
does not fit in 200 pixels beside a canvas, and that is the whole reason there
are two surfaces rather than one.

**One hierarchy, two views.** `ProjectFolders` is Core model code precisely so
this window does not grow a second implementation of the tree. Q29 answered
that in advance; the point of writing it down again is that the temptation
arrives now.

## What the model can already answer

More than expected, because B114 and Q30 did most of it. Nothing below is new
work:

| The window shows | Already in the model |
| --- | --- |
| Every document, wherever it is filed | `manifest.Documents` — one list since B114 |
| The tree it is filed in | `ProjectFolders.All / ChildrenInOrder / AncestryOf` |
| Status per document | `DocumentRef.Status`, `AssetStatuses.InOrder` |
| How long each runs | `DocumentRef.Frames/Fps/Seconds`, `ProjectIo.FolderDuration` |
| What a folder carries | `Taxonomy`, `Pivot`, `Variants`, `Order`, `Notes`, `Icon` |
| What resources reach a document | `ResourceScopes.Resolve`, eight kinds |
| What an export would produce | `ExportPlan.For`, `ExportPlan.Describe` |
| What is stale | `ExportRecord` against `DocumentRef.Version` |
| What is not on disk | the docker's `MarkMissing` walk |

**The dashboard is mostly a second reading of a tree that already exists.** That
is the argument for building it now rather than later: the traversal is written,
the statuses are on the right object, and the one-tree change removed the
special cases a dashboard would otherwise have had to carry.

## What was genuinely missing — all four landed

Four gaps, and each was small. They are listed separately from the window itself
because three of them were model changes that stood on their own. Kept here as
written, because the reasoning is what makes the shapes they took defensible.

### 1. Tags on a document

`ProjectFolder.Tags` exists. `DocumentRef.Tags` does not, so "tag the rooftop
shot" has nowhere to go — and tagging is half of what the window is for.

One nullable `List<string>?`, the same shape the folder already has, absent
until something is tagged.

**Free strings, and the vocabulary is derived.** Every tag in use anywhere in
the project is the offered list; typing a new one adds it by using it. A
declared vocabulary is a registry somebody maintains and a wall in front of the
first word that is not in it — the same argument that retired the six-entry New
menu. Q31's reference question already pointed here: tags are how *"every
character animation"* becomes expressible without listing them.

### 2. Resources declared on a document

`ResourceScopes` resolves **folder path → project → published**, and its own
doc comment says the chain is four tiers: *user library → project → folder path
→ document*. The document tier was designed and never built, so "manage assets
on … file level" currently has no target.

`DocumentRef.Resources`, nullable, and one more `Take` at the near end of
`Resolve`. Nearest already wins, so a document's own palette beating its
folder's falls out with no new rule.

### 3. The user tier

The other end of the same chain, and the other thing
`DESIGN-project-scoping.md` lists as missing. `TipStore.Available` takes a user
`State`, so one kind has a user library and the mechanism does not — user
scoping is half-built.

One more `Take` at the widest end, which also gives the right precedence for
free: **document beats folder beats project beats user**, so a project *can*
override an artist's default without anything special.

### 4. Who is working on it

The roadmap entry names *"assigned artist"* and *"workload"*, and there is no
user model of any kind — no accounts, no sync, no auth.

**Decided (Q43): a people list on the project, assigned by picking**, against a
recommendation of free text. The case that won is the feature's own purpose:
this is the surface that replaces a spreadsheet, and two spellings of one person
is exactly the spreadsheet problem. Grouping by assignee has to be exact, and a
rename has to fix every row.

**And the boundary that comes with it (Q45): `Person` is a name and an id, and
never gains a role or rights.** The manifest is plain JSON on disk, so a
permission here is one a text editor defeats — and a permission that cannot be
enforced is a UI that lies about what it enforces. An advisory role field was
rejected for the same reason: a role that grants nothing gets read as granting
something, and by the time somebody asks, the studio has organised around it.

Sharing is the project file over git or a drive. A tracker adapter — ShotGrid,
Kitsu, Flow — is the seam if a studio ever needs one, and it needs no new model
because documents already have stable ids to match a shot against.

## The window

One window, tabs across the top, because the four things it does want different
shapes and none of them wants to be squeezed.

```
┌─ Production.lbproj ─────────────────────────────────────────────┐
│  Structure │ Status │ Assets │ Export                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  (the tab)                                                       │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│  47 documents · 12 Ready · 3 Reopened · 2 not on disk            │
└─────────────────────────────────────────────────────────────────┘
```

The footer is the one thing on every tab: a count of what the project holds and
what is wrong with it, which is the sentence somebody opens this window to read.

### Structure — the tree, with room

The docker's tree with the columns it has no width for: **glyph, name, tags,
status, assignee, length, what it carries**. Editable in place — that is the
difference from the docker, where a status change is a right-click and a
sub-menu.

**Multi-select and act on the selection.** Set the status of nine drawings,
add a tag to a folder's whole subtree, assign a sequence to somebody. The
docker deliberately has no multi-select; a bulk operation is exactly the thing
you do between drawings rather than during one.

**Everything a folder carries is editable here**, which closes Q39's cost: the
facet list in the docker is read-only and behind a click, and this is where an
artist corrects a reading, sets a pivot or names a variant.

### Status — the production at a glance

The roadmap's *"all shots at a glance"*. Columns are the six statuses in
`AssetStatuses.InOrder`; each holds the documents in it. Drag between columns to
change a status.

Two things it says that a list cannot:

- **Reopened is not a step backwards on a line, it is an alarm.** It is last in
  the enum for that reason, and it gets its own emphasis here.
- **Stale beats status.** A document marked Ready whose exported sheet was built
  from an older `Version` is not really ready, and `ExportRecord` already knows.

Grouped by folder, by tag or by assignee — one control, because the same set of
documents answers three different questions depending on how it is piled up.

### Assets — the three levels, in one place

The scoped-resource surface the row menu can only reach one scope at a time.
Rows are scopes — the project, then every folder, then a document when one is
selected — and columns are the kinds. A cell says what is declared and what is
inherited, so *"why is this drawing painting from the studio palette"* is
answerable by looking rather than by reasoning.

This is where the user's sentence lands literally: **project, folder and file
level, all three visible together**, which is the thing no context menu can do.

### Export — what would be written

`ExportPlan.For` and `Describe` already produce this and only a confirmation
dialog reads them. The same plan, standing still, so it can be read before
anything is run: which artifacts, from which documents, how many held back by
status, which are empty.

## What was decided — Q41–Q45, answered 2026-08-07

| | Decision |
| --- | --- |
| **Where it lives** | **Its own window** with tabs inside, opened like Configure. A docker is 200 pixels and the point is columns; a main-window tab gives up the second monitor |
| **First cut** | **Structure, Status and Assets**, plus the model gaps they need. Export follows — `ExportPlan` exists and the row menu already reaches that view |
| **People** | **A registry, not a typed name** (Q43), and **never a role or a right** (Q45) |
| **Bulk undo** | **None.** Metadata, not artwork; each edit says what it did, including when it did nothing |

Two shipped beyond the cut because they fell out of it: a **People** tab, since
a registry with no way to add to it is a registry nobody can use, and **filters**
above the tabs rather than inside one — narrowing the tree and then finding the
status board showing something else would be two projects on one screen.

## What shipped after the first cut

The four things the first cut named as absent, all landed:

| | |
| --- | --- |
| **Assets writes** | Select a scope, pick from one flat list of everything it could be given, ✕ a chip to take it back. Both switch-flipping moments — the first declaration of a kind and the last — say so, because each is one click that changes what every other drawing sees |
| **Facet editing** | A panel for exactly one selected folder: notes, pivot, reading, variants. This is what closes Q39's cost, and it is also where `Reviewed` can finally be set — the flag shipped in PR #48 with nothing that could write it, and the refusal message said "clear it first" about a control that did not exist |
| **Drag between status columns** | A card is a drag source, a column is a drop target, and `MoveToStatus` behind both stays drivable without a pointer — synthetic input through Xvfb is unreliable here, so a gesture that exists only in a handler is one nothing can check |
| **Export tab** | `ExportPlan.For`/`Describe`, standing still. Read-only: running an export is the export window's job, and a second button is two places that can disagree about what export means |

One thing the Assets tab deliberately cannot offer: **references**. A reference
binds to a *target* as well as an id (`ReferenceTargets`), so a flat entry would
declare a sheet without saying what to do with it. `ProjectBoard.Offers` refuses
that kind rather than the tab forgetting it.

## What this must not become

- **Not a second tree implementation.** Q29's whole point. If a traversal is
  needed that `ProjectFolders` does not have, it goes in `ProjectFolders`.
- **Not a project manager.** No deadlines, no burndown, no time tracking. The
  roadmap's own note: *"does not replace ShotGrid, just gives visibility."* The
  line: it shows what is in the project and lets you change what is in the
  project. Anything about *people and dates* is a different application.
- **Not a place drawings are edited.** Double-click opens a tab in the main
  window. Nothing here touches a stroke, so invariant 1 is not in play anywhere
  in this feature.
- **Not required.** A single-illustration project never opens it, and nothing
  it adds writes a key until used.

## Verification

1. **The tree is not reimplemented.** The window's structure view is built from
   `ProjectFolders`; a test asserts the two surfaces list the same documents in
   the same order for the same project.
2. **Absence holds.** A document with no tags, no assignee and no declared
   resources writes none of those keys. `Assert.DoesNotContain("\"tags\"", json)`
   in the same commit as each field.
3. **The chain is four tiers and nearest wins.** With the same kind declared at
   user, project, folder and document, the document wins and each tier beats the
   one wider than it.
4. **A bulk edit is one undo, or none.** Answered by Q44: none, because status
   is manifest metadata rather than document state.
   `NothingHereNeedsTheDocumentOpen` is what makes that honest rather than a
   shortcut — it asserts a bulk edit reads no artwork file at all.
5. **The counts are the same counts.** The footer, the status view and
   `ExportPlan.Describe` must agree; three places counting documents separately
   is three places to drift.
6. Four suites green; `codemap.py build`, `roadmap.py sync`, `bugs.py check`,
   `manual.py sync`.

## The roadmap entry needed editing either way

Its evidence anchors are `StudioDashboard, ShotStatusView, DashboardTests,
AllShotsVisibleWithStatusAtAGlance, BlockedShotsAreHighlighted,
ArtistWorkloadIsBalanced`.

Two of those name things this design does not build. **`BlockedShotsAreHighlighted`**
needs a dependency model — what blocks what — which nothing in Lightbox has and
this design does not propose. **`ArtistWorkloadIsBalanced`** is a claim about
balance, which needs estimates and capacity, and is the "project manager" line
above. Both come out, or the item ships green against tests that assert
something else — which is the one thing the derived checkbox cannot represent.
