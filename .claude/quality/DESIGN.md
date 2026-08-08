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
| `--tool` | 26 | Tool palette buttons — hit while drawing; 26 with a 12px glyph, the owner's trade of hit size for breathing room (was 28/16) |
| `--gap` | 4 | Between related controls in a group |
| `--gap-lg` | 8 | Between groups, and docker content padding |
| `--label` | 52 | Label column in a labelled-row layout, so rows align |
| `--field` | 44 | Numeric field's floor — it grows with the value, never shrinks below "100%" |

**A strip is as tall as its tallest control plus 2 above and below.** The four
strips above the canvas — menu, tool options, AI, document tabs — each wrapped
their contents in 6 to 12 px of padding, which on a 30 px bar is a third again
of its height for nothing. Every pixel of those is taken from the canvas
permanently, on every document, whether or not the strip has anything to say.

The same rule the whole file opens with, applied to the least-examined chrome in
the application: a strip is a container, and a container's padding is not where
comfort comes from. Comfort is the size of the thing you are aiming at, which is
`--tile` and `--tool` and is unchanged.

Font sizes: **12** in docker content, **11** in dense rows and option bars,
**10** for status and hints. **No text smaller** — below 10 the app stops being
readable on a laptop panel.

*Text*, and the word is doing work. A **glyph used as an icon** is a shape
rather than something to read, and it is sized by the box it has to fit: the
stacked layer-reorder arrows are 8 px in an 11 px button, deliberately half
height each so the pair fits one row without setting the row's height. Raising
those to 10 would make the arrows crowd their box to satisfy a rule about
reading words. The floor applies to anything with a word in it.

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
| **Panel** | docker headers; the timeline's modes | Rounded top corners, outlined at rest; the active one lit from the top, fading over its whole height, with a violet-to-magenta gradient line along its TOP edge. |
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

The active panel tab's gradient **ends transparent and starts soft**.
Transparent because a header, a floating panel and a docked strip are not the
same ground, and a gradient that named its destination would be a visible seam
on two of the three. It runs the tab's whole height — it ended at 0.6 first,
read off a 26px tab in the compressed mockup, and the owner's correction is
that the subtlety IS the length of the fade. What keeps it from being a filled
block is the soft start and the transparent end, not an early stop. The top
line is the second marker, off the reference's Brush Settings tab: 2px,
violet into magenta, dying before the far edge.

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

**A field is a well.** Text boxes, numeric fields and combos all take
`BackgroundPrimary`, the darkest surface, so a field reads as cut into whatever
it sits on. That makes a field on a docker, a field on a dialog and a field in a
flyout **the same colour**, with only the surface behind them changing — which
is the solidity the design has and a per-surface tint cannot produce.

*Hover* still lifts, and that half is not negotiable: pointing at something
makes it lighter, never darker. Fluent's original did the opposite — `#66000000`
resting against `#99000000` hovered — which is a control dimming under the
pointer.

**Every boxed control is the same shape**, one corner radius, no exceptions. The
combo was rounded and the numeric field square, side by side in the same docker
row; nothing was wrong with either on its own, which is why it survived every
review that looked at one control at a time. `FieldShapeTests` asserts they
agree *and* that they are actually rounded, because "they all match" is also
satisfied by all of them being wrong the same way.

**And the square corners were never painted square — they were clipped.** Every
radius property read correct while the screen showed square, because Fluent's
`ButtonSpinner` carries a 32px minimum inside our 22px numeric fields: the whole
inner stack overflowed by five pixels each way and the corners and top border
were cut off. Two lessons, both now enforced: a property probe cannot see a
clip, only geometry can (`TheInnerTextBoxFitsInsideTheFieldThatHostsIt`), and a
"fix" verified by reading properties back is not verified.

**Buttons are the bar's own dark surface with a rim light on the top edge.**
Fluent's default was 20% white — a pale box, and a toolbar of them a wall. The
rim is `RimLightBrush` used as a *BorderBrush*: a vertical white-to-nothing
gradient that a 1px border samples by position, so the top edge catches light,
the sides taper, the bottom gets none. One brush, no second element.

**The menu is the toolbar's surface**, separated by a 1px line of
`BackgroundPrimary`. Two strips of one colour with a scored line read as one
piece of chrome; two strips of different colours read as a stack.

**The violet has two depths.** `AccentViolet` (#7B61FF) is for *marks* —
keyframe dots, gradients, the tab line, focus. `AccentVioletDeep` (#5B48C8,
violet composited over the deepest ground at 70%) is for anything that uses
violet as a *fill the size of a button* — the theme accent, toggled states,
selection — because the bright violet glows at that size. The owner's read of
the alternate reference: the darkness is where the solidity comes from.

**The application's base font size is 12.** Fluent's default is 14, and
anything left unstyled — layer names, dialog labels, menu items — towered over
the 11s and 12s beside it, which is most of why the UI read larger than Krita
on the same monitor. Set once with `:is(Window)`, because a bare type selector
does not match subclasses and every window here is one.

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

**A sidebar never scrolls** — the owner's call, and a **reversal** of the
first answer, which was pixel heights plus a strip that scrolls (kept below
for the record). The sidebar is a **proportional split that always fits**:

- Dockers share the height as weighted stars — the saved extent is the
  weight — so another panel arriving shrinks everyone proportionally and a
  splitter drag's proportions survive the next rebuild.
- The floor per docker is its **chrome**: title strip and option bars stay
  visible however hard the side is squeezed. The squeeze lands on the
  content, which scrolls *inside* the docker (the bars never leave).
- Scalable content — the colour wheel — scales with its docker.
- Hidden dockers cost nothing — no row, no splitter, no floor.

The first answer, reversed above and kept because its failure mode is real:
five dockers in starred rows once divided the sidebar until each was a
sliver, which is why the strip used pixel heights and scrolled. What changed
is the floor — chrome rather than content, with the content scrolling inside
its docker — so "too small to use" now degrades to "scroll inside the
panel", not to five useless slivers. Tabbing (below) remains the real
answer to a crowded side.

**Panels may share a slot as tabs**, and that is the other half of the rule
above. Five stacked dockers means five slivers; five in two slots means two
comfortable panels. A group is not a thing that exists — it is the panels
currently sharing a slot. **A slot of one wears its title as a tab too** (the
owner's reversal of "renders as a plain title"): one header treatment
everywhere, and dropping a panel onto any docker reads as joining tabs that
are already there. The one wrinkle that buys is in `Docker.LandedOnAControl`
— a lone tab is a grip, not a control, or the panel cannot be dragged at all.

**Dropping onto a docker's body joins its tabs** — full width, full height,
previewed over the whole panel. The half-panel insert targets are gone (the
owner: a dead lower half, and a top target that should have been the entire
docker). Inserting into the stack lives in a slim sliver at each panel
boundary, kept only so a stack can be reordered and a group split.

**Tab what is used alternately, never what is used together.** Colour, palette
and gradient answer one question and you want one at a time. The layers list and
the project tree are read *while* drawing, so tabbing either trades a scroll for
a click on every stroke — a worse bargain than the height it saves.

**Content scrolls inside its docker**, so a docker with forty swatches is as
tall as a docker with four until the artist grows it.

**Track colours on the timeline.** Every track wears its own hue, cycling
violet, blue, magenta, teal, green, gold — a row is findable by colour before
it is read by name — and the camera always takes the orange, exactly as the
reference draws it. The vocabulary lives in `TrackView.ColourOf`, the graph
editor's series reuse the same hues for the same things, and a seventh track
cycles rather than inventing a colour.

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
