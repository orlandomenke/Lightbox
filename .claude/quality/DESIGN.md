# Design system

The rules the UI is held to. Numbers live here, not in individual views — a
control that sets its own height is how a panel drifts.

**`docs/design/ui-reference.png` is the visual source of truth**, and this file
is the rules read off it. Where they disagree the image wins and this file is
wrong. It carries the brand, the palette with its hex values, the four control
ranks, both kinds of tab, the badges, and a full window showing how they sit
together — which is the part no written rule captures, because "what does this
look like beside the thing next to it" is the question a style guide cannot
answer about itself. **Look at it before proposing a treatment**, and measure it
rather than describing it: the panel-tab gradient below came off a pixel column
down the mockup's active tab, and the guess it replaced was wrong in a way
nobody would have argued with.

The tension this file exists to settle is **screen efficiency versus comfort**.
Lightbox is a drawing app: the canvas is the work, and every pixel of chrome is
taken from it. But an artist tuning a brush mid-stroke needs to hit a slider on
the first try. The resolution below is not "small everywhere" — it is *dense by
default, generous where a mistake costs a stroke*.

## The one rule that decides most arguments

> Chrome is measured against the canvas it takes from. If a control can be
> smaller without becoming harder to hit **for the way it is actually used**,
> it should be.

"The way it is actually used" is the part people skip. A layer row is scanned
and clicked once; it can be 24 px. A brush-size slider is dragged while
drawing, often without looking; it stays full width with a generous track. The
question is never "how small can this be" but "how small can this be *for its
job*".

## Sizes

One scale, implemented once in `src/Lightbox.App/Styles/Density.axaml`.
Anything not on it needs a reason in a comment **at the site in that file** —
not in a view, because a view that re-declares a size wins silently and the
scale is then describing controls that do not exist.

| Token | px | Used for |
| --- | --- | --- |
| `--row` | 24 | Field, combo, small button, list row |
| `--tile` | 26 | Icon button, overlay-bar tile — a square, and see below |
| `--bar` | 30 | The tool options bar's fixed row |
| `--tool` | 34 | Tool palette buttons — hit while drawing, so the largest |
| `--gap` | 4 | Between related controls in a group |
| `--gap-lg` | 8 | Between groups, and docker content padding |
| `--label` | 52 | Label column in a labelled-row layout, so rows align |
| `--field` | 64 | Numeric field beside a slider |

Font sizes: **12** in docker content, **11** in dense rows and option bars,
**10** for status and hints. Nothing smaller — below 10 the app stops being
readable on a laptop panel.

**`--tile` is 26 because of the glyph, not because of the scale.** 24 is the
tidier number and it does not survive contact with the content: the icons are
emoji, and a 16px emoji with an ascender does not fit a 24px tile once the
border and the line box are paid for. It *clips* rather than crowds, which is
the version nobody notices until it ships.

That makes it **the one entry on this scale waiting on something else.** A
docker row is `--tile` plus its padding, so the icons set the row height, and
the design reference's 21px rows are unreachable until the icon set replaces
the emoji with vector paths. `docs/DESIGN-ui-system.md` carries that dependency
so the next person measuring the mockup does not re-derive the blocker.

**Where these numbers were before, and why that mattered.** Four sources
disagreed: this table, `Density.axaml`, per-view overrides in `MainWindow.axaml`
that beat both, and two constants in C#. The overrides won, so the scale
described sizes no control had — `--tool` was written 30 here and rendered 34,
and the icon tile was written 24 and rendered 26. Both are now recorded at the
value that actually shipped, because in each case the view was right and the
scale had drifted away from it. `DensityScaleTests` parses this table and
`Density.axaml` and fails when they disagree, which is the check whose absence
let it happen.

## Controls

**No spinner buttons.** `NumericUpDown`'s up/down arrows cost ~20 px of width,
are never used (the artist types or drags), and make every numeric row taller
than it needs to be. `ShowButtonSpinner="False"` is set globally; do not
re-enable it locally. The field still parses, clamps and formats — only the
arrows are gone.

**Slider plus field, not slider or field.** A value that is explored by feel
(size, opacity, flow) gets both: the slider to drag, the field to type an exact
number and to *read* one. A value that is only ever set exactly (frame count,
canvas size) gets the field alone. A value with fewer than about six choices
gets buttons or a combo, never a slider.

**Button sizes are consistent by role, not by neighbour.** Three roles:

| Role | Size | Examples |
| --- | --- | --- |
| Icon | `--tile` square, padding `0` | ✕ close, ▲▼ reorder, ＋ add |
| Text | `--row` high, padding `8,0`, `MinWidth 70` | "Import…", "＋ Swatch" |
| Tool | `--tool` high, padding `4,0` | Toolbar tools, transport buttons |

Named from the scale rather than restated as numbers, because this table
restating them is how it came to disagree with the scale on every row at once:
it said icon padding `4,2` where the code had `0`, text `MinWidth 64` where the
code had `70`, and tool `30` where the code had `34`.

Two buttons that do comparable things must be the same size. A row of
`＋ Swatch` / `－` / `Import…` / `Export…` reads as a group only if the icon
buttons share one width and the text buttons share another. Mixed sizes inside
one bar is the most common way this app has looked unfinished.

**Emphasis is a rank, and it is a different axis from size.** Size answers *how
do I hit this*; emphasis answers *which of these should I reach for first*. They
compose — `Classes="text primary"` is a text-sized button carrying the primary
treatment — and they compose only because they stay disjoint. **A rank never
sets a size.** One that also set a height would silently override the role
beside it and the row would stop lining up, which is the failure above wearing
a different hat. `ControlTreatmentTests.ARankNeverSetsASize` holds the line.

| Rank | Treatment | When |
| --- | --- | --- |
| `primary` | Accent gradient, no border | The one thing you came to this view to do |
| `secondary` | Elevated surface, thin border | The ordinary button, and what most things are |
| `tertiary` | Outlined, transparent ground | Present, clearly not the answer — Cancel |
| `ghost` | No box until hover | Rows of them, where boxes would be a wall |

**One primary per view, and usually none.** The gradient means "this is the
thing", and a screen with three of them has said nothing. **The button Enter
presses is the one that looks like the answer**: `IsDefault="True"` and
`primary` say the same thing in two languages, one to the keyboard and one to
the eye, and a dialog with a loud button that Enter does not press is worse than
one with neither. That agreement is checked, not remembered.

**Nothing destructive is ever `primary`.** Delete, Remove and Clear are the
things an artist arrives at by accident, and making one the loudest object on
the screen is how it gets pressed by reflex. Also checked, because it is exactly
the rule somebody breaks while making a delete dialog feel decisive.

**A tab strip has to be legible as a tab strip**, and weight alone does not do
it. Three words at slightly different brightnesses read as a row of labels — and
a tab strip nobody recognises has *hidden* the panels it was meant to offer,
which is the opposite of what tabbing is for.

**There are two kinds of tab and they are not interchangeable.** What separates
them is what they divide:

| Kind | Where | Treatment |
| --- | --- | --- |
| **Panel** | docker headers; the timeline's modes | Rounded top corners, outlined at rest, and the active one lit from the top and fading into the panel below. No accent. |
| **Section** | the workspace tabs | No ground at all, and an accent underline. |

A panel tab is a **sheet edge**: the active tab and its content are one surface,
and the others are sheets behind it. That is why its bottom corners are square
and it has no bottom border — it runs *into* the panel, which is the whole
difference between a tab and a button that happens to sit in a row.

**The strip's rule is the front edge of the sheets behind.** It runs along the
bottom of the strip, past every resting tab and on past the last one to the end
of the header, and it **breaks at the active tab**. That break is the same
statement as the missing bottom border, seen from the other side: the line is
where the panel stops and the sheets behind it start, so it cannot cross the one
tab that *is* the panel.

Each tab draws its own bottom edge rather than one line being drawn under the
strip, and that is load-bearing rather than an implementation detail. A single
rule would have to be **covered** by the active tab, and the active tab cannot
cover anything — its gradient ends transparent so it merges into whatever ground
it lands on. Anything opaque enough to hide a line would be a seam. The stretch
past the last tab is the only piece the tabs cannot draw, so the docker header
draws that one. A section
tab divides a mode rather than a stack, so there is nothing behind it to be
behind, and it takes the accent because switching one changes what the whole
window is for.

Getting this backwards is not a small miss: an underline on a docker header
makes a panel group claim to divide the application.

The active panel tab's gradient **ends transparent and ends early**. Transparent
because a header, a floating panel and a docked strip are not the same ground,
and a gradient that named its destination would be a visible seam on two of the
three. Early — around 0.6 — because the design fades *fast*: it is the lit top
edge that says "this one is in front", and a fade stretched to the full height
gives a lighter block, which is the segmented-control look panel tabs are
specifically not.

**Badges are named for the meaning, not the colour** — `info`, `warning`,
`error`, `success`. A badge that says "amber" has to be renamed when the design
changes its mind. Their grounds are tinted rather than filled: a solid amber
block beside a solid red one reads as two warnings shouting, when the text
already carries the message and the colour only says which kind it is. A badge
is a label with a state, never a button, so it carries no hover.

## Colour

**Every colour in the chrome names a role. None of them is a hex value.** The
tokens live in `src/Lightbox.App/Styles/Palette.axaml`; this section is the rule
they implement, the same way `Density.axaml` implements *Sizes* above.

**The palette has two halves, and only one of them is the views.** Tokenising a
view reaches the surfaces somebody aimed at a token. Every *stock* control —
toggle buttons, slider thumbs, checkboxes, radios, focus rings, list selection —
paints from the **theme's** palette instead, so the theme has to be repointed at
ours or the application wears two colour systems at once. It did for a week: the
opacity slider had our coral track and Fluent's `#0078D7` thumb, and no test
could see it because both halves were internally consistent.

Two properties on `FluentTheme.Palettes` carry most of the second half:

| Property | Governs | Ours |
| --- | --- | --- |
| `Accent` | every "this is on" state | `AccentViolet` |
| `RegionColor` | the window ground, so dialogs | `SurfaceElevated` |

**What those two do not reach lives in `Styles/Theme.axaml`**, and the split is
worth knowing before hunting for a colour. A `ColorPaletteResources` entry is a
*seed* — the theme derives a family from it, so `Accent` fixes fifty controls at
once. A key like `TextControlBackground` is a *leaf*: nothing is computed from
it, so nothing else moves when it does, and the only way to correct it is to
name it. That file is therefore a list, and a list is the right shape for it.

**A field is lighter than what it sits on.** Text boxes, numeric fields and
combos: Fluent's ground for all of them is `#66000000` — 40% *black* — so every
field was a hole in its panel, and hovering deepened the hole. The reference has
them lifted everywhere, 39–52 against a 19–22 ground. This is a direction rather
than a colour: a sunken field says "a gap in the panel", a raised one says "a
surface you can put something on", and this application asks an artist to type
into them all day.

It is a **tint**, for the reason `SelectionBrush` is: a field on a panel and a
field in a dialog sit on different grounds, and one flat value cannot lift both.
An opaque `SurfaceElevated` field would be right on a panel and *invisible* on a
dialog — the case somebody would only find by opening one.

**Anything that floats takes the elevated surface** — context menus, combo
drop-downs, flyouts, dialogs. They were all on Fluent's `#2b2b2b`, a flat
neutral with no blue in it, which is why a right-click looked like it belonged
to a different application.

**One colour means "on".** Violet is the accent because it was already the
selection colour in the layers list and the cel vocabulary — a selected row and
a switched-on toggle should not be two different colours. It also leaves coral
meaning *the primary action*, which is what the button ranks above depend on: if
every "on" state is as loud as the one button you want pressed, nothing has been
ranked. A local setter that gives one control its own "on" colour breaks this,
which is why the ToggleSwitch and CheckBox ones were deleted rather than kept
for being pretty.

Those two values are the one place a role **cannot** be named: a
`ColorPaletteResources` is built before the merged dictionaries it would look
into, so `{StaticResource}` there does not resolve. They are hex literals on
purpose, and `TheThemePaletteIsWrittenInHexOnPurpose` asserts they equal the
tokens they stand in for.

Four surfaces, back to front — `BackgroundPrimary`, `BackgroundSecondary`,
`SurfacePanel`, `SurfaceElevated`. The order is the meaning: anything raised
above its neighbour goes one step up, and that is the whole system. Two text
weights, `TextPrimary` and `TextSecondary`. Six accents, which are a vocabulary
rather than decoration — timeline tracks and layer groups are coloured from
them, so they have to stay distinguishable at the size of a keyframe dot.

Three rules, each learned from what went wrong before the tokens existed:

- **Name the role, never the colour.** `SurfacePanelBrush` survives the panel
  becoming blue; `DarkGreyBrush` does not. A codebase that has been through one
  re-skin is full of tokens whose names are lies.
- **A literal in a view is a defect**, not a shortcut. Before this there were
  120 of them across 60 distinct values, which is how two panels come to be
  *nearly* the same colour — a difference nobody can see deliberately and
  everybody can see accidentally.
- **Two things are not colours and stay literal**: the drag grip's near-invisible
  fill, which is a hit target, and the paper the artist draws on, which is white
  because paper is white. Both are commented at the site, because the next
  person's instinct will be to tokenise them.

The exception is anything that reaches **pixels in the document**. Paper, ink,
swatches and every colour an artist picks are part of the record, governed by
the invariants in `CLAUDE.md`, and have nothing to do with this section.

## Dockers

**A docker must never be too small to use.** With five dockers open, a fixed
grid of starred rows divides the sidebar until each is a sliver and none is
usable — the failure the sidebar had. The sidebar is therefore a **vertical
scroll of stacked dockers with explicit pixel heights**, not a proportional
split:

- Each docker owns a default height and a floor (`MinHeight`) below which it is
  genuinely unusable rather than merely tight.
- A splitter sits between every adjacent pair, and dragging one changes only
  the two it touches.
- The stack may be taller than the sidebar. That is the point: scrolling past a
  docker is fine, being unable to use one is not.
- Hidden dockers cost nothing — `Auto` height, no splitter, no floor.

**Panels may share a slot as tabs**, and that is the other half of the rule
above. Five stacked dockers means five heights and a scroll; five in two slots
means two. A group is not a thing that exists — it is the panels currently
sharing a slot, and a slot of one is an ordinary docker that renders as a plain
title, so nothing about an untabbed panel changes.

**Tab what is used alternately, never what is used together.** Colour, palette
and gradient answer one question and you want one at a time. The layers list and
the project tree are read *while* drawing, so tabbing either trades a scroll for
a click on every stroke — a worse bargain than the height it saves.

**Content scrolls inside its docker**, so a docker with forty swatches is as
tall as a docker with four until the artist grows it.

## Density that has to stay generous

Do not shrink these to save space:

- The **brush-size slider** and the other brush parameters — dragged blind,
  mid-stroke.
- The **colour wheel** — precision here is the whole point of the control.
- **Timeline cells** — they are drag targets for cel moves, and a 12 px cell is
  a misdrop waiting to happen.
- The **canvas**. Everything above is in service of it.

## When a rule does not decide it

Write the question down in `.claude/quality/QUESTIONS.md` rather than guessing,
and say which way you leaned in the meantime.
