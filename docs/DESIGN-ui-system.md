# Migrating to the designed UI

A visual design for Lightbox exists as a mockup: a colour system, a set of control
treatments, and a full application layout. This records what it asks for, what the
codebase already has that answers it, and the order the gap gets closed in.

> **The mockup is checked in at `docs/design/ui-reference.png`**, and it outlives
> this plan. When the last stage lands and this document is deleted, the image
> stays: it is the visual source of truth `.claude/quality/DESIGN.md` reads its
> rules off, and **ui-critic** consults it on any change to a treatment.
>
> Two stages were built before it was in the repository, from a description of
> it, and both had to be redone — the panel tab was given a section tab's accent
> underline, which made a docker header claim to divide the application. Reading
> the image would have caught it in a minute. **Measure it rather than
> describing it**: a pixel column down the mockup's active tab settles a gradient
> that no amount of arguing about it will.

> **This is a migration plan, not the design system.** The design system is
> `.claude/quality/DESIGN.md` — one file, and it stays the one file. Each stage
> below lands its rules *there* and its numbers in `Styles/`. This document is
> time-bound: when the last stage lands it describes history, and it should be
> deleted rather than maintained. A second permanent system of record is exactly
> the failure that makes a design system stop being believed.

## The one thing that makes this tractable

**Most of the design is a re-presentation of machinery that already exists.** The
part that is genuinely missing is small and specific. Sorting one from the other is
the whole job of this document, because a migration that treats the mockup as
entirely new work would rebuild the docking system to get a tab strip.

| The design asks for | What is already here |
| --- | --- |
| **PAINT / ANIMATE / COMPOSITING** tabs | The workspace system. Six workspaces, a store, save/reset/fork, per-project-type defaults — all of it, behind a dropdown instead of tabs |
| Layers / Channels / History tabs in one slot | The docker **switcher**: picking another panel makes the two trade places, so no panel is ever open twice |
| Slider paired with a numeric field | `Density.axaml` already fixes one field width and one track length so a bar reads as a column of columns |
| Numeric fields with no spinners, drag-to-scrub | Already the rule, set once in `Density.axaml` |
| A defined colour system | **Nothing.** 120 hex literals across 60 distinct values, 6 resource references |
| Navigator, Brushes grid, Brush Settings panels | Not as panels. The brush library is a separate *window* |
| Xsheet / Dope Sheet / Graph Editor | The Timeline panel exists; `Xsheet` exists only as a feature key |
| The icon set | Nothing, and deliberately so — see *Deferred* |

## Decisions taken before planning

- **The three tabs are workspaces.** Three stay visible as tabs with a dropdown for
  the rest, so the existing six survive and the design's prominence is gained without
  a second concept. Workspaces stay global rather than per-project — the rule that a
  layout belongs to the artist and not the artwork is unaffected.
- **Scaling means density.** The design is tighter than what ships today. That is a
  retune of the existing scale, not a high-DPI defect.
- **Branding is deferred entirely.** No logo, no icon set, and the splash keeps its
  placeholder. `ROADMAP.md` argues the app should draw its own icons once the vector
  tooling exists, and that argument is not weakened by having a mockup — a drawing
  application that cannot make its own icons is telling you something.

## The order, and why it is this order

Each stage is landable alone and leaves the app working. The order is a dependency
chain, not a priority list: every later stage is cheaper once the earlier ones exist.

### 1 · The colour system becomes tokens

**The foundation, and the only stage that blocks every other one.** Today the palette
is 120 literals in 60 values, so "make the app look like the design" is currently 120
edits with no way to check they agree. One token layer turns it into one file.

The design gives six core colours and six accents. It does **not** give a border or
divider colour, and the app leans on borders heavily — so one is derived and that fact
is written down rather than smuggled in.

A guard test keeps new literals out, or the layer decays the moment someone is in a
hurry.

### 2 · The control treatments

The mockup's *UI Treatment* block is a component library: four button ranks (Primary
carries the coral→magenta gradient, then Secondary, Tertiary, Ghost), toggles and
checks, the slider-plus-value pair, tab strips, and four badge kinds.

Style-only — no behaviour changes. Badges land on the status surface that already
exists rather than becoming a new mechanism.

### 3 · The density retune

The design is tighter than the shipped scale. Done **after** 1 and 2 deliberately:
retuning against components that are about to be restyled means measuring twice.
`.claude/quality/DESIGN.md` holds the scale and is the thing edited — per-view tweaks
are what the scale exists to prevent.

**Measured, not estimated.** Text renders at the same size in the reference and in
the app (10 px vs 11 px cap height), so the two are directly comparable — and the
rows are not: the reference runs a **20–22 px pitch** in the layers list, the timeline
tracks and the brush settings alike, where the app renders **33–34**. Roughly 1.5× the
vertical space for the same content.

> **The reference's density is partly gated on deferred work, and that is worth
> knowing before anyone measures the mockup again.** Our row height is set by the icon
> tile, the tile is 26 because the icons are **emoji**, and `Density.axaml` already
> records that a 16 px emoji with an ascender *clips* in a 24 px tile. The reference's
> 21 px rows use vector icons. So the last few pixels are blocked on the icon set,
> which is deferred below on the vector tooling — and when it lands, the tile can go
> to 20 and the rows with it.

The stage therefore runs in three branches, tightest-honest rather than
tightest-possible:

1. **One scale.** Four sources disagreed — this document's table, `Density.axaml`,
   per-view overrides in `MainWindow.axaml` that beat both, and two C# constants. The
   overrides won, so the scale described sizes no control had. No visual change; a new
   `DensityScaleTests` fails when they diverge again.
2. **Docker density.** The rows an artist reads, 33 → ~28.
3. **Bar density.** The strips above and below the canvas, where the 35–38 px pitch
   is and where the canvas gets the most back.

### 4 · Workspace tabs

The picker moves from a dropdown at the right of the tool options bar to a tab strip,
three visible plus an overflow. `WorkspaceStore` is untouched; this is a view change.

### 5 · Panels, and the dockers-versus-bar question

The mockup keeps a **thin top bar** (opacity, flow, a few toggles) *and* puts brush
settings in a left docker. That is a real answer to a question the app has not asked
yet, and it is the first stage that changes where things live rather than how they
look:

- Brush Settings becomes a docker with Brush / Stabilizer tabs.
- The brush library stops being a window and becomes a grid docker with search.
- Navigator is new.
- Channels and History are new, and **Channels needs a channels concept in the
  document model first** — it is not a panel waiting to be drawn. Deferred within
  this stage rather than promised.

### 6 · Timeline modes

Timeline / Xsheet / Dope Sheet / Graph Editor as tabs on the timeline panel. The
exposure sheet is the one with existing groundwork. The mockup's coloured per-track
keyframes map onto the accent palette from stage 1.

### 7 · The menu bar

File / Edit / View / Image / Layer / Select / Filter / Tools / Window / Help against
today's File / Edit / View / Help. Mostly re-homing commands that exist, and it comes
last because a menu is a map of the application — drawing it before the panels move
means drawing it twice.

## Deferred, with the reason

- **The logo and the icon set.** Blocked on the vector tooling by the roadmap's own
  argument, and confirmed as deferred by the owner. The splash keeps its placeholder.
- **Channels.** Needs a document-model concept before it can be a panel.
- **High-DPI behaviour.** Not what "scaling" meant here, and it would be a defect
  rather than a design change if it turns out to be wrong.

## What each stage owes

Every stage updates `.claude/quality/DESIGN.md` where it changes a rule, the manual
section for anything an artist sees, and carries its own tests. A stage that only
moves colours still changes what an artist sees, so the manual's screenshots-in-prose
descriptions have to keep up.
