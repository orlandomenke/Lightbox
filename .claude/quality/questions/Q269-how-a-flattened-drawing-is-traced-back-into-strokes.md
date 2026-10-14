# Q269 · How a flattened drawing is traced back into strokes — **answered 2026-10-14**

**Answered: each line becomes a centre-line brush stroke with pressure from its width, traced onto a new layer above with the pixel layer hidden, line art only for now.** Three questions asked together with `AskUserQuestion`, all as recommended; then a fourth thing the owner said while it was being built.

Raised by the owner on 2026-10-13 when choosing the grouped merge (Q265): drawings already flattened by a merge — two layers of the AI test file, `LegAndFeetLines-Back` and `ArmLines` — have no older copy of their strokes, so "build trace-to-strokes".

What it blocks: getting those drawings back to something the inbetweener can read.

## What was asked, and what was answered

**1. What a traced line becomes.** *Centre-line strokes* (recommended) — **chosen**: one brush stroke down the middle, pressure from width; editable, and matched stroke to stroke by the inbetweener. Against: *filled outlines* (near-exact picture, but a line becomes a shape the inbetweener matches poorly); *ask per trace* (both, more to build).

**2. What happens to the pixels.** *Traced onto a new layer, original hidden* (recommended) — **chosen**: nothing lost, the two can be compared. Against: *replace in place* (tidier; undo the only way back).

**3. Colour.** *Lines only, for now* (recommended) — **chosen**. Against: *lines and flat colour as fills*, roughly double the work, and colour layers were going to follow inbetweened lines anyway.

## What the owner added while it was built

> "The tracer could be a good feature to convert images to strokes. Especially if that could be paired with sliders that change contrast for better results."

Taken as direction, not as a question, because it adds to the first answer rather than choosing between alternatives. So the trace asks how before it runs, with the drawing at the playhead traced live in front of the artist: **where ink is read from** (transparency for a drawn layer, darkness for a scan or a photo — which is what makes "images to strokes" work), **how faint counts as ink**, and **the shortest line kept**. One threshold slider rather than contrast *and* threshold: for a tracer both only decide which pixels are ink, so two sliders would be one control twice.

## What it costs

- **Lossy, by nature, and said so in the window and the manual**: the exact edge, the drawing order and the pen's real pressure are not in pixels. Measured on the owner's two flattened layers: 82–88% of the ink in the same place, about twenty strokes per drawing, about 0.4 s per 1920×1080 drawing.
- Faint thin lines beside dark ones break up at the default threshold — the slider is there for that.
- An extra layer per traced layer until the artist deletes the hidden one.

## What review found, all fixed with a test each

The adversary, 2026-10-14: ink touching the page's edge was not thinned (a line running off the page came back as a sliver on the border); a busy picture cost far more than its size (a 1080×1080 grid 15.6 s and 7.8 GB — the joiner restarted after every join — and dense noise did not finish); a picture that is mostly ink was traced at full cost for nothing; a whole-layer trace ran inside the edit on the UI thread; the preview queued one trace per slider tick and could read a bitmap the caller had already freed (a native crash); a drawing whose pixels would not decode threw inside the edit; and the traced layer copied identity fields a duplicate clears (a second background layer, a second answer to a simulation, a mask drawing sharing its id) and dropped a cel's unknown keys. Now: a padded page, thinning over the ink only, the junctions paired once and the paths strung together in one walk (four times the grid costs about five times as much), a refusal with a reason for mostly-ink pictures, the layer traced on a worker and put in as one step, a preview that waits a moment and stops the one before, its own copy of the drawing, unreadable drawings left as pixels and counted, and the layer made the way a duplicate is.
