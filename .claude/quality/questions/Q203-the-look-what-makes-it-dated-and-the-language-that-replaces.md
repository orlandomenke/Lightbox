# Q203 · The look: what makes it dated, and the language that replaces it — **answered 2026-10-07**

**Answered:** the chrome moves to a language the owner described as *"a
combination of Swiss 2.0, flat and skeuomorphism/neomorphism — the latter
mostly for light and rim effects"*, with *"subtlety but above all readability
and focus"* and *"nothing should distract from the task at hand"*. Decided:

- **Inter only, with tabular figures** on every number that changes.
- **Corners are tried, not chosen yet**: square (0), tight (4 · 6) and soft
  (8 · 14) all go into the gallery.
- **The default theme is chosen in the gallery**: *Dark, lit* and *Studio grey*
  are both built to the same standard. Whichever loses stays reachable as a
  preference.
- **Rows show state always; actions appear on hover or selection.** Reorder,
  add and delete leave the row and stay on the keyboard, the context menu and
  the panel footer.
- **Light effects are on by default and switchable.** Rim light and insets are
  always on.
- **The sandbox is a native Avalonia gallery**, not Storybook.

Raised by the owner: *"Propose me a new UI look and feel. I like what we have
thus far. But I still think it can look better. The colors, the rounded corners
and the fonts I am doubting."* and, after the first proposal: *"In general it
feels outdated. But I cannot point my fingers to what causes that… It is at
least not color."* Four reference images were supplied: a light-grey soft UI
with a dark tool rail, a warm translucent filter panel, a neumorphic button
state sheet and a dark node editor.

The working proposal was a live mock outside the repository; the gallery
(`tools/Lightbox.Gallery`) is where it continues, on the real controls.

## What makes it dated, read off three screenshots of the build

Not colour. Every element gets the same weight, so nothing recedes and the
canvas is one box among many:

1. **Every row carries every control.** A layer row has nine targets.
2. **Saturated fills on controls, not data**: pink slider tracks, violet tool,
   cel and canvas-bar tiles, an orange navigator frame.
3. **Panels are outlines, not surfaces**: the same near-black as the window
   behind them, separated by hairlines.
4. **One text size**, about 11 px, for titles, labels, values and status.
5. **Tools are hard to find**: 17 ungrouped ~11 px glyphs.
6. **Chrome that clips and repeats**: truncated tabs, float and close on every
   header. The main window mixes 89 vector icons with 52 text-glyph and emoji
   icons.

## The decisions, and what each costs

1. **Separation by light, not lines.** A raised surface is a 1 px top rim; a
   sunken one is a 1–2 px inner shade. Cost: nothing measurable.
2. **Effects on, switchable.** Island shadows beside the canvas repaint only
   with their own content. A shadow on a bar *over* the canvas repaints with
   every canvas frame, so it is drawn from a cached 9-slice image. Glass or
   backdrop blur is out: it needs window transparency, which conflicts with the
   direct swap-chain present (21 ms faster to glass, 2026-08-26).
3. **Hover/selection row actions.** Cost: an action is one pointer move further
   away. Pens report proximity, so hover works on a tablet.
4. **Theme and corners in the gallery.** Cost: two themes and three corner sets
   built before one is chosen. They are resource dictionaries, so the cost is
   values, not templates.

## What is not a question

- The layout stays: rail, canvas, right column, bottom sheet. This is a
  re-treatment.
- The six track and tag colours stay as a data vocabulary; the interface itself
  goes grey.
- The icon glyphs are replaced with paths whatever look wins.
