# Saving and recovery

| Command | What it does |
| --- | --- |
| **Save** — Ctrl+S | Writes in place. With a project open, writes the project and only the documents that changed. A drawing that has never been saved has nowhere to go, so this opens **Save as…** instead of quietly doing nothing. |
| **Save as…** — Ctrl+Shift+S | Picks a new path. |
| **Save as image…** — Ctrl+Alt+Shift+S | Writes the drawing as an ordinary picture — PNG, JPEG or WebP. See [below](#saving-as-an-ordinary-picture). |
| **Export document…** | Writes a standalone `.lightbox.json` with every referenced swatch, gradient, brush tip and clip region **inlined**. |
| **Export PNGs…** | Every frame as a numbered PNG, into a folder you pick — with the scratch track beside them as `audio.wav` when there is one. |
| **Export video…** | Opens the [export window](#exporting-a-video) — format, size, frame range, rate, quality and sound, then the render itself. |
| **Export for a game engine…** | Sprite sheet, sidecar, and optionally the Unity importer. |

Both keys can be rebound like any other — they are in **Edit ▸ Configure ▸
Shortcuts** under *File*, and the menu shows whatever you set them to rather
than the factory key.

## Saving as an ordinary picture

**File ▸ Save as image…** writes what is on the canvas as a picture anyone can
open. This is the plain "give me a PNG of this" that the export commands above
do not cover: they write sequences, sheets and engine metadata, and this writes
one image.

| Setting | What it decides |
| --- | --- |
| **Format** | **PNG** keeps transparency and loses nothing — the right answer for artwork, and the default. **JPEG** is smaller, lossy, and **has no transparency at all**. **WebP** is lossy *and* keeps transparency, which is why it is here. |
| **Quality** | 1–100, for JPEG and WebP. Absent on PNG rather than greyed out, because PNG has no such setting. |
| **Size** | A percentage of the document. A larger render draws the strokes onto a larger surface rather than enlarging pixels, so 200 % is genuinely sharper — the same promise the video export makes. |
| **Fill with** | Only for a format with no transparency. The colour that shows through where the drawing is see-through — white unless you change it, which is what you want unless you are matting a sprite onto something specific. |
| **Every frame** | Only on a document with more than one frame. Writes `name_0001.png`, `name_0002.png` and so on beside the name you chose. |

Whatever the timeline is showing is what gets written, and with a camera in the
scene it is what the camera saw. A saved PNG is the same pixels as that frame
from **Export PNGs…** — the same compositing runs behind both.

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
for exactly these three formats and no others, so the rest would be menu entries
that write nothing. Writing a PSD back out is a separate piece of work and is
not built — Lightbox can read a Photoshop file today and cannot hand one back.

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
**Export PNGs…** works regardless, and every compositing package will encode
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

## Autosave

Under **Edit**. Choose off, 30 seconds, 1, 5 or 15 minutes. Zero is a real
answer, not a mistake to guard against.

Autosave writes a **recovery copy** to your app data folder, not over your file.
Recover by opening it. If you would rather it wrote over the real file too,
there is a checkbox — off by default, because silently rewriting the file you
opened takes away the ability to close without saving.

The write happens **in the background**: autosave takes its snapshot in a few
milliseconds and does the disk work off to the side, so it never pauses the
brush — however large the painting has grown.

---
