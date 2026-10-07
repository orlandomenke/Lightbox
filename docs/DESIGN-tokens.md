# Design tokens

Q203 settled the new look in `tools/Lightbox.Gallery`, and the owner put the
order plainly: *"the most important part of actually building it, is
tokenizing."* This document records how the app gets there without a step
nobody can review.

## The problem

Measured on 2026-10-07, the app's views and styles carried:

| What | Literals | Distinct values |
| --- | --- | --- |
| `FontSize` | 458 | 7 (11, 12 and 10 cover 98%) |
| `Spacing` | 366 | 12 (6, 4, 8 and 2 dominate) |
| `Margin` and `Padding` | 428 | 101 |
| `Width` and `Height` | 574 | many |
| `CornerRadius` | 59 | 6 |
| Colour references | 352 | 132 keys, all `StaticResource` |

868 of them are in `MainWindow.axaml`. A new look applied on top of that is one
more layer of numbers. Nothing would say which inset is "the panel's" and which
is a one-off, and the gallery showed that the look only holds together when
there is one answer.

## The plan: three steps, two of them invisible

1. **Foundation, no visual change.** `Styles/Tokens.axaml` names a type scale, a
   spacing scale, the density sizes, radii by role and the icon line, all at
   **today's values**. The style files read from it. Every `FontSize` and stack
   `Spacing` in the views that sits on the scale becomes a token; this is
   mechanical, because each value maps to exactly one token. Every brush
   reference becomes a `DynamicResource`, which is what lets a theme switch
   live later. `TokenRatchetTests` counts the literals left in each file
   against `.claude/quality/ratchets/literals.json`. A count may fall and may
   never rise, so a new literal fails the build and the count only goes down.
2. **Views by area, no visual change.** Margins, paddings, sizes and radii move
   to role tokens, one area per pull request: the dockers, Configure, the
   project window, then the dialogs. These need judgement rather than a script,
   because `4,0` is a label gap in one place and a button inset in another.
3. **The look.** The token values switch to Dark-lit with Tight corners. The
   gallery's treatments move into the app's styles, Studio grey becomes a theme
   dictionary, and Configure → Interface gets a live theme switch. Because steps
   1 and 2 did the work, this is a small diff that changes every pixel.

Steps 1 and 2 change nothing on screen, and that is what makes them reviewable.
A token that holds the wrong value shows as a visible difference, and there
should be none.

## Decisions inside step 1

- **Colours are `DynamicResource`; sizes are `StaticResource`.** A colour is what
  a theme changes while the app runs. A size changes through the interface scale
  (a layout transform), never by swapping a value, so it can stay resolved once.
- **Only brushes move, not raw colours.** A `Color` used inside a brush
  definition or a gradient stop is not on a control in the tree, so a dynamic
  reference there would not resolve. Those stay static until step 3 gives the
  palette theme dictionaries.
- **Off-scale values stay literal and are counted.** `FontSize="9"` and
  `Spacing="3"` have no token on purpose. Inventing a token for an outlier gives
  it legitimacy; leaving it counted means step 2 has to decide about it.
- **The scale is named by size; the roles come in step 2.** `Space6` is a
  number. `PanelPadding` is a decision, and decisions belong with the views that
  make them.
- **Code-behind is out of scope here.** `new Thickness(…)` in C# is not counted
  yet. The gallery's stories are the one place that does this deliberately.

## Progress

| Step | Literals left | What it moved |
| --- | --- | --- |
| 1 · foundation | 1,239 | type scale, spacing scale, density sizes, dynamic brushes |
| 2 · dockers | 912 | setting rows (label, slider gap, value floor), icon sizes, section gaps — the docker part of MainWindow.axaml and the panels it hosts (tool option pages, effects, guides, scene) |

**How each area proves "no visual change":** the gallery's `--snapshot-app`
renders the main window empty and with a document open, every docker brought to
the front one at a time, every tool's options page and every Configure page —
44 images. They are compared pixel by pixel against the previous step with the
status line masked: it carries live readings. The comparison must first catch a
deliberate one-pixel break in a token the area introduced; for the dockers,
`SizeDockerLabel` at 91 showed in 15 of the 44.

**What the pixels do not reach, and what covers it.** The snapshot shows each
docker and tool page in its default state on a fresh document. A selected bone,
an armature, a populated effect stack, a camera, a text selection, an expanded
section, a popup or a hover is never on screen. Those lines are covered by the
second half of the evidence: every changed line pairs one-for-one with its old
form, and differs only in an attribute value replaced by a token holding the same
number and the same type. A static resource resolves to that number whether or
not its element is visible. The adversary review checks the pairing by script,
302 pairs for the dockers, and found no mismatch.

## Left for step 3, from the docker review

These are decisions rather than mechanics, so they stay literal and counted
until the look sets them:

- **One docker label width.** Two widths are in use today: `SizeDockerLabel` (90)
  and `SizeDockerLabelWide` (100).
- **One fixed field width.** 56, 68 and 72 are still literal, and EffectsPanel's
  rows use 86-wide labels and 52-wide values (`EffectsPanel.axaml` ColumnDefinitions).
- **Fewer icon sizes.** 8, 9, 10 and 11 are all in use.
- **9 px text** (EffectsPanel shelf names and warnings) is below DESIGN.md's floor
  of 10. It is a question, not a token.
- **ScenePanel's depth row** (`*,58,90`) needs a role for its diagram and field
  columns.

## What a hostile file could do

Nothing new. Tokens are compiled resources in the app's own assembly, and no
document or settings value reaches them. The interface scale already normalises
its one user-supplied number.
