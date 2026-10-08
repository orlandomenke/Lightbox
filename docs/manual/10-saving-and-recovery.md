# Saving and recovery

| Command | What it does |
| --- | --- |
| **Save** — Ctrl+S | Writes in place. With a project open, writes the project and only the documents that changed. A drawing that has never been saved has nowhere to go, so this opens **Save as…** instead of quietly doing nothing. |
| **Save as…** — Ctrl+Shift+S | Picks a new path. |
| **Save as image…** — Ctrl+Alt+Shift+S | Writes the drawing as an ordinary picture — PNG, JPEG, WebP or SVG. See [below](#saving-as-an-ordinary-picture). |
| **Export document…** | Writes a standalone `.lightbox.json` with every referenced swatch, gradient, brush tip and clip region **inlined**. |
| **Export image sequence…** | Opens the [sequence window](#exporting-an-image-sequence) — numbered PNG, JPEG or WebP frames into a folder you pick: a range, every Nth frame, or each held drawing once. |
| **Export video…** | Opens the [export window](#exporting-a-video) — format, size, frame range, rate, quality and sound, then the render itself. |
| **Export for a game engine…** | Sprite sheet, sidecar, and optionally the Unity importer. |

Both keys can be rebound like any other — they are in **Edit ▸ Configure ▸
Shortcuts** under *File*, and the menu shows whatever you set them to rather
than the factory key.

## Saving as an ordinary picture

**File ▸ Save as image…** writes what is on the canvas as a picture anyone can
open. This is the plain "give me a PNG of this" that the export commands above
do not cover: they write sequences, sheets and engine metadata, and this writes
one image — always one, whatever is on the timeline. For a run of frames use
[**Export image sequence…**](#exporting-an-image-sequence).

| Setting | What it decides |
| --- | --- |
| **Format** | **PNG** keeps transparency and loses nothing — the right answer for artwork, and the default. **JPEG** is smaller, lossy, and **has no transparency at all**. **WebP** is lossy *and* keeps transparency, which is why it is here. **SVG** is shapes rather than pixels where your marks allow it — see [below](#saving-as-svg). |
| **Quality** | 1–100, for JPEG and WebP. Absent on PNG rather than greyed out, because PNG has no such setting. |
| **Size** | A percentage of the document. A larger render draws the strokes onto a larger surface rather than enlarging pixels, so 200 % is genuinely sharper — the same promise the video export makes. |
| **Fill with** | Only for a format with no transparency. The colour that shows through where the drawing is see-through — white unless you change it, which is what you want unless you are matting a sprite onto something specific. |

Whatever the timeline is showing is what gets written, and with a camera in the
scene it is what the camera saw. A saved PNG is the same pixels as that frame
from **Export image sequence…** — the same compositing runs behind both.

**The transparency warning is worth reading.** Pick JPEG on a drawing with
see-through areas and the dialog says so before you save, because a character on
transparent paper saved as JPEG comes back on a solid white box. If you save
anyway the empty areas are filled with white rather than turning black, and the
status line afterwards says it happened.

**The extension you type wins over the format you picked.** Leave the format on
PNG and save as `cover.jpg` and you get a JPEG — typing the extension is the more
deliberate of the two choices. The dialog's warning cannot see that coming, so in
that case the status line after the save is where you are told the transparency
went.

**Why not TIFF, GIF, BMP or PSD?** The image library Lightbox uses has encoders
for exactly PNG, JPEG and WebP and no others, so the rest would be menu entries
that write nothing. (SVG is written by Lightbox itself, from the drawing.) Writing a PSD back out is a separate piece of work and is
not built — Lightbox can read a Photoshop file today and cannot hand one back.

### Saving as SVG

An SVG is shapes, and only some marks are shapes. So an SVG save does two
things, and tells you which it did:

- **A layer whose every mark is an outline is written as paths.** That is
  fills, set type, and lines drawn with a hard round brush — **Ink**, and any
  brush you build from it with full hardness and no texture, scatter or jitter.
  A line keeps its pressure: it is written as its outline, thick and thin where
  you pressed, not as a centreline with one width.
- **Any other layer is written as its pixels**, inside the same file. Pencil,
  airbrush, watercolour, a smudge, an eraser — these are not outlines, and a
  path pretending to be one would not look like your drawing.

A layer is one or the other, never a mixture: an eraser changes the fill
before it, so a layer with one mark that is not an outline goes in whole as
pixels. **Keep line art and flats on their own layers** and those layers stay
vector, whatever the paint layers beside them hold.

The dialog says which layers will be pixels as soon as you pick SVG, and the
status line says it again after the save. A file that is all paths says
nothing.

Lightbox does not go by the brush's settings alone. It draws the paths back and
compares them with the layer on your canvas, and if they are not the same
picture the layer is saved as pixels. What varies from one drawing to the next
is how much of the file is editable as shapes, not whether it looks like the
drawing. A layer that also holds imported pixels or a placed symbol is always
saved as pixels.

**A layered file holds more than the picture shows.** Unlike a PNG, the SVG
carries each visible layer whole — including the parts another layer covers —
and each layer's name. Hide a layer you do not want in the file; a hidden layer,
and a layer at zero opacity, is left out.

| | |
| --- | --- |
| **Layers** | Each is a group named after the layer, in stacking order, with its opacity. A blend mode is written the one way SVG has (`mix-blend-mode`), which browsers and Inkscape honour and some editors ignore. |
| **Size** | Changes how big the file opens, up to 1600 %. The shapes themselves are the same shapes at any size. |
| **Paper** | A paper layer is a path like any other fill; see-through paper writes no background. |
| **The whole picture as one image** | A camera, a mask or a clipped layer, a layer effect, an adjustment layer, a scene effect or production footage cannot be said one layer at a time. With any of those the SVG holds a single image of the finished frame, and the dialog says why. |

**Not built:** gradients as SVG gradients (a gradient fill sends its layer to
pixels), a run of SVG frames from *Export image sequence…*, and text as live
SVG text — set type is saved as its outlines, which is what it is in Lightbox.

## Exporting an image sequence

**File ▸ Export image sequence…** writes the animation as numbered pictures in a
folder. Choose the settings, press **Export…**, then pick the folder. Left
alone, it writes what *Export PNGs…* always wrote: every frame, PNG, the
document's own size, `frame_0001.png` onwards.

| Setting | What it decides |
| --- | --- |
| **Format**, **Quality**, **Size %** | As in [Save as image](#saving-as-an-ordinary-picture): PNG, JPEG or WebP; quality for the two lossy ones; a larger size re-draws the strokes rather than enlarging pixels. |
| **Tag** | Only on a document that has animation tags. Picking one fills in the range with that tag's frames. Type over the range afterwards and the picker lets go of the tag. |
| **Frames … to …** | The first and last frame to write, counted from 1 as the timeline shows them. |
| **Every** | 1 writes every frame of the range, 2 every second one, and so on. |
| **Unique frames only** | A drawing held for several frames is written **once**. See below. |
| **Name**, **Digits**, **Start at** | `walk`, 3 and 10 give `walk_010.png`, `walk_011.png`… Characters a file name cannot hold are dropped. |
| **Number files by their frame on the timeline** | Each file takes the number of the frame it sits on instead of its place in the run. *Start at* disappears, because the timeline is doing the numbering. |
| **Leave the paper out** | PNG and WebP only. Frames are written with a see-through background. This export only — the document keeps its paper. |
| **Fill with** | JPEG only. The colour behind the drawing where it is see-through. |

The sentence at the bottom says how many files are about to be written and what
the first one will be called, before you pick a folder.

**Unique frames.** Animating on 2s, a 24-frame second holds 12 drawings, and
this writes those 12. A frame is left out when it would be exactly the same
picture as the file before it — judged on the finished frame, so a held drawing
under a moving camera, or over a layer that is still changing, is *not* a
repeat and is written. A picture that comes back later (A, B, A again) is
written again: the files stay in the order they play.

Beside the pictures it writes `frame_timing.json` (using your name in place of
`frame`), which is the only place the holds are recorded:

```json
{ "fps": 12, "from": 1, "to": 6, "step": 1,
  "frames": [ { "file": "frame_0001.png", "frame": 1, "hold": 3 },
              { "file": "frame_0002.png", "frame": 4, "hold": 2 },
              { "file": "frame_0003.png", "frame": 6, "hold": 1 } ] }
```

`frame` is where the picture sits on the timeline and `hold` is how many frames
it stands for. By default the files are numbered 1, 2, 3 without gaps; tick
*Number files by their frame on the timeline* and the same export is
`frame_0001`, `frame_0004`, `frame_0006` instead. The dialog says "up to" a
number of files in this mode, because how many pictures repeat is only known
once they are rendered. Exporting every frame writes no timing file.

With a camera in the scene the frames are what the camera saw. When the
document has a scratch track, it is written beside the frames as `audio.wav`.

**Two things it does not do yet.** The settings are not remembered from one
export to the next. And exporting into a folder that already holds a longer
sequence does not remove the older files — pick an empty folder when the new
run is shorter.

**SVG is not offered for a sequence** — *Planned*. [Save as image](#saving-as-svg) writes one frame as SVG.

## Opening a Photoshop file

**File ▸ Open…** accepts `.psd` and `.psb` alongside Lightbox's own documents.
Each Photoshop layer becomes a Lightbox layer, keeping its name, visibility,
opacity, blend mode and lock, and folders become layer folders. The drawing
arrives as one frame — a PSD is a single image — and the imported pixels sit
*underneath* anything you then paint, so the file is never written over: the
import has no path attached, and **Save** sends you to **Save as…**.

RGB and greyscale files are read, at 8 or 16 bits per channel. A 16-bit file is
brought down to 8, which is what Lightbox paints in, and the status line says so.

**Layer masks and clipping masks come across.** A Photoshop mask becomes a
Lightbox [layer mask](06-layers-selections-and-guides.md) — its coverage is the
mask's coverage, at the rectangle Photoshop gave it, with whatever it said applies
outside — and a mask switched off in Photoshop arrives switched off. A clipped
layer arrives clipped to the layer below, which is the same rule Photoshop uses.

**Folders come across as they were** — nested inside one another, under their
own names, empty ones included. A folder hidden in Photoshop arrives hidden (and
so hides everything inside it), and one collapsed in Photoshop's panel arrives
collapsed.

**Lightbox refuses a PSD it cannot draw faithfully, and tells you exactly what to
fix.** Adjustment layers, fill layers, text layers, smart objects, layer effects,
vector masks and a folder that blends as a group all change what the pixels
beneath them look like in ways Lightbox has no model for. Rather than opening a
picture that is not the one you saved, it lists every feature it found, the layer
carrying it, and the Photoshop menu path that flattens it. One trip back to
Photoshop should be enough.

This is still a real limitation: a file with a Curves layer or a drop shadow will
not open until those are flattened. The trade is deliberate — a drawing that
silently comes in wrong is worse than one that does not come in yet.

CMYK, Lab, indexed-colour and duotone files, and 32-bit HDR files, are refused
the same way, with the **Image ▸ Mode** conversion that fixes them.

## Exporting a video

**File ▸ Export video…** opens a window that holds the settings, the
destination and the render itself. It stays open while the frames encode: the
bar moves per frame, **Export** turns into **Stop**, and the sentence at the
bottom names the finished file and its size. A failure stays on screen until
you have read it.

| Setting | What it decides |
| --- | --- |
| **Format** | **MP4** (H.264) plays anywhere and is the one to send for review. **ProRes 422** in MOV is what an editor wants: much bigger files, no generation loss. Changing this renames the file so the stream and the container agree. |
| **Quality** | H.264: *High* is visually lossless, *Standard* is a review copy, *Small* is a quick look. ProRes: the profile, 422 HQ by default. |
| **Size** | 25 % to 200 % of the document — or of the camera's output size, when the scene has a camera. A larger render draws the strokes onto a larger surface rather than enlarging pixels, so 200 % is genuinely sharper. Sides are rounded to even numbers, which H.264 requires. |
| **Frames** | The whole timeline, or a range. The numbers are the frame numbers on the timeline, and both ends are included. |
| **Frame rate** | Defaults to the scene's. Changing it does not resample: the same frames play back faster or slower. |
| **Sound** | Muxes the scratch track, with its offset, trim and volume honoured. Off — and unavailable — when the document has no sound or the track is muted. Rendering a range starts the sound where the range starts. |

Production footage composites beneath the drawings, exactly as it does on the
canvas; a plain reference never reaches an exported pixel. With a camera, the
video is what the camera saw.

**If the window opens with a warning across the top, no video can be written**:
the encoder Lightbox drives, FFmpeg, was not found. The packaged application
ships a copy beside itself, so this normally means a development build or a
broken install — put an `ffmpeg` on your PATH and reopen the window.
**Export image sequence…** works regardless, and every compositing package will encode
the sequence.

## Nothing leaves the app until the drawing is on disk

**Exporting, and marking an asset Ready, are both statements about a file.** An
export says "this is what the drawing looks like"; a status says "this is
finished, go and use it" — and with auto-export on, that second one immediately
writes a sheet for a game engine to pick up. Neither means anything if the
drawing itself was never saved.

So both check first, and there are only two answers:

- **Save file as…** — pick a path, and what you were doing carries on.
- **Revert status change** (or **Don't export**) — nothing happens, and the
  status goes back to what it was.

There is deliberately no "do it anyway". A status change is **prohibited** until
the drawing has a file: the alternative is a designer told an asset is ready,
pointing at nothing.

If the drawing has a file and you have unsaved changes, it just saves them —
no dialog. You already said where the file goes, so asking again would be a
click in the way.

One case worth knowing because the wording differs: if the file you saved to has
since been **moved or deleted**, you are asked again rather than having it
written silently back to a folder you emptied on purpose.

## The unsaved dot

A tab shows **•** when it differs from the file on disk. Two things make that true:

- You have edited it since the last save.
- It has **never been saved** — a new document has no file at all, so it says so
  from the moment you make it.

**Undoing back to where you saved clears the dot.** Draw something and undo it,
add a reference and remove it again, and the tab goes quiet, because the question
is *does this differ from the file* rather than *did anything happen*.

Things that are **not** edits, and never raise it: choosing a brush or changing
its settings (brushes are saved separately), moving a document to the folder it is
already in, clicking into a name box and out again without typing, and switching
tabs, panning or zooming.

## Closing with work in flight

**Closing a tab** with unsaved changes offers **Save**, **Discard changes** and
**Cancel**. Save is what Enter does, because it is the only one that cannot lose
anything; Discard sits at the far end so a fast hand does not find it next to
the safe one. If Save has nowhere to write, it opens Save as… — and **cancelling
that picker cancels the close too**, because you asked to keep the work.
A brand-new document you have not drawn in closes without asking, for the same
reason it is left off the closing-Lightbox list below: it shows the dot because
it has no file, but there is nothing in it to lose.

**Closing the last tab** leaves nothing open and asks what to open next — the
same question as the start screen, at the only other moment it is the right
one. Escape on it leaves the application empty, with New and Open waiting in
the middle of the workspace.

**Closing Lightbox** asks the same question about everything at once. One box,
listing the documents by name rather than counting them, with **Save all** as
the default. A brand-new document you have not drawn in is not on the list —
it shows the dot because it has no file, but there is nothing in it to lose. Cancel and the application stays open; cancel a file picker
part-way through and the whole close is called off, with everything still there.

Nothing is ever discarded without being offered to you first.

## The file on disk

A document is a `.lightbox.json` file, and since 2026-08-13 it is written
**gzip-compressed** — several times smaller on disk, which matters most for
paintings, where the stroke record grows with every mark. Nothing about the
content changed: gunzip the file and the same readable JSON is inside. Every
document saved before the change is plain JSON and **opens exactly as it always
did** — Lightbox looks at the file's own bytes, not its age or its name, so
both kinds coexist and a resave simply produces the smaller form.

## Large paintings open from a stored rendering

A drawing is a list of strokes and the picture is worked out from them, which is
what keeps every mark editable and re-colourable however old it is. The bill is
that opening a drawing means painting every stroke again. On an animation cel
that is a few milliseconds and you will never notice. On a painting you have
been building for a week it is the slowest thing the application does — around
ten thousand painterly strokes takes about a hundred seconds.

So when a drawing passes **250 strokes**, saving also stores a picture of the
strokes it has so far, beside them in the same file. Reopening starts from that
picture and paints only what came after it. Measured on a thousand-stroke
painting the difference is **8.5 seconds against 30 milliseconds**.

Three things worth knowing, because they are what the feature promises rather
than what it does:

- **The strokes are still the artwork.** The stored picture is a shortcut and
  nothing more. Deleting it changes nothing except how long the drawing takes to
  appear, and every export is painted from the strokes regardless.
- **It costs file size** — roughly one full-canvas image per drawing big enough
  to qualify, which on a big painting is most of the file. Turn it off in
  **Edit ▸ Configure ▸ Performance** if you would rather have the megabytes;
  turning it off also takes the stored pictures out of what is already open.
- **It arrives just after the save, not during it.** Making the picture costs
  what opening the drawing costs, so it happens in the background and Ctrl+S
  never waits for it. That means if you save a large painting for the first time
  and quit within a few seconds, the picture may not have reached the file yet —
  the next save of any kind puts it there. Nothing is lost either way; the
  drawing simply opens the slow way once more.

You will not see it work. There is no badge and no progress bar, because the
only thing it changes is a wait that is no longer there.

## Autosave

Under **Edit**. Choose off, 30 seconds, 1, 5 or 15 minutes. Zero is a real
answer, not a mistake to guard against.

Autosave keeps a **recovery copy of every open document with unsaved work** —
each one on its own, in your local app data folder (`%LOCALAPPDATA%\Lightbox\recovery`), not over your
files. A copy is deleted when you save or close that document, and they all go
when you quit normally. If you would rather autosave also wrote over the real
file, there is a checkbox — off by default, because silently rewriting the file
you opened takes away the ability to close without saving.

### After a crash

If Lightbox closes without quitting — a crash, a power cut, the process being
ended — the copies stay, and the **next launch offers them back**: a list of the
documents, when each copy was kept and which file it came from, with three
answers.

- **Restore** opens each one as a new tab named *"name (recovered)"*. It is **not
  tied to the original file**: saving asks where, starting beside the original,
  so a recovery can never quietly overwrite a file that changed after the crash.
  Until you save it, closing it asks first.
- **Not now** leaves the copies exactly where they are. **File ▸ Recover unsaved
  work…** brings the list back whenever you want it.
- **Discard…** deletes them, after asking you to confirm — it is the only answer
  that cannot be undone.

Each running Lightbox keeps its copies separate, so a second window or the next
launch can never write over what a crashed one left. A crash costs at most one
autosave interval of work — which is also why *off* is a choice to make knowingly.

The write happens **in the background**: autosave takes its snapshot in a few
milliseconds and does the disk work off to the side, so it never pauses the
brush — however large the painting has grown.

---
