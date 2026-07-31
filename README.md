# Lightbox

An **AI-native, raster-first** art and animation application — in the spirit of Krita/Photoshop, built for hand-drawn frame-by-frame animation where inbetweens are near-indistinguishable from the original drawings.

Built with **C# / .NET 8**, **Avalonia** (Windows · macOS · Linux), and **SkiaSharp**.

## The core ideas

1. **AI-native document format.** A Lightbox document is plain JSON: scenes → layers → cels → frames → strokes. Every stroke is geometry (`points` with `x`/`y`/`pressure`) plus paint parameters (color, brush size/hardness/opacity). An LLM can read, reason about, and *write* artwork directly — no pixels-only opacity wall.

2. **Raster-first with stroke provenance.** You paint with a real raster brush onto raster layers, but the app silently records the stroke geometry behind every brush stroke. A painted frame = `baseline PNG + stroke record`, and strokes are never baked in.

3. **Inbetweens indistinguishable by construction.** Inbetween frames are computed on stroke *geometry* (match → resample → interpolate) and then **re-rendered through the exact same brush pipeline** that painted your keyframes. A generated frame is genuinely painted, not pixel-blended.

## Solution layout

| Project | Purpose |
|---|---|
| `src/Lightbox.Core` | UI-agnostic core: document model, JSON serialization, geometry, the deterministic inbetween engine, exposure-sheet timeline, undo. Zero dependencies beyond the BCL. |
| `src/Lightbox.Raster` | The paint pipeline: stamp-based `BrushEngine`, `FrameRasterizer` (strokes → pixels — the single source of rendering truth), PNG codec. |
| `src/Lightbox.Ai` | Claude integration (Milestone 2): inbetween generation and text-to-strokes drawing via the Anthropic API with structured outputs. |
| `src/Lightbox.App` | The Avalonia desktop app: canvas, brush controls, timeline, onion skin, playback. |
| `tests/*` | xunit suites for every layer, including pixel-level brush tests and headless UI tests. |

## Run on Windows — no admin rights needed

Every push builds a self-contained Windows bundle in CI:

1. Repo → **Actions** tab → newest green `build` run → **Artifacts** → download `Lightbox-win-x64` (you must be signed in to GitHub).
2. Unzip anywhere in your user profile, e.g. `%LOCALAPPDATA%\Lightbox`.
3. Run `Lightbox.App.exe`. Nothing is installed, no .NET required, no admin. If SmartScreen objects (unsigned exe), click **More info → Run anyway** — that also needs no admin.

Prefer building yourself? Install the .NET SDK per-user (no admin) with the official script — `dotnet-install.ps1 -Channel 10.0 -InstallDir $env:LOCALAPPDATA\dotnet` — then `dotnet run --project src/Lightbox.App` from the clone.

## Use Claude without an API key — the MCP server

If you have the **Claude Desktop app** (Pro is enough), your subscription can drive Lightbox directly — no API key. The bundle ships an MCP server (`mcp\Lightbox.Mcp.exe`) that exposes Lightbox to Claude as tools: `get_scene`, `get_frame_strokes`, `render_frame` (Claude *sees* your drawing), `insert_inbetweens`, and `draw_strokes`. Everything Claude does arrives through the same validation and undo path as your own edits — one Ctrl+Z removes it.

Setup:

1. Start Lightbox (it quietly opens a local, per-user pipe for the bridge; nothing on the network).
2. In Claude Desktop: **Settings → Developer → Edit Config**, add (escaped backslashes, absolute path):

```json
{
  "mcpServers": {
    "lightbox": {
      "command": "C:\\Users\\you\\AppData\\Local\\Lightbox\\mcp\\Lightbox.Mcp.exe",
      "args": []
    }
  }
}
```

3. Fully quit Claude Desktop (from the tray) and reopen — servers load at startup.
4. Draw two keyframes in Lightbox, then ask Claude something like:

> You are a professional animation inbetweener connected to my drawing app. Call `get_scene`, then `render_frame` on both keyframes to see them, then `get_frame_strokes` for both. Draw 3 inbetweens and insert them with `insert_inbetweens` (aIndex = the first key). Follow arcs, preserve stroke labels, then `render_frame` your middle inbetween to check your work.

Troubleshooting: if the server never appears, some MSIX installs of Claude Desktop read the config from `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude_desktop_config.json` instead of `%APPDATA%\Claude\` — put the same file in both. Tool errors like "Start Lightbox first" mean exactly that.

## Fully offline AI — Ollama

With [Ollama](https://ollama.com) installed (per-user, no admin) you can point the in-app AI buttons at a local model: `ollama pull qwen3`, then set `LIGHTBOX_OLLAMA_MODEL=qwen3` (and optionally `LIGHTBOX_OLLAMA_URL`, default `http://localhost:11434`) or add `"ollamaModel": "qwen3"` to the settings file. Expect noticeably weaker inbetweens than Claude — this path is for offline pipeline testing; the MCP path is where quality lives. An `ANTHROPIC_API_KEY`, if present, always wins.

## Building and running

Requires the .NET SDK (8 or later; a recent SDK is needed for Avalonia 12's source generators). On Linux, SkiaSharp needs `libfontconfig1`.

```sh
dotnet build            # build everything
dotnet test             # run all test suites (fully headless-safe)
dotnet run --project src/Lightbox.App   # launch the app
```

## Using the MVP (Milestone 1)

- **Paint** with the mouse (pressure-ready pipeline; tablet support is the next milestone). Brush size, hardness, color, and eraser in the toolbar.
- **Timeline** at the bottom: `＋ Frame` (blank), `⧉ Dup` (duplicate), `🗑` (delete), click a cell to jump. `●` = keyed cel, `—` = hold.
- **Onion skin**: previous key tinted red, next key tinted blue.
- **Playback**: `▶ / ⏸` or Space, loops at the scene fps (default 12).
- **Inbetweens**: set the count and easing in the toolbar, then `＋ Inbetween` fills the gap between the current key and the next key with painted, interpolated frames. Undo (`Ctrl+Z`) if the spacing isn't right.
- **AI (needs an API key)**: set `ANTHROPIC_API_KEY` (or add `"anthropicApiKey"` to the Lightbox settings file) and the AI bar lights up. **✦ AI Inbetween** asks Claude to draw the inbetweens — useful where straight interpolation fails (arcs, rotation, overlap); **✦ AI Draw** paints strokes from a text prompt onto the current frame. Both return strokes in the document's own format and go through the same brush re-render as hand-painted frames, and both are one `Ctrl+Z` from gone.
- **Layers**: the layer picker sits in the timeline bar — `＋P` adds a painted (raster) layer, `＋V` a vector layer, `👁` toggles visibility. Painting, inbetweening, and AI all operate on the active layer and respect its kind.
- **Polish**: stroke smoothing on release (toggle), timeline thumbnails, onion-skin depth (1–3), fps control, `Export PNGs…` (numbered image sequence — feed it to ffmpeg for video), and a once-a-minute autosave (`Lightbox/autosave.lightbox.json` in your app-data folder — open it to recover after a crash).
- **Save / Open**: `.lightbox.json` — the whole document, human- and LLM-readable.

## Roadmap
- **M4 — pure-raster inbetweening**: ML frame interpolation (RIFE/FILM via ONNX Runtime) + Claude-vision correspondence for imported/flattened art with no stroke record.
- **Post-MVP**: tablet pressure, advanced brush engine (textured dabs, JSON brush presets), multi-layer UI, fill/coloring, GIF/MP4 export, AI breakdown poses & timing charts.

See `MANUAL_TESTING.md` for the on-device checklist (this repo is developed in a headless environment; windowed behavior needs a manual pass).
