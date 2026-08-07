# Manual testing checklist

Everything algorithmic is covered by `dotnet test` (headless-safe). The items
below need a real desktop session — they exercise windowing, GPU rendering, and
input feel that a headless environment cannot verify.

## Driving the real app in a container, when a screenshot is the only evidence

Most of this file is for a person at a screen. Some of it can be automated, and
**cursor-to-mark alignment is the case worth the setup**: the brush ring is drawn
in the canvas's render op under a Skia lease, so no headless test can capture it
beside the ink, and the transform bug of 2026-08-07 was invisible to 2 898 green
tests. The recipe:

```sh
apt-get update                                    # <- do this FIRST
apt-get install -y --no-install-recommends xdotool x11-apps imagemagick
Xvfb :99 -screen 0 1280x900x24 &
DISPLAY=:99 dotnet run --project src/Lightbox.App &
DISPLAY=:99 xdotool search --name Lightbox         # window ids, once it is up
DISPLAY=:99 xdotool mousemove 500 300 mousedown 1  # …drag…  mouseup 1
DISPLAY=:99 import -window root shot.png
```

Three things that cost time to find out:

- **`apt-get install` without `apt-get update` first fails**, and it fails
  looking like a network policy refusal rather than a stale index. The proxy does
  block two third-party PPAs, and `update` reports those loudly while still
  fetching the main archive successfully — so read past the warnings.
- **`import -window root` is the reliable capture.** Xvfb's `-fbdir` also works
  and needs no ImageMagick, but it writes an XWD file whose header you then have
  to decode (`bits_per_pixel` is word 11 and `bytes_per_line` word 12 of
  `X11/XWDFile.h`, not the neighbours you will guess).
- **Measure the screenshot, do not squint at it.** `convert x.png txt:-` gives
  per-pixel values; the densest row and column of ink are the stroke, and
  comparing those against the coordinates you passed to `xdotool` is the whole
  measurement. Crop the canvas area first, or the panel borders become the
  densest row and you will "find" a 240 px error that is a window chrome edge.

Still true, and the reason this is a supplement rather than a replacement:
`CLAUDE.md` prefers a headless pixel test, because a dropped synthetic click
looks exactly like a bug. Use this to *see*, and land a pixel test to *guard* —
`CursorAlignmentTests` is the pair to this recipe.

## Before that: look at the sheets

```sh
scripts/visuals.sh            # everything, into artifacts/visuals
scripts/visuals.sh media      # just the sheets whose test name matches "media"
```

Contact sheets rendered by the visual tests, for the checks that are about how
something *looks* rather than whether it runs. They need no display, take a few
seconds, and each one is produced by a test that already fails on its own — so
the pictures are for judgement, never for coverage. What is worth opening:

| Sheet | The question it answers |
| --- | --- |
| `blur-drag-vs-commit-*` | Does what you see while dragging a blur match what commits? (B54) |
| `blur-drag-vs-commit-*-rim` | The same at ×8, where the residue B54 knowingly accepted lives |
| `canvas-blur-release`, `canvas-smudge-release` | The same question through the real canvas, pen-down against released (B69) |
| `scale-smudge`, `scale-blur` | Does an effect brush land where it was dragged when the output scale changes? (B57) |
| `media-strip` | The simulated media at true size, with the same brush unsimulated beside them (B50) |
| `brushes-0*` | Every shipped preset drawing its own stroke at its own size |

Two habits worth keeping. **A difference panel that is flat mid-grey means
identical** — the amplification factor is printed on it, so a shape there is a
few 255ths and not a catastrophe. And **check that both panels have a mark in
them**: two blank panels agree perfectly, and a probe in this repository once
reported 100% coverage on an empty image.

## Launch

- [ ] `dotnet run --project src/Lightbox.App` opens a dark-themed window titled "Lightbox".
- [ ] A plain orange panel with no title bar appears first, and does not appear in the taskbar.
- [ ] It stays up for a moment even on a fast machine — it never blinks in and out. (The minimum is asserted in `StartupTimingTests`; whether it *reads* as a flash is this line.)
- [ ] The main window replaces it with no flash of an empty or white window in between, and the panel does not linger over the main window.
- [ ] The start screen appears **over the main window**, centred on it, with the orange panel already gone.
- [ ] After the handoff, typing goes to the main window without clicking it first. (Focus is the one part of the sequence no test here can reach.)
- [ ] The canvas shows a white 960×540 "paper" centered on a dark background, scaled to fit the window; resizing the window rescales it without distortion.

## Painting

- [ ] Left-drag paints a black stroke that follows the cursor with no visible lag or rubber-banding.
- [ ] Stroke appears *while* dragging (live preview), not only on release.
- [ ] Brush size slider changes stroke width; hardness slider softens the edge (low hardness = airbrush-like falloff).
- [ ] Color field accepts hex values like `#cc3311` and paints in that color.
- [ ] Eraser toggle removes paint where you drag, and leaves the white paper visible (it erases layer content, not the paper).
- [ ] A single click (no drag) leaves a single dab.
- [ ] Fast scribbles look smooth (intermediate pointer events are captured).

## Timeline

- [ ] `＋ Frame` adds an empty frame after the current one and moves the playhead to it.
- [ ] `⧉ Dup` duplicates the current drawing; editing the duplicate does not change the original.
- [ ] `🗑` deletes the current frame (refuses when only one frame remains).
- [ ] Clicking timeline cells jumps frames; the current cell is highlighted blue; keyed cells show `●`, holds show `—`.
- [ ] Left/Right arrow keys step frames.

## Onion skin

- [ ] With onion skin on, the previous key shows tinted red and the next key tinted blue, both ghosted.
- [ ] Onion skin disappears during playback.

## Playback

- [ ] Space (or ▶/⏸) plays the timeline in a loop at ~12 fps; drawing is ignored while playing.
- [ ] Playback resolves holds (a held drawing stays on screen for its full exposure).

## Inbetweens (the headline)

- [ ] Draw a shape on frame 1, `＋ Frame`, draw the same shape moved/deformed on frame 2, select frame 1, set count = 3, press `＋ Inbetween`.
- [ ] Three new frames appear between the keys; played back, the shape travels smoothly.
- [ ] The inbetween strokes look *painted* — same brush character as the keys, no ghosting or double lines.
- [ ] Easing choices visibly change the spacing (EaseInOut clusters inbetweens near the keys).
- [ ] `Ctrl+Z` removes the inserted inbetweens in one step.

## Undo / redo

- [ ] `Ctrl+Z` undoes stroke-by-stroke; `Ctrl+Y` redoes.
- [ ] Undo after frame operations (add/dup/delete) restores the timeline exactly.

## Save / open

- [ ] Save produces a `.lightbox.json`; open it in a text editor — it should be readable JSON with strokes and points.
- [ ] Re-opening the file restores the animation pixel-identically (strokes re-render through the same brush pipeline).

## The brush ring (B72, B74)

Everything here is checked by eye on purpose. The ring is drawn in the canvas's
render op rather than into the published snapshot, and the suite runs on
Avalonia's headless *software* drawing, so no test can capture the frame. The
silhouette is asserted exactly in `BrushTipOutlineTests` and the wiring in
`BrushGizmoTests`; whether it *looks* right is this list.

- [ ] Hover the canvas, then drag the brush-size slider. The ring resizes as you
      drag, **without** moving the pointer. (B72 — it used to wait for a move.)
- [ ] Same with `[` and `]`.
- [ ] Pick a brush with a chisel tip. The ring is a chisel at the tip's angle, not
      a circle. Turn **Tip rotation**: the ring turns with it.
- [ ] Set **Roundness** to about 0.3 on a round brush. The ring flattens into an
      ellipse.
- [ ] Import an `.abr` or `.gbr` tip and select it. The ring outlines that tip's
      actual shape. A tip with holes — a bristle or a ring — shows its holes.
- [ ] The ring is an outline at every size; nothing is filled in, and at a 300 px
      brush the line is still about one pixel rather than fat.
- [ ] Switch to the eraser. The ring shows the *eraser's* tip and size, and
      switching back shows the brush's again.
- [ ] Zoom to 800% and to 10%. The ring tracks the mark's on-screen size at both.
## Project panel against the disk (B61)

The one link no test asserts is the debounce **timer** firing: whether a
`DispatcherTimer` ticks under a headless pump is a fact about Avalonia's test
harness rather than about the fix, so a test resting on it would fail for
reasons nobody could read. The coalescing it drives is covered by
`ProjectDockerTests.ABurstOfDiskEventsCostsOneRefresh`, and the event actually
arriving by `ADeletionOnDiskReachesTheRowWithoutARefreshCall`. This is the line
between them.

- [ ] With a project open, delete one of its animation files in a file manager.
      Within about a second, and **without touching Lightbox**, that row reads
      *not on disk*. It must not disappear.
- [ ] Put the file back. The flag clears on its own, so this reports the world
      rather than latching on first sight.
- [ ] Switch a git branch, or unzip something, inside the project folder. The
      panel updates once and does not stutter — a burst has to cost one re-read,
      not one per file.
- [ ] Start a rename (right-click ▸ Rename…), then save with `Ctrl+S` while the
      edit box is open. The row being renamed must still be the row you were
      editing.
- [ ] Press **F5**, and click **⟳** in the panel header. Both report what they
      found in the panel's status line.
- [ ] Rebind F5 in **Edit ▸ Configure ▸ Shortcuts** and confirm the new key
      works and F5 no longer does.

## The rig overlay (B58)

The whole point of B58 is that this was unreachable, so the first item is the
bug: before the fix there was no way to get to any of the rest.

- [ ] **View ▸ Rig ▸ Edit anchors and hitboxes** exists, and `Ctrl+K` toggles it.
      Rebind it in **Edit ▸ Configure ▸ Shortcuts** and confirm the new key works.
- [ ] With the mode on, **Add anchor** puts a blue cross in the middle of the
      canvas and **Add collision shape** puts an orange rectangle there. Both are
      draggable straight away.
- [ ] Drag the shape's body to move it; drag a corner to resize. The opposite
      corner stays put, and dragging past it flips the rectangle rather than
      inverting it.
- [ ] The selected mark is white and the shape shows four corner handles.
      Unselected shapes have none.
- [ ] A drag is **one** undo step, not one per pointer event. `Ctrl+Z` once puts
      it back where it was.
- [ ] Click an anchor sitting inside a shape: the anchor is what gets selected,
      and it is drawn on top.
- [ ] Zoom to 800% and 10%. Crosses and handles stay the same size on screen.
- [ ] With the mode on, dragging on empty canvas must **not** paint. Turn the mode
      off and confirm the same drag does paint again.
- [ ] Turn the mode off with marks placed: the overlay disappears entirely, rather
      than staying faintly over the drawing.
- [ ] Place a socket while parked on a **held** frame, then re-time the sequence.
      The mark travels with its drawing, and the hold stays a hold.

## Per-document framing (B67)

The mechanism has tests; what they cannot reach is the one line in
`MainWindow.axaml.cs` that subscribes the window to `TabSwitched` — a
`MainWindow` cannot be constructed headlessly, so if that line were deleted the
suite would stay green and nothing here would work. **These checks are that
line.**

- [ ] Zoom one document to ~400% and frame a detail. **File ▸ New**: the new
      document opens fitted at 100%, not at 400%.
- [ ] Switch back: the first document is at 400%, on the same detail. Switch
      forward again: the second is still at 100%.
- [ ] Mirror (`M`) and rotate one document. The other tab is neither mirrored nor
      rotated, and both come back correctly on return.
- [ ] Pan one document far off-centre. It comes back off-centre, not re-centred.
- [ ] Close the document and reopen the file: it opens **fitted**. Framing lasts
      the session, and is not written into the file.
- [ ] Import a reference into two documents, select a later strip in the one with
      more, then switch. The document with fewer strips still shows *its*
      reference rather than nothing.
- [ ] The brush is the deliberate exception: change the size, switch tabs, and it
      is still the size you set. That is Q9, not a regression.

## Project folders, and deleting (B85, B86, B87)

The confirmation dialog is the part no test reaches — the docker decides
*whether* to ask and what the question says, and both are covered, but nothing
headless can open a window and click Delete.

- [ ] **＋ New ▸ Folder** with nothing selected makes a folder at the top. With a
      folder selected, the next one goes inside it, indented.
- [ ] Name one *Act 2 — Interiors*. The panel keeps the em dash; the folder on
      disk is `act-2-interiors`.
- [ ] The chevron hides and shows what is inside. Save the project while a folder
      is collapsed — it must stay collapsed.
- [ ] Create a document with a folder selected: it appears **inside** that folder,
      and the file is at that path on disk.
- [ ] Drag a document onto a folder, and a folder onto another folder. Drag a
      folder onto its own child: nothing happens, and nothing moves.
- [ ] **Remove from project** on a document: the row goes, the file is still
      there. Reopen the project — it must not come back.
- [ ] **Remove from project** on a folder holding a drawing: the folder goes and
      the drawing reappears at the top level rather than vanishing.
- [ ] **Delete permanently…** on an empty folder: no prompt, folder gone from disk.
- [ ] **Delete permanently…** on a folder with things in it: the prompt names how
      many folders and documents. **Cancel is the default** — pressing Enter must
      not delete anything.
- [ ] Confirm it, and check the folder and its files are gone from disk.
- [ ] **＋ New** shows containers first (🗀 Folder, Character, Scene), a
      separator, then drawings (▣ Animation, Shot, Document).
- [ ] Every entry asks for a name, prefilled. **Escape creates nothing** — check
      the panel and the folder on disk.
- [ ] Rename a document: the file on disk is renamed too. Rename a folder with a
      drawing in it: the directory moves and the drawing goes with it.
- [ ] Rename a folder to a name a sibling already has: refused, the edit box
      stays open, and the status line says why.
- [ ] Make a folder and save without putting anything in it. It exists in a file
      manager — an empty folder is a real folder.

## Cross-platform notes

- Linux: needs `libfontconfig1` (`apt install libfontconfig1`).
- If the canvas stays dark gray with no paper on some GPU/driver combo, report it — the renderer has a documented CPU-blit fallback path we can switch to.

## Milestone 3 additions

- [ ] Layer picker: `＋V` adds a vector layer; drawing on it works; `👁` hides/shows it; switching layers changes which drawing receives paint.
- [ ] Onion depth 2–3 shows fainter ghosts further out.
- [ ] "Smooth" toggle visibly relaxes jittery strokes on release.
- [ ] Timeline cells show live thumbnails that update as you paint.
- [ ] fps control changes playback speed immediately (even while playing).
- [ ] `Export PNGs…` writes frame_0001.png… to the chosen folder; frames match what playback shows.
- [ ] After a minute of editing, `Lightbox/autosave.lightbox.json` exists in app-data and opens correctly.

## AI (needs ANTHROPIC_API_KEY)

- [ ] Without a key the AI bar is disabled and the tooltip explains why.
- [ ] With a key: draw two keyframes, `✦ AI Inbetween` → progress bar runs, frames appear between the keys, status reports the count.
- [ ] `✦ AI Draw` with a prompt ("a small house") paints labeled strokes onto the current frame; Ctrl+Z removes them in one step.
- [ ] Cancel (✕) stops a long request; app stays responsive throughout.

## MCP (Claude Desktop, no API key)

- [ ] With Lightbox running and the config entry added (see README), Claude Desktop shows "lightbox" tools after a full restart.
- [ ] `get_scene` returns your canvas/layers; `render_frame` shows Claude the actual drawing.
- [ ] The inbetweener prompt from the README makes Claude insert frames that appear in the timeline immediately; Ctrl+Z in Lightbox removes them in one step.
- [ ] With Lightbox closed, tools fail with "Start Lightbox first" (not a hang).

## Ollama (offline)

- [ ] With `LIGHTBOX_OLLAMA_MODEL` set and Ollama running, the AI bar enables without any Anthropic key.
- [ ] ✦ AI Inbetween produces frames (quality depends on the model); errors mention "is Ollama running?" when it isn't, and `ollama pull` when the model is missing.

## Windows bundle (no admin)

- [ ] The Actions artifact unzips and `Lightbox.App.exe` starts on a machine with no .NET installed and no admin rights.
- [ ] **B115** — double-clicking `Lightbox.App.exe` opens the Lightbox window and **no console or PowerShell window at all**, and there is no second taskbar entry. Nothing on screen can be closed that takes the app down except the app's own window.
- [ ] **B115** — `Lightbox.Mcp.exe` still runs as a stdio server and Claude Desktop still lists the Lightbox tools. This is the check that catches `WinExe` applied to the wrong project: a stdio server with no stdin fails silently, and the tools simply do not appear.
- [ ] **B115** — with `LIGHTBOX_TRACE=1` set, running `Lightbox.App.exe` from a terminal still prints trace lines to that terminal.
- [ ] **B119** — with the console switch on, **Help ▸ Trigger a test failure** appears. Work down it: *a failure the app survives* writes to `diagnostics.log` and names it in the status strip while the app carries on; *write a line to the console* puts text in the console window; *a background task nobody waited for* writes a report and Lightbox keeps running.
- [ ] **B119** — *crash on the drawing thread* is the one that matters most: it is the path a real crash takes, and it is the one the reporter did not listen on before. A report must appear naming `ui-thread`.
- [ ] **B119** — *crash on a background thread* and *crash with a cause underneath* each produce a report; the second one has the inner exception in it.
- [ ] **B119** — *kill the process outright* asks first, then leaves **no** report at all. That is correct and is the honest limit of the reporter; check the autosave copy is still there afterwards.
- [ ] **B119** — turn the console switch off. The whole *Trigger a test failure* submenu disappears immediately, without a restart.
- [ ] **B119** — stack traces in these reports name the **file and line**, not just the method. (The bundle publishes with `DebugType=embedded`; it was `none`, which shipped no debug information at all — the reports would have named methods and nothing else.)
- [ ] **B117** — dismiss that dialog, put the DLL back, and start again. The status strip says the previous run ended unexpectedly and names the file. Start a third time: it does **not** say so again.
- [ ] **B118** — **Help ▸ Open the diagnostics folder** opens `…\Lightbox\logs\` in Explorer, including on a clean install where nothing has been logged yet (the folder is made rather than missing).
- [ ] **B118** — tick **Help ▸ Show a console while drawing**, restart. A console window appears alongside Lightbox. With `LIGHTBOX_TRACE=1` also set, trace lines arrive in it while drawing. This is the check that matters: the app was **double-clicked**, so there was no terminal to attach to and the console had to be created.
- [ ] **B118** — untick it, restart. No console window. The setting survived both restarts.
- [ ] **B118** — **Help ▸ Lightbox 1.0.0+…** shows a build with a commit after the `+`, and clicking it copies that text.
- [ ] **B116** — `Lightbox.Mcp.exe` works as the Claude Desktop MCP command from `mcp\`, beside `Lightbox.App.exe`. (It moved back into `mcp\`; it was at the bundle root for one stretch of builds — B32.)
- [ ] **B116** — the bundle root holds `Lightbox.App.exe`, three native DLLs and the `mcp\` folder, and nothing else.
- [ ] **B116** — `Lightbox.App.exe` starts from the single-file bundle on a machine with no .NET, and rendering is **hardware**: a large document pans and zooms at the usual speed. This is the check that catches a native library silently not resolving — Avalonia falls back to software rendering rather than failing, so the symptom is "the new build feels slow" rather than an error.
- [ ] **Arrow (A)** — click a line on the canvas. It is **traced in cyan** and the tool bar says *1 line selected*. This is the check the suite cannot make: the highlight is canvas chrome drawn in the render op, and the headless tests run on software drawing where a rendered frame cannot be captured at all — the same limit `BrushGizmoTests` records for the brush ring. The wiring is asserted in `StrokeSelectionTests`; that it *looks* right is here.
- [ ] **Arrow** — the trace stays a hairline at 25% and at 800%. A highlight that scaled with the view would vanish zoomed out and become a stripe zoomed in.
- [ ] **Arrow** — Shift-click a second line: both traced. Shift-click one of them again: it drops out. Click empty canvas: everything lets go.
- [ ] **Arrow** — click a **guide** where it crosses a line. The guide is selected, not the line. Then click a placed **symbol**. Neither of these was reachable before the arrow existed — `CanvasToolMode.Select` was never assigned.
- [ ] **Arrow** — switch layers with something selected. The trace goes, because that line is not on this layer.
