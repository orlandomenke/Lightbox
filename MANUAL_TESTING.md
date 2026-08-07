# Manual testing checklist

Everything algorithmic is covered by `dotnet test` (headless-safe). The items
below need a real desktop session — they exercise windowing, GPU rendering, and
input feel that a headless environment cannot verify.

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
- [ ] `Lightbox.Mcp.exe` works as the Claude Desktop MCP command from the same bundle, at the bundle root beside `Lightbox.App.exe` (it moved out of `mcp\` — B32).
