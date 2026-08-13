# A project as a game's asset set

Status: **status and auto-export built; folders and scope still designed only.** This is the answer to three questions asked
together — can a project hold *all* a game's assets, can the folder structure be
the artist's rather than ours, and can export follow it — plus two that arrived
with them: asset status, and version control.

## The shape of the problem

A project has exactly one grouping axis today: **`Character`**, which carries real
semantics (a palette, a pivot, variants that inherit animations). Everything else
lands in a flat `Documents` list. So "environment A, environment B, props, UI" has
nowhere to go except as characters-in-name-only, which reads wrongly the moment
somebody opens the panel.

The fix is not to generalise `Character` into a folder. `Character` earns its type:
a palette shared by its animations is the whole of Pillar 1, and a folder with a
palette is a character wearing a different word. What is missing is a **second,
generic axis** beside it.

## Dynamic folders

```
ProjectManifest.Folders : List<AssetFolder>?     // null until one is made
AssetFolder { Id, Name, Kind?, Documents, Children }
```

- **Nestable**, because "Environments / Forest / Props" is how people actually
  file things, and a single level would be outgrown in a week.
- **`Kind` is a nullable hint**, not a type system: `Character`, `Environment`,
  `Prop`, `UI`, `Fx`, or absent. It picks sensible export defaults and an icon.
  It must never gate what can go in a folder — the moment it does, somebody has a
  prop that is also a background and the model is arguing with them.
- **Additive and nullable**, so a project that never makes a folder writes no
  folder key and behaves exactly as it does today. The camera's rule.
- `Characters` stays. A folder can hold characters' animations by reference if
  somebody wants both views, but the character is still where the palette lives.

The exporter then reads the tree rather than a fixed structure, which is the whole
of "dynamic instead of static": **create a folder, and the exporter can already
export it.** No exporter change per folder kind, ever.

## What export can be pointed at

Two independent choices, because they answer different questions:

| Scope — *what* | |
| --- | --- |
| **Document** | the open animation. What exists today, and it stays the primitive. |
| **Folder** | one folder and everything under it, recursively. |
| **Selection** | several folders ticked in the panel. |
| **Project** | all of it. |

| Grouping — *how many sheets* | |
| --- | --- |
| **Per animation** — the default | One sheet per document. Simplest import, no texture-size cliff, and Unity's own Sprite Atlas re-packs it at build time anyway. |
| **Per folder** | One atlas per folder — a character's whole cycle set in one texture. Fewer binds, usually *fewer* total bytes. **Needs animation tags to be usable at all**, and needs the size guard below. |
| **One atlas** | Everything in one. Best at runtime, worst for iteration: change one frame and the whole atlas re-imports. |

**The correction worth writing down, because the intuition runs the other way:**
more sheets is usually *more* total bytes, not fewer — each pays its own padding
and its own rounding, and each is a separate texture at runtime. The real hazard
of grouping is not size growth, it is **exceeding max texture size** (commonly
8192 or 16384), where the failure is an engine refusing the import. So the grouped
modes take a **`MaxSheetSize`** and fall back to splitting into numbered sheets
rather than producing a file nothing will read. Silently. With a line in the
report saying it happened, because a split changes how many files an importer
finds.

### Backgrounds are a folder-shaped question

The one export setting that genuinely differs per *kind* of asset rather than per
export, and the folder tree is what makes it expressible:

| Folder | Wants |
| --- | --- |
| `characters/` | No background, ever. The grey layer under the line is a working aid. |
| `props/`, `fx/` | Same. |
| `environments/`, `backdrops/` | Often the background **is** the asset. |
| `ui/` | Mixed, and usually decided per file. |

The mechanism is already built and is deliberately not being invented again here:
`BackgroundHandling` — `PaperOnly` / `Detected` / `Everything` — on the export,
plus a three-state pin per layer (`Layer.OmitFromExport`: never, always, decide).
What the folder tree adds is a **default `BackgroundHandling` on `AssetFolder`**
that an export inherits, **nearest ancestor wins**, and a document may still
override it. So `characters/` is set to `Detected` once and every character
underneath it is right without anybody ticking anything.

Three notes on why it is shaped this way:

- **Inheritance rather than per-file configuration** because per-file is what the
  artist is trying to escape. A per-file tickbox is the fallback for the odd case,
  not the mechanism.
- **The folder default is a default, not a rule.** A backdrop that lives under
  `environments/` and happens to want the paper dropped says so on the file, and
  the file wins. Nearest-ancestor-then-file is the whole precedence.
- **It stays reported.** Whatever the folder decided, the export names every layer
  it left out. A default that hides *why* something is missing would make the
  tree the thing people distrust.

**Path template** is how a studio gets its own layout rather than ours:
`{project}/{folder}/{animation}`, `{folder}_{animation}`, `{path}` for the full
nested path. This is the "custom method" half of the question, and it is cheap
because it is string substitution over a tree walk.

## Asset status, which is the best part of the idea

`DocumentRef.Status` — nullable, absent by default:

`Design` → `Draft` → `InDevelopment` → `Review` → `Ready`, plus `Reopened`.

One refinement since (owner request, 2026-08-13): **a new document becomes
`Draft` on its first save** — the write that first puts its file on disk, in
`ProjectIo.Save` — so fresh work enters the pipeline by itself. First write
only, and only when the status is null: a document already on disk without a
status is imported or predates statuses, and backfilling it would invent a
pipeline position nobody chose. Clearing a status back to null still sticks,
so "nobody has said" remains sayable.

Three things make this worth more than a label:

1. **It lives on the manifest, not in the document.** Marking something Ready must
   not dirty the artwork file, must not touch a pixel, and must not need the
   document open. Status is production metadata about a drawing, not part of it.
2. **The exporter filters on it.** *Export everything that is Ready* is the single
   most useful thing on this page: it is what lets an artist keep work-in-progress
   in the same project as shipped art without shipping it by accident. Today the
   only way to do that is a second project, which is the thing this design exists
   to remove.
3. **`Reopened` is not a synonym for `InDevelopment`.** It means *this was Ready
   and is not any more*, which is exactly the state a producer needs to see and
   the one a linear pipeline cannot express. Keeping it distinct is the difference
   between a status field and a workflow.

The panel shows it as a colour, filters by it, and sets it in bulk on a selection.
No approval gates, no locking, no permissions — an indie studio has none of that
machinery and would resent being handed it.

### Auto-export: the status *is* the trigger

**Built.** The payoff turned out to be bigger than an export filter. If status
already means "this is done", then reaching it is the moment to hand the asset over
— so the artist stops thinking about exporting at all, and the engine reads a file
that appeared because the workflow moved forward.

The rules that make it safe, in the order they matter:

1. **The status change is authoritative; the export is a consequence.** Ready is
   written and saved, *then* the export is attempted. A missing folder, a file the
   engine has locked, an unmounted drive — the artist keeps their status and gets a
   message. The reverse would make a production field hostage to a network share.
2. **Off until switched on.** It writes files into somebody else's project on a
   click. That is consent, not a default.
3. **Re-selecting the same status does nothing.** Opening the menu to check what a
   document is set to must not re-export it.
4. **The trigger is configurable.** Ready by default; a studio that reviews in
   engine sets Review. When the engine should see an asset is a question about
   their pipeline, not ours.
5. **Relative output folders resolve against the project, and are refused without
   one** rather than resolved against the working directory — which would write
   files somewhere nobody chose.

Configuration is general — one folder, one preset, one trigger — because it
describes how a studio ships rather than anything about one document. It lives in
the Configure window beside the autosave interval, which is the closest existing
thing: a background action switched on once and then forgotten.

## Version control: be friendly to it, do not reimplement it

Lightbox should not become a git client — and the more useful realisation is that
**git is not the version control most of its users are on.** See the section after
this one: game studios run *locking* systems, and locking is the part a drawing
application can genuinely help with.

What it can do, in ascending order of cost, and only the first two are certain:

**1. Be diffable, which is mostly already true and has one real hole.** The
`.lbproj` folder of plain JSON was chosen partly for this, and the stroke record
diffs well. The hole is `PaintedFrame.PngBase64`: an imported baseline is one
enormous single-line string, so a document carrying one produces a diff nobody can
read and a repository that grows badly. Worth measuring before promising anything
— and if it is as bad as it looks, baselines belong in sidecar `.png` files beside
the document rather than inside it. That is a change to the on-disk layout and
therefore its own decision, filed rather than assumed.

**2. Show status from git, and never act on it.** Read `git status` for the
project folder and put a mark on each row: unmodified, modified, untracked,
conflicted. Read-only, so nothing can go wrong that an artist has to understand.
This composes with asset status rather than competing: one says *where this is in
production*, the other says *whether the file on disk matches the last commit*.
Conflating them would be the mistake.

**3. Commit and push from the panel** — deliberately last, and honestly optional.
The value is real for a solo artist who does not use a terminal. The cost is that
every git edge case becomes a support question in a drawing application, and
merge conflicts in art are not something a panel can resolve. If it is built, it
is *stage, commit, push* and nothing else: no branching, no rebasing, no conflict
UI, and a plain "resolve this in your git client" when it cannot proceed.

## The version control game engines actually ship, and why it changes the design

*Written up after the question "could we connect to version control systems shipped
by game engines, like Unity's?"*

Yes — and it is a better fit than git, for a reason worth stating plainly.

### One provider interface, several CLIs

| System | Where it comes from | Driven by |
| --- | --- |
| **Unity Version Control** (formerly Plastic SCM) | Unity ships it | `cm` |
| **Perforce / Helix Core** | the industry default for game art | `p4` |
| **Git** | everywhere | `git` |
| **Godot, GameMaker** | ship no VCS of their own | git, via their own plugins |

Unreal is worth naming precisely: it does **not** ship a VCS. It ships *Revision
Control integration* that talks to Perforce, Git, Subversion or Plastic. So there
are three real backends, not five, and every one of them is driven by a
command-line client that is already installed if the artist is on that system.

That makes the shape obvious and cheap: **an `IVersionControl` with one
implementation per CLI, shelling out to a binary that is absent unless the artist
has it.** The same rule the roadmap already applies to Laigter — absent unless
installed, degrading rather than breaking — and for the same second reason:
Perforce and UVCS are proprietary, and running their client as a separate process
keeps the licences apart. **Never link, never vendor, never ship a client.**

### The realisation: locking is the feature, not history

Git cannot say *"I have this open, do not touch it."* Perforce and UVCS can, and it
is the mechanism game studios actually run on:

- Perforce marks binary assets `+l` (exclusive open) through a **typemap**, so
  opening one for edit takes a global lock. The Perforce documentation names 3D
  models and other digital assets as the typical case, and recommends exactly this
  typemap when configuring against Unity, Unreal or Godot.
- UVCS has **Smart Locks**, which additionally check you are on the latest version
  before granting the lock.

This matters to Lightbox more than history does, and it settles two open questions:

1. **The `PngBase64` diff hole shrinks from a problem to a note.** Under a locking
   workflow nobody merges a drawing — they take the lock, edit, and submit. A
   baseline that diffs unreadably is a nuisance in a git repository and almost
   irrelevant in Perforce. Still worth measuring, but it stops being a reason to
   change the on-disk layout.
2. **The most valuable integration is not commit and push.** It is *"who has this
   checked out"* on the row, and *take the lock* before you start painting. Two
   artists opening the same walk cycle is the failure that costs a day, and it is
   the one thing a panel can prevent outright rather than help resolve.

So the ascending order of cost, restated for a locking backend:

1. **Show lock state per row** — free of consequence, and the highest value thing
   on this page: unlocked, locked by you, locked by someone else and who. Composes
   with asset status the same way git status does: one says where it is in
   production, the other says whether you may touch it.
2. **Take and release a lock**, and **check out** (which in Perforce is what makes
   a read-only file writable at all — an artist on Perforce cannot save without
   it, so this is closer to essential than to convenient).
3. **Submit** — last, optional, and the same restraint as git: no branching, no
   merging, no conflict UI.

### The parts that will bite, recorded before they do

- **Lightbox does not own the workspace root.** A `.lbproj` normally lives *inside*
  or *beside* an engine project already under version control, so the integration
  must find the enclosing workspace rather than assume the project folder is one.
  On Perforce that is `p4 -ztag info`; on UVCS the workspace is discovered upward
  from the path.
- **Read-only files are the Perforce norm.** Anything that writes — save, autosave,
  auto-export — has to expect a read-only target and say "check this out first"
  rather than throwing an IO error at an artist. That is a change to the *save*
  path, not only an addition to a panel, and it is the most invasive part.
- **Gluon is the mode these users are in.** UVCS's artist-facing client checks out
  part of a tree rather than switching branches. An integration that assumes branch
  switching is modelling the programmer's workflow, not the animator's.
- **None of it can be verified here**, and that is exactly the shape of failure the
  Unity importer just demonstrated: a write-only integration against an external
  tool cannot fail visibly on this side. So every backend records the CLI, the
  minimum version and the commands it uses, and gets one real run by hand against a
  live server before its box is ticked. That discipline is already a roadmap item;
  this is its second customer.

## What this is not

Not a production tracker. No assignees, no due dates, no comments, no
notifications. The moment those appear this is competing with the tool the studio
already uses, and losing. Status exists because the *exporter* needs it; the
producer-facing view is a side effect, not the goal.

## Order

1. **`AssetFolder`** — the record, nullable, plus the panel showing a tree. Nothing
   else is reachable without it and it is useful alone.
2. **Export scope and grouping** over that tree, per-animation default, with the
   path template.
3. **`DocumentRef.Status`** and the export filter, which is the payoff.
4. **Max-sheet-size splitting** for the grouped modes.
5. **Git status, read-only.** Measure the baseline-diff problem first.
6. Everything under *Version control* step 3, if it is still wanted by then.

Steps 1–3 are the workflow. 4 is a correctness guard on 2. 5 and 6 are a separate
feature that happens to belong in the same panel.
