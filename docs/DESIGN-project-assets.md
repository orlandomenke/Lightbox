# A project as a game's asset set

Status: **designed, nothing built.** This is the answer to three questions asked
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

**Path template** is how a studio gets its own layout rather than ours:
`{project}/{folder}/{animation}`, `{folder}_{animation}`, `{path}` for the full
nested path. This is the "custom method" half of the question, and it is cheap
because it is string substitution over a tree walk.

## Asset status, which is the best part of the idea

`DocumentRef.Status` — nullable, absent by default:

`Design` → `Draft` → `InDevelopment` → `Review` → `Ready`, plus `Reopened`.

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

## Version control: be friendly to it, do not reimplement it

Lightbox should not become a git client. What it can do, in ascending order of
cost, and only the first two are certain:

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
