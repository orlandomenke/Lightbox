# Scoped resources — palettes, references and everything else shared

Follows Q30, which settled that a character is a folder carrying character data
and that resources declared on a folder accumulate down the tree with the
nearest declaration winning ties. This works out what that means for the rest of
the shared machinery, driven by four workflows the owner supplied from game
development.

The headline, before the detail: **the four workflows need two different
mechanisms, not one — and the codebase already has both, each attached to a
single resource and unavailable to the others.**

---

## The four workflows, and what each one proves

### 1 · A knight with a deep hierarchy

```
characters/
  knight/                 ← palette, reference sheet, export config
    locomotion/           ← animations
    combat/
    abilities/
```

### 2 · A knight with a flat one

```
characters/
  knight/                 ← palette
    walk.lightbox         ← animations, directly
    run.lightbox
```

**These two differ only in depth, and that is the point of including both.** The
artist's gesture is identical — *the palette belongs to the knight* — and the
resolution has to be identical too. This settles three things:

- **Resolution walks up until it finds one.** Not "the parent", not "two levels".
  Depth is an organisational choice and must not be an authoring one.
- **Reorganising cannot break resolution.** Splitting `locomotion` into
  `locomotion/ground` and `locomotion/air` adds a level, and everything still
  resolves because the walk still reaches `knight`. This is the property that
  makes the folder tree safe to rearrange, and it is the strongest argument
  against the *explicit per document* option that Q30 rejected — that one breaks
  precisely here.
- **A resource is declared once, where it conceptually belongs.** Never
  re-declared per subfolder, and never declared on each document.

### 3 · An environment reference used by everything

> *"One large environment document outlining all environments as reference. Both
> the environment assets and the characters could benefit, so project-wide
> distribution proves valuable."*

```
environments/
  overview.lightbox       ← lives here, must be visible everywhere
characters/
  knight/combat/attack.lightbox   ← wants to draw against it
```

**Cascade cannot express this.** `environments/` is not an ancestor of
`characters/knight/combat/`, so no amount of walking up reaches it. This is a
*sideways* reach and it is a different mechanism.

The naive fix — declare it at the project root — is wrong for a reason worth
stating: the document **belongs** in `environments/`. Filing it at the root to
make it visible would make the tree lie about what the thing is. So:

> **Where a resource lives and how far it reaches are two different
> properties, and this workflow is the proof.**

### 4 · A sword in the asset library

> *"A tool in the game and environmental storytelling. Characters, environments
> and props may want to use it. Project-wide distribution."*

Structurally identical to 3, and **already solved** — by symbols. From
`Doc.Symbols`, unedited:

> *A symbol normally lives on the **project**, above the animations that place
> it; that is what makes editing **the sword** once change every animation
> holding it.*

The existing design already reached this workflow, with this example. What it
cannot do is the opposite: there is no way to scope a symbol to the knight.

---

## Before any of it: this mechanism has been built three times already

Checking the design against the roadmap turned up the thing that should govern
it. **Three shipped features independently invented a scope chain, and every one
of them landed the same two properties.**

| Feature | Roadmap | The chain | Precedence pinned by | Renders-alone pinned by |
| --- | --- | --- | --- | --- |
| **Brush tips** | Pillar 0, *"A tip library, scoped like palettes: project, user, and inlined on export"* | user → project → inlined into the document on export | `AProjectTipComesBeforeAUserTip` | `DeletingFromTheLibraryCannotChangeADrawing` |
| **Symbols** | Pillar 3, *"Global and project symbol scopes — a personal library beside the project's"* | global library → project, copied on place | `PlacingAGlobalSymbolCopiesItIntoTheProject`, `EditingALibrarySymbolDoesNotReachIntoAProjectThatPlacedIt` | `TheProjectRendersWithTheLibraryGone` |
| **Palettes** | Pillar 1, *"Shared palette across a character's animations"* | project → character → document | `TwoAnimationsUnderOneCharacterPaintFromOnePalette` | colour stored literally beside `SwatchId` |

Three consequences, and they are worth more than anything I would have designed
from scratch:

1. **The determinism boundary is not a proposal, it is already the rule and
   already tested.** `DeletingFromTheLibraryCannotChangeADrawing` and
   `TheProjectRendersWithTheLibraryGone` are the same assertion twice, written by
   whoever built each feature without a shared statement to point at. The
   section on it below is a *restatement*, not a new constraint — which is the
   strongest possible position for it.
2. **The tip item literally says "scoped like palettes".** The intent to unify
   was written down; what was missing was a mechanism to unify *onto*.
3. **There is already a verb for moving a resource up a scope: promote.**
   `PromotingCopiesUpAndKeepsTheId` is symbols' existing gesture, and it is
   exactly what "publish this palette project-wide" needs. Reuse the word.

**And one correction this forces on the model below.** Tips and symbols scope on
a **user ↔ project** axis — a personal library that outlives any one project.
Q30 adds a **tree** axis, folder depth within a project. These are different axes
and both are wanted: a personal tip library is not a folder, and a knight's
palette is not a machine-level preference. So the full chain is

```
user / global library  →  project  →  folder path (walk up)  →  document
```

with nearest winning throughout. Designing only the tree axis would have stranded
the two features that already ship the other one.

---

## The finding: two mechanisms, and we have one of each

| | Reach | Today | Serves |
| --- | --- | --- | --- |
| **Cascade** | ancestor → descendants | palettes (via `Character.PaletteId`), references (via `Character.References`) | workflows 1, 2 |
| **Publish** | anywhere → everywhere | symbols, `Manifest.Palettes`, `Manifest.Brush`, `Manifest.Tips` | workflows 3, 4 |

Both exist. Neither is available to the other's resources:

- **Symbols are project-wide and cannot be scoped down.** Every symbol in a
  project is offered to every document, which is right for the sword and wrong
  for a knight-specific prop in a project with forty characters.
- **Palettes and references are scoped and cannot be published.** They attach to
  a character, so the environment reference in workflow 3 has no way to reach the
  knight.

So the redesign is not inventing a mechanism. It is **making the two that exist
available to everything, and letting one declaration choose between them.**

### The model

A shared resource is declared at a **scope** — any folder, or the project root —
and carries a **reach**:

| Reach | Meaning | Default? |
| --- | --- | --- |
| `Subtree` | visible to documents at or below the declaring folder | **yes** — writes no key |
| `Project` | visible to every document in the project | opt-in |

Resolution for a document is: walk from the document's folder to the root,
accumulating every declaration; then add every `Project`-reach declaration from
anywhere. Nearest declaration wins ties, and a `Subtree` declaration nearer the
document beats a `Project` one — locality is the tie-break, so the knight's own
red can override the studio's red without unpublishing anything.

Declaring at the root with `Subtree` reach and declaring anywhere with `Project`
reach converge on the same visibility, which is a consistency check rather than
a redundancy: the root's subtree *is* the project.

**`Subtree` is the default because declaring should be cheap and local, while
publishing is a claim on everyone's picker.** It also satisfies *optional means
absent*: an ordinary declaration writes no reach key at all.

---

## The character sheet

**The record is already generic. Only its scope and its label are not.**

```csharp
ReferenceSheet { Name, Views[] }
ReferenceView  { Name, Width, Height, Layers[] }   // static; no timeline
```

Nothing in that is about characters. It is *a named set of views, each a static
layer stack* — the character-ness lives entirely in the default view names
(Front, Side, Back, Expressions) and in the UI calling it "Character sheet".
That is good news and it means the redesign is mostly about **where a sheet can
live**, not about restructuring the type.

Two real problems, both of scope rather than shape:

1. **A sheet lives in `Doc.ReferenceSheets`** — inside one document. So it
   cannot be filed anywhere, cannot be shared, and workflow 1's *"a character
   sheet at the knight folder is valuable as all knight files have direct access
   to it"* is unbuildable.
2. **`Character.References` is `List<string>`** — paths, attached to a character.
   Workflow 3's environment document cannot be one.

### What a reference actually is

Workflows 1 and 3 want reference art of **two different shapes**, and a generic
system has to hold both:

| | Shape | Example |
| --- | --- | --- |
| **Multi-view sheet** | several small views, each its own canvas | Front / Side / Back / Expressions |
| **A document** | one large drawing, ordinary canvas | the environment overview |

So the generalisation is not "make the sheet bigger". It is:

> **A reference is a declaration at a scope that names something to draw
> against.** What it points at is one of: a multi-view sheet authored in place,
> an ordinary document in the project, or an imported image.

`Character.References` is already a pointer list — path-only and
character-scoped. This widens what it can point at and where it can hang, which
is a smaller change than it sounds.

**Recommended naming:** keep `ReferenceSheet` for the multi-view record, because
it is accurate and already generic, and introduce `ReferenceRef` for the
declaration that points at one. Renaming the record to something like
`SubjectSheet` would churn every call site to fix a problem that lives in the UI
label — *"Character sheet"* becomes *"Reference sheet"* there, and the ninth
panel keeps working.

---

## The sweep: other document-bound systems

Everything currently on `Doc` or pinned to one project-wide slot, judged against
the same question — *would an artist want this shared by a folder?*

| Resource | Today | Verdict |
| --- | --- | --- |
| **Gradients** `Doc.Gradients` | per document | **Yes, strongest of the additions.** A gradient is a named colour resource an artist reuses, exactly like a palette; today one made for the knight's shield cannot be used by the next animation. Same argument, same fix, and it is odd that palettes and gradients are already asymmetric |
| **Guides** | per document | **Yes.** A character height guide at the knight folder so every knight animation shares it — the roadmap already carries `[?] Character height guide` and this is what it wants to be |
| **Export configuration** | per document | **Yes.** The knight exports at one cell size, the boss at another; per-folder is exactly the grain a sprite pipeline needs. Named in the owner's Q30 answer |
| **Brush + tips + textures** `Manifest.Brush`, `Manifest.Tips`, `Doc.BrushTips`, `Doc.Textures` | project-wide library, per-document raster | **Yes, as libraries.** The manifest comment already says *"Pillar 1 says a character's work shares one palette and one brush set"* — scoped was always the intent and project-wide was the only scope available. The raster must keep travelling into each document (see the boundary below) |
| **Templates** `Doc.IsTemplate` | **shipped** — Pillar 6, *"Animation templates — a document in the project marked as a template"* (`IsTemplate`, `TemplateId`, `NewFromTemplate`, `ANewDocumentFromATemplateIsACopyNotALink`) | **Yes, and it is the sharpest of the small ones — and cheaper than it looked.** The template machinery exists; what is missing is a *default* template per scope, so workflow 1's `locomotion` folder makes new animations that already know what they are. One field on the scope, resolved by the same walk |
| **Frame markers / tags** | **shipped** — Pillar 6, `FrameMarker`, `IsEvent`, `Note`, and *"Frame tagging — this is frame markers, shipped"* | **Yes, as a shared vocabulary.** Markers, notes and event flags all exist per document; what does not is an agreed *set of names* (*anticipation, contact, breakdown*) so a tag means the same thing across forty animations and can be queried. This is also the substrate the market-research item *"Expression and pose metadata tagging"* needs — that item is unbuildable while every document invents its own words |
| **Timing presets** | **shipped app-level** — Pillar 3, `TimingPresetStore`, `ASavedPatternPersistsAndComesBackOnTheNextLaunch` | **Probably, and it already has the wrong half of the axis.** Persisting per launch is the user tier; "this show is on 2s" is the project tier and cannot be said. Low value only because the app-level default is usually right |
| **Palette folders** `Doc.PaletteFolders` | per document | Follows palettes wherever they go — not a separate decision |
| **Onion skin settings** | per document | **Marginal.** Mostly a per-artist preference; a folder-level default would rarely be reached for |
| **Camera** | per scene | **No.** Not a shared resource — it is authored content belonging to one scene |
| **Clip regions** `Doc.ClipRegions` | per document | **No, and this one is a defect if changed.** See below |

### The one that must not move

`Doc.ClipRegions` is invariant 3: a selection is a content-hashed entry
referenced by `Stroke.ClipId`, and it is **provenance** — the record of what a
stroke was actually painted under. Sharing it across documents would mean a
stroke's clip could be edited from outside the document that owns the stroke,
and a reload would render something the artist never drew. It stays per
document, and the reason is worth keeping written down because *"it is a
dictionary keyed by id, like gradients"* makes it look eligible.

---

## The determinism boundary

**This is the constraint that makes the whole design safe, and getting it wrong
would break invariants 1 and 4 together.**

> Scoped resources are a **library to choose from**, not something rendering
> reads. Resolution happens when an artist picks, not when a frame renders.

When a stroke is painted it captures what it needs — colour, tip raster, texture,
gradient — into the document. Otherwise moving a document between folders would
change its pixels, which breaks invariant 1 (a reload renders the same image) and
invariant 4 (settings that reach pixels are stored per stroke).

The codebase already states this for tips, and the sentence should govern
everything added here:

> *The raster still travels into each document that paints with it — this is a
> library to choose from, not what a drawing renders out of.*

### Palettes are the deliberate exception, and they need a guard

Live recolour is a *feature*: `Stroke.SwatchId` links a stroke to a swatch so
changing the palette recolours existing art. That is render-time resolution on
purpose, and it means palettes alone can change a document's appearance based on
where it sits.

Mostly this is safe — the colour is stored literally as well as linked, so a
stroke whose palette is gone still renders. The hazard is narrow and worth
pinning:

> Drag `attack.lightbox` from `knight/` to `goblin/`, and if a swatch id resolves
> in the new scope, the art recolours.

Swatch ids are generated, so independently authored palettes will not collide.
**Duplicated palettes will** — and duplicating a palette to tweak it is the
obvious thing to do. The cheap fix is to record the palette id alongside the
swatch id on the stroke, so resolution is unambiguous and a missing palette falls
back to the literal colour rather than to a stranger's swatch of the same id.
`AMovedDocumentKeepsItsColours` is the test.

---

## Suggested phasing

Not a schedule — an ordering, so each step is landable alone and nothing is left
half-migrated.

1. **The scope record and resolution.** `ResourceScope` on `ProjectFolder`,
   walk-up accumulation, the four-tier chain, nearest wins. No resource moves
   yet; the mechanism is testable on its own. **Model it on `SymbolScopes`**,
   which already resolves a two-tier chain and has the tests to copy.
2. **Palettes onto it** — Q30 answered them, and Pillar 1's *"Shared palette
   across a character's animations"* (`TwoAnimationsUnderOneCharacterPaintFromOnePalette`)
   is the behaviour that has to survive with a folder in place of the character.
   Plus the palette-id guard above.
3. **References**, widening `Character.References` into a scoped `ReferenceRef`
   pointing at a sheet, a document or an image. Workflows 1 and 3 close here.
   Touches Pillar 1's *"Character workspace"* and Pillar 0's *"Reference image
   panel"* (both `ReferenceSheet`, `ReferenceTabTests`), and it is where the UI
   label stops saying "Character".
4. **Gradients, guides, export config, default templates** — *landed in part,
   2026-08-06, and the split is the finding.* Gradients and default templates
   joined by naming a kind and nothing else, which is the evidence step 1 was the
   right shape. **Guides and export configuration could not**, for the same
   reason both times: neither has a project-level record with an id to point at.
   A guide lives on a document and nowhere else; export settings are chosen per
   export. Scoping needs something that outlives one document, so those two want
   that record built first — they are not a line of resolver away, and the next
   person to try will otherwise rediscover it.

   **Guides followed on 2026-08-06**, once `GuideSet` existed to point at: an id,
   a name and the guides themselves, held on the manifest and absent until one is
   made. The roadmap's `[?] Character height guide` is that and a declaration and
   nothing else. Note the boundary holds the ordinary way here rather than the
   palette way — guides are *copied into* a document when used, not resolved at
   render time, because a drawing whose snapping changed when it was dragged into
   another folder is the defect scoping exists to prevent.

   **Export configuration — see the section below.** I twice described this as
   "there is no export-settings record", which was wrong and is corrected there:
   `ExportPreset` exists and is good. It is in the wrong assembly and is missing
   one field. Two roadmap items close as a side effect:
   `[?] Character height guide` becomes an ordinary guide set declared on the
   knight folder, and Pillar 6's shipped template machinery gains the per-scope
   default it was missing.
5. **Symbols gain the tree axis.** A *narrowing* of shipped behaviour rather than
   a widening, so it comes last. Pillar 3's *"Global and project symbol scopes"*
   already has the user↔project half; this adds folder depth beneath it. A symbol
   with no declared scope stays project-wide, which is what every existing
   project means — and `TheProjectRendersWithTheLibraryGone` must keep passing
   untouched.

**Two that sit outside the ordering.** Brush libraries — Pillar 0's tip library
already says *"scoped like palettes"*, so it joins whenever step 1 exists — and
the frame-tag vocabulary, which blocks nothing but unblocks the market-research
metadata item.

### Roadmap items this design is the missing piece of

Worth listing, because several unbuilt items are waiting on exactly this and do
not say so:

| Item | Pillar | Why it needs this |
| --- | --- | --- |
| `[?] Character height guide` | 6 | It is a guide set declared on a scope. There is no other thing it could be |
| `[?] Sprite atlas generation across characters` | 5 | "Across characters" is a scope question — which subtree's assets pack together |
| *Expression and pose metadata tagging* | 1 (research) | Needs a shared tag vocabulary or every document invents its own words |
| *Studio dashboard* | 6 | Reads status across the tree; the folder walk is the same traversal |
| *Collaborative palette sync* | Tier 3 | Cannot sync a palette that has no scope to be synced *at* |
| *Style guide enforcement* | Tier 3 | A style guide is a scoped resource plus a checker |
| *One registry of features, defaults per project type* | Architecture | Sibling mechanism, same shape — derived defaults, nearest wins, nothing gated. Worth building the two consistently rather than discovering later that a project type and a folder disagree about what a default is |

## What this does not settle

- **Whether a scope can decline an inherited resource.** "The knight uses none of
  the studio palettes" has no expression here. Probably wanted eventually;
  deliberately not invented now, because every guess at a negation syntax before
  someone needs one has been wrong.
- **Migration.** Q30 answered *new projects only*, so existing projects keep
  character palettes and `Character.References` — and the code keeps both paths.
  That is recorded in Q30 with the consequence named.


---

## Export, and what the project structure changed about it

**A correction first, because I got this wrong twice.** I said there was no
export-settings record and that one would have to be designed. There is:
`ExportPreset` in `Lightbox.App/Services`, with `Target`, `Trim`, `Pack`,
`Columns`, `Padding` and `Background`, a `ExportPresetStore` that persists them,
an `ExportRunner` that is deliberately thin — *a preset in, files and a report
out* — and an `AutoExport` that already decides *when*. Pillar 5's last mile is
built. The plan below is therefore much smaller than the one I implied, and it
is mostly about moving one type and adding one field.

### What the new structure actually broke

Nothing is broken in the sense of failing. What changed is that **three of the
export design's assumptions were true when a project was a flat pile and are not
true now.**

| Assumption | Was fine because | Now |
| --- | --- | --- |
| A preset is a **user** setting, held in `ExportPresetStore` beside the app's other preferences | Every project exported roughly one way | The knight exports at one cell size and the boss at another. A preset is a property of *part of a project*, not of the person |
| Export is a thing you do **to the open document** | There was one obvious document to mean | An artist wants *export this folder*, and "the knight's locomotion cycles" is now a thing that can be named |
| Auto-export is one **global** on/off (`AutoExportSettings.Enabled`) | One rule could describe a whole project | A production wants finished shots exported and work-in-progress not, which is a rule about a subtree rather than about the application |

### The plan, in the order the pieces depend on each other

**1 · Give `ExportPreset` an `Id` and move it to Core.** It is a record of enums
and numbers with no UI dependency, so the move is mechanical. `Name` is what it
has today and a name is not an identity — two folders can reasonably both have a
preset called *Sheet*, and a scope declaration has to point at one of them.

**2 · Scope it.** `ExportScopes` beside the four that exist, kind `export`,
resolved with `Nearest` rather than `Resolve` — a document exports one way at a
time, so accumulating presets would be offering a choice nobody made. Q30's
migration hinge applies unchanged: a project that declares none keeps using the
user's store exactly as it does today.

**3 · Export a scope, not just a document.** `ExportRunner` already takes *a
preset and a path*, so this is a caller change rather than an engine one: walk
the folder's documents, run each through the resolved preset, and return one
report. **The interesting part is what it does about mixed results** — thirty
documents where two are missing files is a report, not an exception, and
`ExportRun` already carries `Omitted` and `Suspected` for exactly that kind of
partial truth.

**4 · Let auto-export be a rule on a scope.** `AutoExportSettings` becomes
declarable per scope rather than only globally — *finished shots under `act-1/`
export on status change; nothing else does*. `AutoExport.Decide` already takes
settings as an argument, so it needs a different caller rather than different
logic.

### Three decisions I should not make alone

- **Is a preset per target, or one preset with per-target overrides?** A studio
  shipping to Unity *and* Godot wants both from one authoring pass. Per-target
  presets are simpler and duplicate cell size and trimming; one preset with
  overrides keeps the shared parts shared and is a more complicated record.
  I lean **per-target presets plus scoping**, because the scope already gives
  the sharing — declare the common one on the character, the Godot-specific one
  on the folder that needs it — and that avoids inventing an override mechanism
  next to one that exists.
- **Does cell size belong to the preset or to the document?** Today's `Trim` and
  `Pack` are preset-side, which is right for a sheet. A per-document override
  would let one oversized attack frame break a character's grid, which is
  exactly the consistency the *assets* output target exists to protect.
  I lean **preset only**, and let a document that genuinely differs live under a
  folder with its own preset.
- **What does "export this folder" do about nested folders?** Recursive is the
  obvious reading and is what an artist means by *export the knight*. It is also
  how somebody accidentally writes four hundred files. I lean **recursive with
  the count in the confirm** — the number is the safeguard, not a checkbox.

None of the three blocks step 1 or 2, which is why they are worth landing first.


## What an export scope actually is, and the two axes it is not

Asked directly — *what is a scope, for export* — and the answer is that "scope"
was doing three jobs at once:

1. **Where the settings live** — which folder declares the preset.
2. **What was selected** — the folder you asked to export.
3. **What becomes one file** — is the knight one sheet, or eight?

Only the third constrains anything. A sprite sheet is *one artifact from many
documents*, so everything in it must share a cell size — you cannot resolve
settings per document and then pack them together. A PNG sequence is one artifact
per document and does not care.

> **An export scope is the boundary of one deliverable**, and the declaration is
> that boundary. Declaring a sheet preset on `knight/` says *everything under
> here packs into one knight sheet*; declaring it on `knight/locomotion/` instead
> makes locomotion its own.

Nearest-wins then gives the grouping for free, and selection becomes independent:
exporting `characters/` finds every scope at or under it and produces one artifact
each. No declaration anywhere means per-document, which is today's behaviour and
the migration path. It also makes the assets-versus-shots split concrete — for
assets the grouping *is* the point, and for shots the deliverable is naturally
per-shot so grouping never arises.

### Structure is not the only axis, and on its own it is rigid

The owner pushed on this and was right: grouping answers *how things package* and
says nothing about *when they are allowed out*. Both of the real workflows —
"let me test one animation" and "when it is ready, update everything" — are about
**state**, not structure.

`DocumentRef.Status` already exists (Design, Draft, In development, Review,
Ready, Reopened) and `AutoExport.Decide` already fires on a status change. The
state axis is largely built; what it lacks is a scope to belong to.

So a preset carries three things rather than one:

| | Comes from | Answers |
| --- | --- | --- |
| **Grouping** | the tree | what packages together |
| **Filter** (`IncludeStatuses`) | status | what is allowed into the artifact |
| **Trigger** | a status change | when it is rebuilt |

**Testing one animation is a different destination, not a smaller export.** If a
test overwrites the shipped sheet, looking at one cycle has broken the build. So
it is its own verb: *Test export* uses the resolved preset, forces grouping to
per-document, ignores the filter, and writes somewhere scratch. Optionally
automatic — *In development → test export* — which is what puts a fresh frame
where a running engine can hot-reload it.

**"When ready, update everything" is where grouping earns its place.** The
trigger is per document and the effect is per artifact: one animation reaching
Ready means the sheet holding it is rebuilt. Grouping is what tells you *what to
rebuild when one thing changes*, which is a build graph, and without it a status
change cannot know what it invalidated.

### Staleness: the framework already exists, in symbols

The awkward case is a shipped sheet where one animation goes back to
**Reopened**. The filter says it is no longer eligible, so a rebuild would drop
it and leave a hole in a sheet an engine is already loading.

`docs/DESIGN-symbols.md` S7 already solved this shape. `Symbol.Version` is an
integer bumped on every edit; `SymbolPlacement.SeenVersion` records what the
placement was made against; the two differing **is** staleness. Nothing else is
needed — no history, no diffing, no store.

The same pair generalises without inventing anything:

- `DocumentRef.Version`, bumped when the document is saved.
- The artifact records the version of each document it was built from.
- Any difference means **the artifact is stale**, named down to which documents
  moved.

So the Reopened case answers itself: **the artifact keeps what it had and reads
stale.** Removing work from a deliverable because somebody reopened it for polish
is the kind of helpfulness that breaks a build at 2am; the staleness is visible
where the hole would not be.

This is also the lightweight versioning the owner asked for, and it arrives as a
side effect rather than as a feature. Two integers per document give: export
staleness, the market-research *character version tagging* item, and
*frame-to-version linking* — all three of which currently assume a versioning
system nobody has built, when the pattern has been shipping in symbols since
Pillar 3. **Worth stating plainly: this is not a new subsystem, it is
`Symbol.Version` applied to a second kind of thing.**

It is deliberately *not* snapshots. Pillar 6's *version snapshots* and *undo
history browser* are a different feature with a different cost — they store
document states, and they are blocked on the undo record becoming data. A
version integer is not blocked on anything.

### Revised step 1

`ExportPreset` moves to Core and gains, in one go rather than one bug report at a
time: `Id`, `Grouping`, `IncludeStatuses`, and a trigger rule. `DocumentRef`
gains `Version`. Both default to the behaviour that exists today, so a project
that declares nothing exports exactly as it does now.


## The three export decisions, answered 2026-08-06

All three went to the recommendation, so the reasoning below is the *why it
holds* rather than a record of disagreement — worth writing down because the
alternatives are each reasonable and somebody will propose them again.

### Per-target presets, sharing by scope

A preset produces one thing for one engine, and a character shipping to Unity
and Godot declares two — the common one where it belongs, the engine-specific
one on the folder that needs it.

**The reason this beats an override mechanism is that it does not add a second
precedence rule.** Scoping already answers *which value applies here* with
nearest-wins; per-target overrides would put a second answer beside it, and
"why did I get this cell size" would have two places to look instead of one. The
cost is real and accepted: a character shipping to two engines with identical
geometry writes the geometry twice.

Rejected, and worth naming: *one preset with per-target overrides* keeps shared
parts visibly shared, which is the honest expression of the intent — it loses on
the second precedence rule alone.

### Cell size lives on the preset, and nothing below can override it

A document cannot say its cell is bigger. **The whole value of the assets output
target is that frame bounds stay consistent**, and an override lets one document
break the grid every other frame in the sheet depends on — discovered at import
time in the engine, which is the worst available place to find it.

The exception is still expressible: a document that genuinely differs lives under
a folder with its own preset. That costs a folder rather than a checkbox, and
that is the right price, because it makes the exception visible in the tree
instead of hidden in one document's settings.

Rejected: *size the cell to the largest frame*. It never clips and nobody
maintains a number, but the memory cost of an entire sheet is then set by its
worst member and changes without anyone editing a setting — a performance
characteristic that moves on its own is worse than one that is wrong and stated.

### Exporting a folder is recursive, and the confirmation carries the count

*Export the knight* means the knight, including everything filed under it. The
confirmation says how much — "3 folders, 47 documents, 4 sheets" — and **the
number is the safeguard rather than a checkbox**: a wrong scope reads as an
obviously wrong count, which a yes/no prompt cannot convey.

This is the same argument B87's delete confirmation already makes — *"Delete
'Art' and the 1 folder and 1 document inside it?"* rather than *are you sure* —
so the two dangerous bulk operations in the application say the same kind of
thing in the same way.

Rejected: *immediate children only* is never surprising and is not what the words
mean, so the common case needs three exports and a document filed one level
deeper is silently missed. *No confirmation* is right for the twentieth export of
a polish pass and offers no moment to notice the project root was selected.
