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
| **Templates** `Doc.IsTemplate` | a document flag | **Yes, and it is the sharpest of the small ones.** Workflow 1's `locomotion` folder wants new animations in it to start from the locomotion template. A scope declaring its default template turns "new document here" into something that knows what it is |
| **Frame tags / markers** | per document | **Yes, as a vocabulary.** A project-level or folder-level set of tag names (*anticipation, contact, breakdown*) so tags mean the same thing across animations and can be queried. This is also what the roadmap's expression/pose metadata item needs underneath it |
| **Timing presets** | app-level store | **Probably.** "This show is on 2s" is a real per-project statement. Lower value than the rest because the app-level default is usually right |
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
   walk-up accumulation, `Subtree`/`Project` reach, nearest wins. No resource
   moves yet; the mechanism is testable on its own with palettes alone.
2. **Palettes onto it**, since Q30 answered them and they are the one with a
   working live path to compare against. Plus the palette-id guard above.
3. **References**, widening `Character.References` into a scoped `ReferenceRef`
   that can point at a sheet, a document or an image. Workflows 1 and 3 close
   here, and the UI label stops saying "Character".
4. **Gradients, guides, export config, templates** — mechanical once 1 exists,
   and each is independently landable.
5. **Symbols gain a scope**, which is a narrowing of existing behaviour rather
   than a widening, so it comes last and needs its own compatibility thought: a
   symbol with no declared scope stays project-wide, which is what every existing
   project means.

Brush libraries and the frame-tag vocabulary sit outside this ordering — both are
wanted, neither blocks anything else.

## What this does not settle

- **Whether a scope can decline an inherited resource.** "The knight uses none of
  the studio palettes" has no expression here. Probably wanted eventually;
  deliberately not invented now, because every guess at a negation syntax before
  someone needs one has been wrong.
- **Migration.** Q30 answered *new projects only*, so existing projects keep
  character palettes and `Character.References` — and the code keeps both paths.
  That is recorded in Q30 with the consequence named.
