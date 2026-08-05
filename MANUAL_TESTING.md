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
- [ ] `Lightbox.Mcp.exe` works as the Claude Desktop MCP command from the same bundle, at the bundle root beside `Lightbox.App.exe` (it moved out of `mcp\` — B32).
