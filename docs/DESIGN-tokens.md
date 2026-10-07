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

## What a hostile file could do

Nothing new. Tokens are compiled resources in the app's own assembly, and no
document or settings value reaches them. The interface scale already normalises
its one user-supplied number.
