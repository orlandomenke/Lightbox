# Q220 · Image sequence export: where it lives, how unique frames are numbered, and what an SVG save contains — **answered 2026-10-08**

Raised by the owner, 2026-10-08: *"let's get into save as; I want to be able to
save as png, svg and jpg (jpeg) files. The same for exporting/render animation
where we can select range of frame, export unique frame only (or all) and more
settings."*

What was already true when it was asked: **Save as image…** wrote PNG, JPEG and
WebP, with an opt-in *every frame*; **Export PNGs…** had no dialog at all — a
folder picker, then every frame as PNG. SVG did not exist, held back on purpose
by the roadmap ("a raster document cannot become an SVG except as an embedded
bitmap, which is a lie in a vector wrapper").

Four things were the owner's to decide, asked with `AskUserQuestion`.

### 1. What an SVG save contains when the marks are not honest vector — **paths, plus reported raster**

Recommended and taken. Pen paths, fills and set type become real SVG paths;
textured and painted marks go into the same file as an embedded bitmap, and the
dialog and the status line say how many layers went in as pixels. Rejected:
paths only (a painted document saves nearly empty), a centreline for every
stroke (editable, and does not look like the canvas), and a bitmap in a wrapper
(the thing the roadmap item said not to do).

**Built on its own branch** (`feat/export/svg-save`), as `SvgExporter`. Two
things the answer left to the build, decided there and recorded in the roadmap
item: the choice is made **per layer, never per stroke**, because marks on a
layer act on one another; and a layer's paths are **drawn back and compared
with the layer** before they are trusted, so a mark that only looks like an
outline in its settings still goes in as pixels.

### 2. How unique frames are numbered — **an unbroken run by default, timeline numbers as an option**

The recommendation was timeline numbers with gaps; **the owner chose the other
way round**: *"2 by default but 1 optionally by bool?"* So a run of unique
frames is `0001, 0002, 0003` and a timing file is the record of the holds, and
a tick — *Number files by their frame on the timeline* — keeps each file's
frame number and leaves the gaps instead. The tick applies to any export, not
only a unique one: an excerpt under timeline numbers overwrites the same files
a full export wrote.

### 3. Where the sequence export lives — **one dialog, and a slimmer save-as**

Recommended and taken. *Export PNGs…* becomes **Export image sequence…** with a
real dialog, and *Save as image…* goes back to writing one picture: its *Every
frame* tick is removed, so there is one place that writes numbered files.

### 4. Which further settings are in the first version — **all four offered**

Naming and start number, a range from a tag, a frame step, and transparent
paper / matte.

### Decided without asking, because they are not preferences

- **"Unique" is read off the rendered picture, not off the record.** A layer's
  holds are the wrong answer three ways: two layers hold different lengths, a
  camera move changes a held drawing, and a duplicated cel is two drawings that
  are one picture.
- **"Unique" collapses a hold and nothing else.** A picture that comes back
  later (A B A) is written again, so the files stay in playing order and a
  reader that ignores the timing file still gets the animation.
- **The timing file is absent unless unique frames are asked for.**
- **Leaving the paper out is an argument to the render, never a write to the
  document.**

### Left open, and said so

- The settings are not remembered between exports and are not in an
  `ExportPreset`. The `PngSequence` target in *Export for a game engine…* still
  writes every frame as PNG.
- A second export into the same folder does not remove files the first one
  wrote, so a unique export over a full one leaves the full one's tail behind.
- Nothing over MCP reaches this yet.
