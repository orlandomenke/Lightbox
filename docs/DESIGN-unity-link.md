# Linking a Unity project — open a game's clips, redraw them, write them back

Status: **designed, not built.** Decisions: Q300. Pillar 5 (one-click export to
game engines) gains its first *inbound* half.

## The job

An artist has a Unity game whose characters run on placeholder animation —
generated sheets, or early drawings — and wants to replace it with their own,
clip by clip, without leaving the game in a broken state at any point. Today
that means: find the sheet, cut it into frames by hand, import them, draw,
export a sheet, copy it over the old one, and hope the slicing still matches.
Every step is a place for a frame to land in the wrong cell.

The link makes it four actions: **pick the clips, draw, check, write back.**

## What is actually on disk (surveyed 2026-11-27 in a real game project)

Two shapes, and the design has to treat the second as the normal case:

1. **Generated sheets.** A PNG on a regular grid, a `.json` sidecar (`clip`,
   `fps`, `frames`, `cellPx`, `columns`, `rows`, `loop`, `pivotY`,
   `pixelsPerUnit`) and a sprite-swap `.anim`. One sheet, one clip, equal cells.
   A project-side `AssetPostprocessor` re-slices from the sidecar with
   **stable sprite ids**, so a same-named PNG written over the old one keeps
   every clip reference intact.
2. **Hand-sliced shared atlases.** A dozen of one character's clips point into **one**
   atlas of a few dozen sprites whose cells are hand-cut and all different sizes
   (≈600–800 px, not a grid). A clip uses a handful of those sprites; other
   clips use the rest. One clip mixes two sheets (the character and a swoosh
   effect, on two renderer paths).

And a third the owner named that the project does not have yet:

3. **Single-sprite PNGs** — one file per frame, a clip pointing at many guids.

So "a clip" is: an ordered list of *(time, sprite reference)* keys per renderer
path, and a sprite reference is *(texture guid, sprite fileID)* resolved through
that texture's `.meta` to a **rect and a pivot**. The `.json` sidecar is a
convenience for shape 1, never the source of truth.

## Reading (Lightbox.Import, no Unity required)

- **`UnityProjectScanner`** walks `Assets/`, maps every `.meta` guid to its
  file, and lists every `.anim` with a `m_PPtrCurves` entry on `m_Sprite`.
  Grouping into characters is by folder, overridable per project (a character is
  "this folder and below"); it is a browsing aid and decides nothing.
- **`UnityAnimReader`** reads the sprite-swap curves: keys, `m_SampleRate`,
  loop flag, one track per renderer `path`. A clip with curves it does not
  understand (transform, float, events) is still opened; what was skipped is
  reported, never silently dropped — it is written back untouched because we
  never write the `.anim` at all.
- **`UnityTextureMeta`** reads `spriteMode`, the `spriteSheet.sprites` list
  (name, rect, alignment, pivot, `internalID`), `spritePixelsToUnits`.
  Single mode (shape 3) is one sprite covering the texture.
- A minimal YAML reader scoped to these two files — not a general Unity YAML
  parser. Unknown keys are ignored; a missing one we need is a refusal naming
  the file and key.

Timing becomes exposures: a key held for *n* frames at the document rate is a
cel exposed for *n*. The document rate is the clip's sample rate; a clip whose
key times are not on that grid (rare, but `.anim` allows it) is rounded to the
nearest frame and the import reports each one moved.

### Source of truth, and what a hostile file does (L1, S1, S8)

- **L1.** The format is learned from Unity's published manual (the YAML scene
  format, class ID reference, `TextureImporter`/`AnimationClip` serialization) and
  from text files the owner's own project produced. No Unity binary, DLL or
  package source is read or copied; the reader is written from those observations.
  Unity is named only to describe compatibility (L5).
- **S1.** Every file is read with a size cap (an `.anim` or `.meta` over 16 MB is
  refused by name), the YAML subset is line-based, follows no anchors, aliases or
  tags, caps block and flow nesting depth on every descent, and caps counts (keys per clip, sprites per
  texture, files per scan). Numbers that are not finite or rects with negative or
  absurd sizes are refusals. A malformed file is a `FormatException` naming the
  file and key; the scanner collects those per file and keeps going, so one broken
  clip never hides the rest. The scanner refuses a project whose `Assets` is itself a link, skips every
  directory and file link inside it, and reads each file through a bounded read
  that stops at the cap whatever the file's reported length says. Paths are
  checked as strings to stay under the root (`Inside`); links are not resolved,
  they are simply never followed, which is what keeps the scan inside the folder.
  Key times and rates are bounded but can still describe about a billion frames,
  so step 2 caps a clip's frame count before it allocates a frame per exposure.
- **S8.** Test fixtures are hand-written YAML made for this project, not copies
  of the game's files. The game's own art is never committed.

## Opening a clip as a document

- **Canvas**: the union of every frame's rect after aligning them **by pivot**,
  plus a margin. Cells of different sizes therefore line up the way they play in
  game — feet on the same line — which is what makes onion skin and tracing
  meaningful. The per-frame placement (cell rect → canvas offset) is stored on
  the document's link record so write-back can invert it exactly.
- **Layers**:
  - `Placeholder` — the sprite pixels as a raster baseline (pixels, not
    strokes: invariant 1 is unaffected, it is the same route PSD import uses),
    **locked, dimmed, and pinned out of export.** A layer per renderer path if
    the clip has more than one (`Placeholder · Swoosh`).
  - `Drawing` — empty, above it, where the artist works. More layers are added
    as usual; every unpinned layer is part of the write-back.
- **Several clips at once**: each selected clip opens as its own document in
  the project, named after the clip. One clip per document keeps write-back
  one-to-one and lets clips that share an atlas be finished one at a time.

**Built 2026-11-27 (step 2): `UnityClipImport.Open`.** What the build settled
that the plan above did not:

- **Offsets are whole pixels, and a half-pixel pivot rounds down.** A fractional
  offset would resample the art on the way in and again on the way out. A
  centred pivot on an odd-width cell sits on a pixel's middle (10.5 of 21);
  rounding it up put that cell's feet a pixel left of every even-width cell's —
  found by the alignment test, not by reading. Rounding halves down
  (`PivotPixel`) keeps them on one pixel. The record holds the offset actually
  used, so write-back inverts what was done rather than recomputing it.
- **An atlas is read a row at a time, and only as far down as its lowest
  needed cell**, keeping just the cells' spans — by Lightbox's own reader
  (`PngRows`, Q301), because the SkiaSharp build it ships cannot decode a PNG by
  rows (`StartScanlineDecode` is unimplemented; the first cut assumed it worked,
  and every atlas was silently decoded whole until review measured it). One real
  character atlas is 12 288 × 5 120: a quarter of a gigabyte whole, one 48 KB row
  this way. The reader takes 8-bit RGB/RGBA non-interlaced PNGs, checks every
  chunk's CRC (including the one reading stopped in), and inflates no further
  than the rows asked for. Any other PNG is decoded whole by Skia under a
  64-megapixel budget; its frames open but carry **no pixel hash**, so step 3
  will not rewrite them — Skia keeps only the high byte of a 16-bit PNG, and a
  hash of that would miss an edit to the low one.
- **The drawing layer is timed like the first renderer**: a new empty drawing
  wherever the placeholder shows a new sprite, holds between. Write-back then
  has an unambiguous drawing for every exposure.
- **A blank key is an empty drawing, not a hold**; a clip that starts late gets
  an empty first frame. A sprite shown twice is encoded once and shared.
- **Refusals, each before the cost it prevents.** The canvas and the cells'
  total size are computed from the slicing alone, in 64-bit arithmetic with
  every pivot bounded, before a pixel is decoded (review found a pivot of 1e9
  wrapping a 32-bit canvas bound into a plausible wrong document, and an 8 000 px
  cell decoded in full before the canvas refusal). Refused: more than 5 000
  frames, a pivot more than a canvas outside its cell, a canvas over 8 192 a
  side, cells totalling more than 64 megapixels, a texture whose size no longer
  matches its slicing, a texture that became a link, a clip none of whose
  sprites can be found. A clip with *some* missing sprites opens, those frames
  blank, and says which. Every failure is a `FormatException` with the reason,
  a texture deleted or locked since the scan included.
- **Resizing or cropping the canvas leaves the record alone, on purpose**
  (`ImageResizeTests` classifies `Doc.GameSource` and says why): a game's cell
  is a fixed size, so a scaled frame cannot go back into it, and write-back
  refuses a canvas that no longer matches its recorded size rather than landing
  every frame shifted. Growing the canvas around a character that needs more
  room is a fair wish; it would shift the offsets with the anchor, and is left
  until someone asks.
- **A renderer whose keys stop early stops showing there** (an empty drawing at
  its end) rather than holding past what its record says.
- **The placeholder is not byte-exact at soft edges** — it is stored
  premultiplied, like every layer — and nothing reads it back: write-back works
  from the file and the hash.
- **More than one renderer** gets a placeholder layer each, all at the same
  pivot, with a note: where a child renderer sits is in the prefab, which this
  does not read.
- **Old files (W2, W8):** `Doc.GameSource` is null and absent on every document
  not opened from a clip, so an older file loads and saves byte-for-byte as
  before; a committed fixture (`game-source-v1.lightbox.json`) guards the
  record's shape and that keys from a newer build survive (Q230). Nothing is
  migrated (W5 does not apply). The import only reads the game project (W6).
- **Measured on a real game project (Release):** all 64 sprite clips open in
  0.1–6 s each, almost all of it encoding the placeholder frames (about 18 ms a
  frame; a 119-frame clip is the slowest). Reading the largest atlas, all
  12 288 × 5 120 of it, takes 0.54 s against Skia's 0.32 s whole decode. Contact sheets of
  three characters were checked by eye: feet stay on the pivot line.

## The link record

On the **project** manifest, absent until set (optional-settings rule):

```json
"gameLink": { "engine": "unity", "root": "C:/…/MyGame" }
```

On each **document** opened from it, also absent otherwise:

```json
"gameSource": {
  "clip": "Assets/Animation/Hero/Run.anim",
  "tracks": [{ "path": "", "frames": [
    { "texture": "<guid>", "sprite": <internalID>, "name": "Hero_run1",
      "rect": [x, y, w, h], "pivot": [px, py], "canvasOffset": [ox, oy],
      "sourceHash": "<sha256 of the cell's pixels at import>" } ] }]
}
```

`sourceHash` is how write-back knows the game file changed underneath us (the
artist edited it in Unity, or another document wrote the same atlas) and stops
rather than overwriting someone else's work.

The root is stored as given; the document stores project-relative paths only,
so the pair survives the game project moving.

## Writing back — the part that must not go wrong

The owner's condition (Q300): on a shared sheet, each frame must overwrite
**exactly its own sprite and nothing else**, with visual checks.

1. **Render** each frame's unpinned layers at 1:1, cut out the cell rect at the
   stored canvas offset. Ink outside the rect is **not** silently clipped: the
   frame is flagged (*"Hero_run2 draws 14 px past its cell on the left"*) and the
   artist chooses to clip, or to grow the cell (only possible for a single-sprite
   PNG or a grid sheet with a sidecar; on a hand-sliced atlas growing would
   overlap a neighbour, so it is refused with the neighbour's name).
2. **Check freshness**: re-hash each target cell in the file on disk against
   `sourceHash`. Any mismatch stops the whole write and names the cells.
3. **Compose** the new texture in memory: start from the file's current bytes,
   clear each target rect to transparent, paint the new cell. A sprite used by
   **another clip too** is listed in the dialog (*"also used by Dash"*) — the
   write is still correct, but the artist should know they are changing two clips.
4. **Prove it**: diff the composed texture against the original. Every pixel
   outside the union of target rects must be byte-identical; anything else
   aborts. This is the automatic half of the visual check, and it is the one
   that catches a wrong rect, a flipped Y (Unity's rects are bottom-left —
   `UnityConvert` already owns that conversion and its tests), or an off-by-one
   on a neighbour.
5. **Show it**: the write-back dialog shows, per frame, *placeholder | new |
   game cell after write* side by side, plus the whole atlas with the touched
   rects outlined. This is the human half. Nothing is written until confirmed.
6. **Write** atomically (temp file + replace), keeping the PNG's size, bit
   depth and colour type. The `.meta` and `.anim` are **never touched**: sprite
   names, ids, rects and pivots stay, which is why the clip, controller and
   prefabs need nothing. Unity reimports the texture on focus.
7. **Re-hash** and update `sourceHash`, so the next write-back is checked against
   what we wrote.

Single-sprite PNGs (shape 3) are the same path with a rect equal to the image.

## What is deliberately not in this

- **Writing `.anim` files.** Changing timing, adding or removing frames, is a
  later step with its own question: it needs new sprites in the `.meta`, which
  means minting ids Unity will accept. Until then a frame added in Lightbox is
  reported as *not written — the clip has no sprite for it*.
- **A live connection to the Unity Editor.** File-level only. Unity notices the
  changed PNG itself.
- **Other engines.** The record has `engine` so Godot can follow; nothing else
  is designed here.

## Surfaces (the registration checklist)

- **Project window**: *Link game project…* (folder picker, Unity detected by
  `Assets/` + `ProjectSettings/ProjectVersion.txt`).
- **Game clips docker**: characters → clips with checkboxes, a thumbnail strip,
  frame count, fps, shared-atlas marker; *Open selected*.
- **File menu / `ShortcutMap`**: *Write back to game* (Ctrl+Shift+W proposed)
  — enabled only on a document with `gameSource`.
- **MCP**: `list_game_clips`, `open_game_clip`, `preview_write_back` (returns the
  diff report and the comparison image); write-back itself stays a human
  confirmation.
- **Manual**: a new section, *Working on a game's animation*.
- **Roadmap**: Pillar 5 item *Round-trip a game engine's clips* with evidence
  anchors `UnityAnimReader`, `UnityTextureMeta`, `GameLinkWriteBack`,
  `AWriteBackLeavesEveryPixelOutsideItsCellsUntouched`,
  `AChangedGameFileStopsTheWriteBack`, `CellsOfDifferentSizesLineUpByPivot`.

## Build order

1. Readers + scanner, tested against fixture copies of the three shapes
   (a grid sheet with sidecar, a hand-sliced atlas shared by two clips, a
   single-sprite clip). No UI.
2. Open-as-document: canvas by pivot, placeholder layer, exposures.
3. Write-back core with the diff proof and freshness check — tests first,
   including one that deliberately writes to a wrong rect and must abort.
4. Write-back dialog with the comparison strip.
5. Project link + clips docker, MCP, manual, roadmap.

Steps 1–3 are where the risk is and are useful on their own via MCP; 4–5 are
surface.
