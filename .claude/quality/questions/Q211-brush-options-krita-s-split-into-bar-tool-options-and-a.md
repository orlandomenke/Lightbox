# Q211 · Brush options: Krita's split into bar, tool options and a brush editor — **answered 2026-10-07**

**Answered:** *"yes do as proposed"* — the brush tool's options split three
ways, the way Krita splits them, with the proposal's three calls taken as
recommended:

- **The bar** holds what changes every few strokes: the preset (marked when it
  has drifted from what was saved), a button that opens the brush editor,
  blend, eraser, alpha lock, size, opacity, flow.
- **Tool options** holds how the tool behaves, never how the brush looks:
  smoothing and its value, per-brush smoothing, the ring following pressure,
  anti-aliasing.
- **The brush editor** is a **popup** from the bar, not a docker: the preset's
  name and its save verbs (reload, overwrite, save new) on top; an option list
  where a check says whether the option takes part; the chosen option's panel;
  a scratchpad.
- **Hardness leaves the bar** for the editor's Tip option, where Krita keeps it.
- **Each option carries its own pressure, tilt and speed curves**, beside the
  value they drive; the separate Pen pressure page goes.

With it, the owner's instruction for the build: *"Do be careful around spacing
as in the gallery certain fields and toggles are cut off."* The checkbox half of
that was a real defect in the app's styles, fixed before the build
(`CheckBoxMinHeight`).

Raised by the owner, 2026-10-07: *"In the gallery let's take a look at the tool
options specifically for brushes. As that one is monstrous. Krita also offers a
tool option bar. Let's see how they do brushes."* Shown in the gallery as four
stories (`tools/Lightbox.Gallery/Stories/BrushOptions.cs`).

## What was there, measured

One Tool options docker, five pages, 49 settings (41 stored per stroke, 8
global): General 6, Effects 23, Medium 20, Pen pressure 16 (seven curve rows),
Presets 7. The quick bar repeated Opacity, Hardness, the smudge radius and the
colour rate. The pressure curves lived on a page apart from what they drive.

## What it costs

- **The single page of curves goes.** Comparing Size's curve with Flow's means
  selecting one option and then the other. Krita accepts the same cost.
- **Hardness is a click further away**: in the editor's Tip option, not on the
  bar. An artist who reaches for it every few strokes pays that click.
- **A popup closes when you paint.** The editor cannot stay open beside the
  canvas the way a docker could; the scratchpad is where a setting is tried.

## How the checks are built (an implementation note, not part of the answer)

The record has no per-option "enabled" field, and adding one would put a key in
every saved brush (the optional-settings rule). So an option's check reads the
record: it is on when the option changes the mark at all. Unchecking sets the
option to the values that leave the mark alone and keeps the old values for the
session, so checking it again puts them back; Reload brings back the saved ones.
Nothing is lost, and a brush that does not use an option still writes no key for
it.

## The alternatives, and why not

- **Keep the docker and tidy its pages.** The monolith stays a monolith: five
  pages, one of which holds the curves for the other four.
- **The editor as a docker.** Stays open while painting, but takes a column of
  screen for something an artist sets up rather than rides.
