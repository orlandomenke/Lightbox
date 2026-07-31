# Manual testing checklist

Everything algorithmic is covered by `dotnet test` (73 tests, headless-safe).
The items below need a real desktop session — they exercise windowing, GPU
rendering, and input feel that a headless environment cannot verify.

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
- [ ] `mcp\Lightbox.Mcp.exe` works as the Claude Desktop MCP command from the same bundle.
