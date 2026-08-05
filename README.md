# Lightbox

An **AI-native, raster-first** art and animation application — in the spirit of Krita/Photoshop, built for hand-drawn frame-by-frame animation where inbetweens are near-indistinguishable from the original drawings.

Built with **C# / .NET 10**, **Avalonia** (Windows · macOS · Linux), and **SkiaSharp**.

## The core ideas

1. **AI-native document format.** A Lightbox document is plain JSON: scenes → layers → cels → frames → strokes. Every stroke is geometry (`points` with `x`/`y`/`pressure`) plus paint parameters (color, brush size/hardness/opacity). An LLM can read, reason about, and *write* artwork directly — no pixels-only opacity wall.

2. **Raster-first with stroke provenance.** You paint with a real raster brush onto raster layers, but the app silently records the stroke geometry behind every brush stroke. A painted frame = `baseline PNG + stroke record`, and strokes are never baked in.

3. **Inbetweens indistinguishable by construction.** Inbetween frames are computed on stroke *geometry* (match → resample → interpolate) and then **re-rendered through the exact same brush pipeline** that painted your keyframes. A generated frame is genuinely painted, not pixel-blended.

## Solution layout

| Project | Purpose |
|---|---|
| `src/Lightbox.Core` | UI-agnostic core: document model, JSON serialization, geometry, the deterministic inbetween engine, exposure-sheet timeline, undo. Zero dependencies beyond the BCL. |
| `src/Lightbox.Raster` | The paint pipeline: stamp-based `BrushEngine`, `FrameRasterizer` (strokes → pixels — the single source of rendering truth), PNG codec. |
| `src/Lightbox.Ai` | Inbetween generation and text-to-strokes drawing behind a provider-agnostic `IAiArtist`: Claude, the OpenAI dialect (GPT, OpenRouter, any compatible endpoint), Ollama, or an MCP server of your own. |
| `src/Lightbox.App` | The Avalonia desktop app: canvas, brush controls, timeline, onion skin, playback. |
| `tests/*` | xunit suites for every layer, including pixel-level brush tests and headless UI tests. |

## Run on Windows — no admin rights needed

Every pull request and every push to `main` builds a self-contained Windows bundle in CI (a branch with no PR open does not — use **Actions ▸ build ▸ Run workflow**, which always builds):

1. Repo → **Actions** tab → newest green `build` run → **Artifacts** → download `Lightbox-win-x64-…` (you must be signed in to GitHub).
2. Unzip anywhere in your user profile, e.g. `%LOCALAPPDATA%\Lightbox`.
3. Run `Lightbox.App.exe`. Nothing is installed, no .NET required, no admin.

**How long a bundle lasts.** One is about 74 MB, so they are pruned rather than kept: a branch keeps its **3 newest**, `main`'s are kept 30 days and everyone else's 5, and any feature-branch bundle over a week old is deleted whatever branch it came from. A documentation-only push does not build one at all. If you need a bundle for a commit that has aged out, re-run the workflow from the Actions tab (**Run workflow**) — `workflow_dispatch` always builds.

**If the storage quota fills anyway**, run **Actions ▸ cleanup artifacts ▸ Run workflow**. It prunes on its own without building anything, which matters because the build workflow's own prune cannot rescue a full quota — that prune runs beside an upload, and once the quota is full the upload fails first.

Both share one policy, in `.github/scripts/prune-artifacts.sh`, and it has three rules:

1. **Keep the newest N per branch** (default 3), across every branch — not just the one being built.
2. **Sweep feature-branch bundles older than N days** (default 7). Release and bugfix bundles are exempt; age alone is not a reason to take one somebody kept on purpose.
3. **Hold total storage under a budget** (default 1500 MB), deleting oldest-first until it fits. This is the safety valve: rules 1 and 2 keep storage flat once it is sane, but only rule 3 can unblock a quota that is already full.

Set **keep** to `0` to clear everything, or tick **dry run** to see the list first. Either way it writes what it found and freed to the run summary.

> **The button only appears when this workflow is on the repository's default branch.** `workflow_dispatch` is resolved from the default branch, so on a feature branch there is nothing to click however correct the file is.

GitHub recalculates usage every 6–12 hours, so a build started immediately after a cleanup may still be refused even though the space is genuinely free.

**If SmartScreen blocks it** (and policy hides "Run anyway"): SmartScreen only screens files carrying the Mark-of-the-Web download tag — remove the tag and it never triggers. Any of these work without admin:

```powershell
# A) Extract with tar (built into Windows 10+; writes no download tags)
mkdir $env:LOCALAPPDATA\Lightbox
tar -xf $env:USERPROFILE\Downloads\Lightbox-win-x64.zip -C $env:LOCALAPPDATA\Lightbox

# B) Or unblock the zip BEFORE extracting with Explorer
Unblock-File $env:USERPROFILE\Downloads\Lightbox-win-x64.zip
#    (equivalent: right-click zip → Properties → Unblock → OK)

# C) Or untag an already-extracted folder in place
Get-ChildItem $env:LOCALAPPDATA\Lightbox -Recurse | Unblock-File
```

If it still blocks with an "administrator" message instead of SmartScreen, that's AppLocker/WDAC app-control policy — build from source instead (locally built binaries carry no download tag; see below).

**Automate it** — two helper scripts live in `scripts/` (copy them next to your Builds folder, or run them from the clone):

```powershell
# One command per new build: newest Lightbox-win-x64-*.zip from Downloads →
# unblocked + tar-extracted into a folder named after the zip.
powershell -ExecutionPolicy Bypass -File scripts\get-build.ps1 -Dest C:\path\to\Builds

# Or fully hands-off: watch the Builds folder and auto-unblock anything
# copied or moved into it. Start it at logon via a shortcut in shell:startup:
powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File scripts\watch-builds.ps1 -Folder C:\path\to\Builds
```

There is also a per-user Windows switch that stops download tags being written at all (`SaveZoneInformation=1` under `HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Attachments`) — no admin needed, but it disables that safety net for *everything* you download, and corporate group policy often overrides it. The scripts above are the safer scope.

Prefer building yourself? Install the .NET SDK per-user (no admin) with the official script — `dotnet-install.ps1 -Channel 10.0 -InstallDir $env:LOCALAPPDATA\dotnet` — then `dotnet run --project src/Lightbox.App` from the clone.

## Use Claude without an API key — the MCP server

If you have the **Claude Desktop app** (Pro is enough), your subscription can drive Lightbox directly — no API key. The bundle ships an MCP server (`Lightbox.Mcp.exe`, beside `Lightbox.App.exe`) that exposes Lightbox to Claude as tools: `get_scene`, `get_frame_strokes`, `render_frame` (Claude *sees* your drawing), `insert_inbetweens`, and `draw_strokes`. Everything Claude does arrives through the same validation and undo path as your own edits — one Ctrl+Z removes it.

Setup:

1. Start Lightbox (it quietly opens a local, per-user pipe for the bridge; nothing on the network).
2. In Claude Desktop: **Settings → Developer → Edit Config**, add (escaped backslashes, absolute path):

```json
{
  "mcpServers": {
    "lightbox": {
      "command": "C:\\Users\\you\\AppData\\Local\\Lightbox\\Lightbox.Mcp.exe",
      "args": []
    }
  }
}
```

> **Upgrading from a bundle built before this changed?** The server used to sit
> in an `mcp\` subfolder, so an older config points at
> `…\Lightbox\mcp\Lightbox.Mcp.exe`. Drop the `mcp\` and it works. The move is
> what stopped the bundle shipping a second copy of .NET — 105 MB down to 74 —
> because a self-contained executable only finds its runtime beside itself, so a
> subfolder had to carry its own. Claude Desktop does not report a bad `command`
> loudly: the server simply fails to start and the Lightbox tools are missing.

3. Fully quit Claude Desktop (from the tray) and reopen — servers load at startup.
4. Draw two keyframes in Lightbox, then ask Claude something like:

> You are a professional animation inbetweener connected to my drawing app. Call `get_scene`, then `render_frame` on both keyframes to see them, then `get_frame_strokes` for both. Draw 3 inbetweens and insert them with `insert_inbetweens` (aIndex = the first key). Follow arcs, preserve stroke labels, then `render_frame` your middle inbetween to check your work.

Troubleshooting: if the server never appears, some MSIX installs of Claude Desktop read the config from `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude_desktop_config.json` instead of `%APPDATA%\Claude\` — put the same file in both. Tool errors like "Start Lightbox first" mean exactly that.

## Choosing an AI provider

**Edit ▸ Configure ▸ AI.** Lightbox is not tied to one service: pick from the dropdown and the fields change to what that service needs.

| Provider | Needs | Notes |
| --- | --- | --- |
| Claude (Anthropic) | API key, model | The default, and what the prompts are tuned against. |
| GPT (OpenAI) | API key, model | Strict JSON schema, so replies parse by construction. |
| OpenRouter | API key, model | One key for many vendors' models. |
| Ollama | Model | Local, no key, no network. `ollama pull qwen3` and pick it. |
| Custom (OpenAI-compatible) | Endpoint, model | LM Studio, vLLM, llama.cpp's server, your own gateway. Key optional. |
| Custom agent (MCP) | Command, tool | An MCP server you supply that owns the model. |

**Use AI assistance** is on by default; turning it off removes the AI bar rather than greying it out, and leaves the provider fields usable so one can be set up and tested first.

**Test connection** draws rather than pings, at one of two depths. *Quick* asks for one short line (seconds, a few hundred tokens). *Test with a drawing* adds a real inbetween between two keyframes and checks it lands **between** them — which is what catches a model that answers in perfect JSON and cannot inbetween. Both check the output is usable, not merely that it parsed. The verdict is green (usable), amber (connected, output not usable) or red (not connected). A progress bar and elapsed clock run alongside, and the test can be cancelled.

Environment variables still work and are shown as placeholders where they apply: `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `OPENROUTER_API_KEY`, `LIGHTBOX_OLLAMA_URL`. What you type wins over the environment, which wins over the default; only what you type is stored (in `Lightbox/ai.json` in your app-data folder). An existing `anthropicApiKey` or `ollamaModel` in the old settings file is migrated on first run.

Local models produce noticeably weaker inbetweens than a frontier one — that path is for working offline and for testing the pipeline.

### Bringing your own model over MCP

The **Custom agent (MCP)** provider launches a server you name and calls one tool on it:

```
tools/call { name: <tool>, arguments: { system, prompt, schema } }
→ { content: [{ type: "text", text: "<json matching schema>" }] }
```

Anything behind that contract works. This is the opposite direction from Lightbox's own MCP server above — there an agent calls *in* and works the document directly; here Lightbox calls *out* for strokes. The two are independent.

## Building and running

```sh
dotnet build            # build everything
dotnet test             # run all test suites (fully headless-safe)
dotnet run --project src/Lightbox.App   # launch the app
```

**One .NET version: the .NET 10 SDK.** Every project targets `net10.0`, so the
SDK that builds the repo carries the runtime that runs it. This used to be two
questions — the solution targeted `net8.0` while Avalonia 12's source generators
needed the newer Roslyn only the 10.0 SDK ships, so a machine with one of them
compiled or ran but not both. On Linux, SkiaSharp also needs `libfontconfig1`.

The easiest way to get both is the devcontainer — open the repo in GitHub
Codespaces, or in VS Code with the Dev Containers extension, and
`.devcontainer/devcontainer.json` provisions them. `dotnet test` needs no
display; the UI suite drives Avalonia headlessly.

## Using the MVP (Milestone 1)

- **Paint** with the mouse (pressure-ready pipeline; tablet support is the next milestone). Brush size, hardness, color, and eraser in the toolbar.
- **Timeline** at the bottom: `＋ Frame` (blank), `⧉ Dup` (duplicate), `🗑` (delete), click a cell to jump. `●` = keyed cel, `—` = hold.
- **Onion skin**: previous key tinted red, next key tinted blue.
- **Playback**: `▶ / ⏸` or Space, loops at the scene fps (default 12).
- **Inbetweens**: set the count and easing in the toolbar, then `＋ Inbetween` fills the gap between the current key and the next key with painted, interpolated frames. Undo (`Ctrl+Z`) if the spacing isn't right.
- **AI (needs a provider)**: choose one in Edit ▸ Configure ▸ AI and the AI bar lights up. **✦ AI Inbetween** asks Claude to draw the inbetweens — useful where straight interpolation fails (arcs, rotation, overlap); **✦ AI Draw** paints strokes from a text prompt onto the current frame. Both return strokes in the document's own format and go through the same brush re-render as hand-painted frames, and both are one `Ctrl+Z` from gone.
- **Layers**: the layer picker sits in the timeline bar — `＋P` adds a painted (raster) layer, `＋V` a vector layer, `👁` toggles visibility. Painting, inbetweening, and AI all operate on the active layer and respect its kind.
- **Polish**: stroke smoothing on release (toggle), timeline thumbnails, onion-skin depth (1–3), fps control, `Export PNGs…` (numbered image sequence — feed it to ffmpeg for video), and a once-a-minute autosave (`Lightbox/autosave.lightbox.json` in your app-data folder — open it to recover after a crash).
- **Save / Open**: `.lightbox.json` — the whole document, human- and LLM-readable.

## Roadmap
- **M4 — pure-raster inbetweening**: ML frame interpolation (RIFE/FILM via ONNX Runtime) + Claude-vision correspondence for imported/flattened art with no stroke record.
- **Post-MVP**: tablet pressure, advanced brush engine (textured dabs, JSON brush presets), multi-layer UI, fill/coloring, GIF/MP4 export, AI breakdown poses & timing charts.

See `MANUAL_TESTING.md` for the on-device checklist (this repo is developed in a headless environment; windowed behavior needs a manual pass).
