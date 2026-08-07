# Design system

The rules the UI is held to. Numbers live here, not in individual views — a
control that sets its own height is how a panel drifts.

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

One scale. Anything not on it needs a reason in a comment.

| Token | px | Used for |
| --- | --- | --- |
| `--row` | 24 | Field, combo, small button, layer row, list row |
| `--row-lg` | 30 | Primary buttons, tool buttons, anything hit while drawing |
| `--gap` | 4 | Between related controls in a group |
| `--gap-lg` | 8 | Between groups, and docker content padding |
| `--label` | 52 | Label column in a labelled-row layout, so rows align |
| `--field` | 64 | Numeric field beside a slider |

Font sizes: **12** in docker content, **11** in dense rows and option bars,
**10** for status and hints. Nothing smaller — below 10 the app stops being
readable on a laptop panel.

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
| Icon | 24×24, padding `4,2` | ✕ close, ▲▼ reorder, ＋ add |
| Text | height 24, padding `8,3`, `MinWidth 64` | "Import…", "＋ Swatch" |
| Tool | 30 min, padding `6,4` | Toolbar tools, transport buttons |

Two buttons that do comparable things must be the same size. A row of
`＋ Swatch` / `－` / `Import…` / `Export…` reads as a group only if the icon
buttons share one width and the text buttons share another. Mixed sizes inside
one bar is the most common way this app has looked unfinished.

## Colour

**Every colour in the chrome names a role. None of them is a hex value.** The
tokens live in `src/Lightbox.App/Styles/Palette.axaml`; this section is the rule
they implement, the same way `Density.axaml` implements *Sizes* above.

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
