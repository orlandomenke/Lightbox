# Lightbox

**Frame-by-frame animation and digital painting, where the drawing stays editable
after you've drawn it.**

Lightbox records the *geometry* of every brush stroke — not just the pixels it
left behind. A frame is a list of strokes; the image is derived from them. That
one decision is what lets a line be moved, recoloured or re-rendered at any
resolution after the fact, and it is what lets an AI draw a genuine inbetween
rather than a cross-fade.

Built with **C# / .NET 10**, **Avalonia**, and **SkiaSharp**. GPL-3.0.

> ### ⚠️ Alpha — not ready to rely on
>
> Lightbox is in **alpha** and under daily development by one person. It is
> published so the work is readable, not because it is finished.
>
> - **No stability guarantee.** The document format still changes, and a file
>   written today may not open in a later build. Do not put work you care about
>   into it.
> - **Windows builds only.** The code targets Windows, macOS and Linux and has
>   only ever been run on Windows. The other two are untested, not supported.
> - **No support, and no release schedule.** Issues are read; nothing is
>   promised.
> - **Not accepting pull requests yet** — see [CONTRIBUTING.md](CONTRIBUTING.md)
>   for why, and what to do instead.

---

## The bet

Every drawing application makes one choice about what a finished mark *is*, and
everything else follows from it.

**Paint applications store pixels.** Photoshop, Krita, TVPaint, Procreate. You
get the full expressive range of a real brush engine — texture, wetness,
granulation, pressure — and the moment the stroke lands, the geometry is gone.
You cannot ask "where was that line" afterwards, because nothing knows.

**Animation applications store vectors.** Harmony, Animate, Moho. The line stays
editable forever, and the price is the mark: a vector line is an outline with a
fill, so it cannot carry charcoal tooth or a wet edge. Every application in this
family restricts brush quality on exactly the layers where geometry is the truth
— Krita's vector layers don't get its brush engine; Illustrator rasterizes its
expensive effects.

**Lightbox stores both.** The stroke record is the document, and the pixels are
produced from it by a real raster brush engine — the same one, every time. A
line drawn with a simulated watercolour brush is still a line you can pick up
and move.

Four rules make that hold, and breaking any of them is treated as a defect here
even when the tests pass:

| | |
| --- | --- |
| **The record is the document** | Everything that paints goes through one engine, so reopening a file re-renders it identically. |
| **Nothing random ever renders** | Scatter, granulation and jitter are hashed from a dab's position, never from an RNG. Re-render a frame a hundred times and get the same pixels. |
| **Pixel settings live on the stroke** | Changing a preference never repaints existing art. Come back to a scene after a month and it is as you left it. |
| **Scale the surface, never the geometry** | Rendering at 2× is the same mark, sharper — not a different mark. |

Rule 2 is the one that matters most for animation and is the least obvious.
Procedural brush variation seeded from a random number generator looks fine on
one illustration and **boils** at 12 fps, because every frame re-rolls the
texture. Seeding from geometry means a mark varies because of *where it is*,
which is how real media varies — and it stays put between renders.

---

## How it compares

Honest version, including where the others are ahead.

| | **Lightbox** | Photoshop | Krita | Harmony | TVPaint |
| --- | --- | --- | --- | --- | --- |
| A finished mark is… | strokes **and** pixels | pixels | pixels | vectors | pixels |
| Full brush engine on editable art | ✅ | — | vector layers excluded | — | — |
| Built for frame-by-frame | ✅ | timeline, not the focus | ✅ | ✅ | ✅ |
| Onion skin per layer, keyed-only, falloff | ✅ | basic | ✅ | ✅ | ✅ |
| Sprite sheets + collision + engine importers | ✅ five engines | — | — | — | — |
| AI that returns **editable strokes** | ✅ | image-level generative | — | — | — |
| Runs a local model, or none at all | ✅ | cloud | n/a | n/a | n/a |
| Imports `.abr` / `.gbr` / `.gih` / `.kpp` brushes | ✅ | `.abr` | `.kpp`, `.gbr` | — | — |
| Character rigging | ❌ symbols, no rig UI | — | — | ✅ best in class | limited |
| Production tracking / review | ❌ | — | — | ✅ | ✅ |
| Maturity | **alpha, one developer** | decades | mature | industry standard | industry standard |
| Price | free, GPL-3.0 | subscription | free, GPL | $$$$ | $$$ |

**Where the others win, plainly:** Harmony's rigging and studio pipeline are not
close to matched here, and won't be soon. TVPaint and Harmony are what films
actually ship on. Krita is a mature, free, excellent painting application with a
decade of polish Lightbox does not have. If you need to deliver work this
quarter, use one of those.

**What Lightbox is actually for:** the space between them. Hand-drawn animation
where the marks matter, the assets need to reach a game engine, and the tedious
frames could be filled by something other than your wrist.

---

## What's in it today

Everything below is built and reachable in the application. Anything not built
is in the next section — this list does not include plans.

### Drawing

- **Brush engine** with a full editor: size, hardness, flow, opacity, spacing,
  scatter, wet edge, granulation, roundness, rotation, per-dab jitter
- **Drawn pressure curves** — seven targets, not a gamma slider. Pressure drives
  size, flow, hardness, scatter, roundness, and a smudge's colour rate and drag
- **Brush tips**: eight built-ins generated from recipes, a procedural generator
  (bristle, superellipse, polygon, spatter, halo, chisel, hatch, ring), and a
  workshop that turns **scans into tips** with levels, crop and edge masking
- **Import the brushes you already own** — Photoshop `.abr`, GIMP `.gbr`/`.gih`,
  Krita `.kpp`, with per-file progress, cancel, and a library to tidy them
- **Stabilization** — lazy mouse, weighted, predictive — as a *brush* setting, so
  two brushes can steady the hand differently
- **Simulated media** — watercolour, gouache, oil, ink — each with a fast
  counterpart, and a cost badge on the picker so an expensive choice is a
  knowing one
- **Smudge, blend and mixer** brushes that sample all layers, live or frozen
- **Textures** from the built-in papers or your own scans, anchored to the
  document so two strokes crossing the same patch sit on the same tooth
- Eraser variants, per-brush blend modes, tablet pressure

### Canvas, colour and structure

- Rotation, mirroring, per-document framing, canvas quality control
- **Live palettes** — recolour a swatch and every stroke painted from it follows
- Colour wheel with history, gradient editor and gradient tool
- Perspective rulers, vanishing points, grid snapping, vector guides, rulers,
  shape tools (shapes are ordinary strokes, so they carry a real brush)
- Layers with blend modes, folders, lock and alpha lock
- Selections, warp transform
- **Pick a whole line** and move, delete or recolour it — one undo step each

### Animation

- Multi-layer timeline and **X-sheet**, scrubbing, playback speed, loop regions,
  frame markers, animation notes
- **Onion skin, fully built out** — on/off, depth, per-layer, colour-coded,
  keys-only, per-frame falloff curve, light table, draw-over, ghost poses, and it
  survives a restart and a workspace switch
- **Deterministic inbetweening** — match, resample, interpolate on geometry, then
  re-render through the same brush pipeline. A generated frame is genuinely
  painted, not pixel-blended
- **Symbols** — a live asset referenced by id, not a copy. Edit the sword once
  and every animation holding it updates. Pose, expression, hand, face, prop, FX,
  background and animation libraries, with tagging, search, versioning and a
  project-wide dependency graph
- **Timing presets** — save an exposure pattern and apply it to a range
- **Camera** — keyframed pan, zoom and roll, preview, and export through it.
  Optional and *absent* from a document that never asks for one

### Projects

- The unit of work is bigger than a file: palettes, brushes, tips and references
  are declared on a folder and resolved by walking up the tree
- Project types (Illustration / Animation / Game Art / Storyboard / Comic /
  Asset Library / Empty) that set **defaults, never availability** — every
  feature is reachable in every project type
- Convert a project between types with no artwork recreated
- Dockable panels, per-workspace layouts, custom and context-aware shortcuts
- Autosave, crash reports with the exact build, and recovery

### Game export

One-click, and further along than most of the application:

- Sprite sheets with **consistent trimmed bounds across a sequence**, skyline
  packing, atlas optimisation
- Pivots (multi-frame, named), sockets, collision shapes, hitbox/hurtbox editor,
  physics shapes
- Frame events, animation events, tags and clips, frame durations
- **Engine exporters** — Unity, Godot (with a GDScript importer), GameMaker,
  Unreal Paper2D (with an in-editor Python importer), MonoGame and Raylib

### AI

Every AI feature here takes something **you** authored and does the tedious part
of it. There is no prompt box that turns an idea into a drawing, and that is a
statement about what this application is rather than a gap. Two more rules:

- **A model never renders.** The AI produces strokes; the ordinary deterministic
  pipeline draws them. Delete the AI's output and the render is byte-identical to
  what the record alone produces.
- **Everything it does is one Ctrl+Z from gone**, because it goes through the
  same document editor and undo stack a menu item does.

What exists:

- **AI inbetweening** — for the cases straight interpolation gets wrong: arcs,
  rotation, overlap
- **Six providers behind one interface** — Claude, GPT, OpenRouter, Ollama, any
  OpenAI-compatible endpoint, or an MCP server you supply. The settings page is
  generated from the catalogue, so each shows only its own fields
- **Runs entirely offline** with Ollama, or **switch AI off completely** — off
  removes the AI bar rather than greying it, for a studio that wants it nowhere
  near a shot
- **A connection test that draws** rather than pings, and checks the inbetween
  lands *between* the two keys — which is what catches a model that answers in
  perfect JSON and cannot animate
- **An MCP server**, so an agent works your open document directly: read the
  scene, see a rendered frame, insert inbetweens, add strokes
- A measured cost budget in the test suite, so a change that doubles what a
  request costs fails at home rather than on a bill

---

## What it can't do yet

The same list an honest reviewer would write.

- **No rigging UI.** Symbols are reusable assets, not a bone hierarchy.
- **Vector editing is one-way.** A stroke is stored as geometry and *carries* a
  real brush, but there is no tool to drag its points afterwards. Designed, not
  built.
- **No plain "save as PNG/JPEG".** Export writes sheets and sequences for
  engines; a single picture is not there yet.
- **No PSD import or export.**
- **No layer masks, clipping masks, adjustment layers or non-destructive
  filters.**
- **No symmetry or mirrored painting.**
- **No tilt or stroke speed** in the record — a tablet's tilt is not read.
- **Zooming magnifies pixels** rather than re-stamping the line, so a 400% view
  is softer than the document could draw.
- **A large painting is slow to reopen.** Rebuilding a frame from strokes is
  linear in stroke count: a 10 000-stroke painting takes about 106 seconds. It's
  the known cost of storing geometry as the truth, it's filed as B30, and the fix
  is designed. *Drawing* is unaffected — stroke 8 000 costs what stroke 8 did.
- **No collaboration or production tracking.**

The full, candid list is [`BUGS.md`](.claude/quality/BUGS.md) — including what's
broken and what was decided badly.

---

## Install (Windows, no admin needed)

1. **[Releases](../../releases)** → download `Lightbox-win-x64-….zip`
2. Unzip anywhere in your user profile, e.g. `%LOCALAPPDATA%\Lightbox`
3. Run `Lightbox.App.exe` — nothing is installed, no .NET needed, no admin

Releases are cut on request: publishing a `v*` tag builds one, and
**Actions ▸ release ▸ Run workflow** builds a bundle from any branch without
making a release.

<details>
<summary><b>If SmartScreen blocks it</b> (and "Run anyway" is hidden by policy)</summary>

SmartScreen only screens files carrying the Mark-of-the-Web download tag —
remove the tag and it never triggers. Any of these work without admin:

```powershell
# A) Extract with tar (built into Windows 10+; writes no download tags)
mkdir $env:LOCALAPPDATA\Lightbox
tar -xf $env:USERPROFILE\Downloads\Lightbox-win-x64.zip -C $env:LOCALAPPDATA\Lightbox

# B) Or unblock the zip BEFORE extracting with Explorer
Unblock-File $env:USERPROFILE\Downloads\Lightbox-win-x64.zip

# C) Or untag an already-extracted folder in place
Get-ChildItem $env:LOCALAPPDATA\Lightbox -Recurse | Unblock-File
```

Two helper scripts in `scripts/` automate this: `get-build.ps1` takes the newest
zip from Downloads and unblocks + extracts it; `watch-builds.ps1` watches a
folder and unblocks anything dropped in.

If it blocks with an *administrator* message instead, that's AppLocker/WDAC app
control — build from source instead; locally built binaries carry no download
tag.

`get-build.ps1` also repoints a `Lightbox` junction at the build it just
extracted, so every build keeps its own `Lightbox-win-x64-<kind>-<branch>-<sha>`
folder — tellable apart when something regresses — while one fixed path always
means the newest. That is the path to hand to anything that has to survive an
upgrade, the Claude Desktop config below being the case it was added for. Pass
`-LinkName ''` to skip it.
</details>

<details>
<summary><b>Version numbers</b></summary>

The base version is `<VersionPrefix>` in `Directory.Build.props`, and names the
version being worked *toward*. Nothing increments it automatically.

| Built by | Version | File |
| --- | --- | --- |
| tag `v0.2.0` | `0.2.0` | attached to a Release |
| tag `v0.3.0-beta.1` | `0.3.0-beta.1` | semver sorts a beta before `0.3.0` |
| **Run workflow** | `0.1.0-alpha.N` | a 14-day artifact |

The number is inside the executable too — right-click ▸ Properties, and at the
top of every crash report. It reads `0.1.0-alpha.17+9f3c1ab`: the version, then
the exact commit.
</details>

---

## Connect it to Claude

Two independent directions, and it's worth knowing which one you want.

**An agent drives Lightbox** (no API key — a Claude Desktop subscription is
enough). The bundle ships `Lightbox.Mcp.exe` in the `mcp\` folder. Point Claude
Desktop at it:

```json
{
  "mcpServers": {
    "lightbox": {
      "command": "C:\\Users\\you\\AppData\\Local\\Lightbox\\mcp\\Lightbox.Mcp.exe"
    }
  }
}
```

If you unpack with `scripts/get-build.ps1`, point `command` at the fixed
`Lightbox` junction it maintains rather than at the folder the build landed in —
`…\Builds\Lightbox\mcp\Lightbox.Mcp.exe`. A path with a commit in it is stale by
the next build, and this is the config where that is *silent*: Claude Desktop
reports nothing, the server simply never starts and the tools are absent. The
script prints the exact line to paste when it finishes.

Start Lightbox first (it opens a local per-user pipe — nothing on the network),
fully quit and reopen Claude Desktop, then ask it to work: `get_scene`,
`render_frame` (Claude *sees* your drawing), `get_frame_strokes`,
`insert_inbetweens`, `draw_strokes`. Every edit is one undo step.

**Lightbox calls out to a model** — **Edit ▸ Configure ▸ AI**, pick a provider,
fill in what it asks for. Anything behind this contract works:

```
tools/call { name: <tool>, arguments: { system, prompt, schema } }
→ { content: [{ type: "text", text: "<json matching schema>" }] }
```

Local models produce noticeably weaker inbetweens than a frontier one; that path
is for working offline and for testing the pipeline.

> **Upgrading and the server has vanished?** Check whether your config says
> `…\Lightbox\Lightbox.Mcp.exe` or `…\Lightbox\mcp\Lightbox.Mcp.exe`. The path
> moved out of `mcp\` and back again; the current answer is `mcp\`. Claude
> Desktop fails silently on a bad `command` — the tools simply don't appear.

> **Upgrading and a fixed bug is still happening?** Ask for `get_scene` and read
> the two build strings it returns. `appBuild` is the running Lightbox and
> `mcpBuild` is the server Claude Desktop launched; they should be the same
> commit. **If they differ, only one half was republished — and if `mcpBuild` is
> missing entirely, the server predates the stamp and is definitely old.** The
> server is a *published* executable, so a fix in the source does nothing until
> it is rebuilt and Claude Desktop is fully quit and reopened; reloading is not
> enough, because the existing server process keeps running. The same string is
> the first line the server writes to stderr, so it is in Claude Desktop's logs
> even when the server fails to start.

---

## Build from source

```sh
dotnet build                             # everything
dotnet test                              # all suites, fully headless
dotnet run --project src/Lightbox.App    # launch
```

**One .NET version: the .NET 10 SDK.** Every project targets `net10.0`, so the
SDK that builds the repo carries the runtime that runs it. On Linux, SkiaSharp
also needs `libfontconfig1`. The easiest route to both is the devcontainer — open
in GitHub Codespaces, or in VS Code with the Dev Containers extension.

| Project | What's in it |
| --- | --- |
| `src/Lightbox.Core` | Document model, JSON serialization, geometry, the deterministic inbetween engine, exposure sheet, undo. No rendering, no UI. |
| `src/Lightbox.Raster` | `BrushEngine` — the only path to a pixel — flood fill, frame rasterization, tiling. |
| `src/Lightbox.App` | The Avalonia application: canvas, dockers, view models, compositing. |
| `src/Lightbox.Ai` | Providers behind one `IAiArtist`. |
| `src/Lightbox.Mcp` | The MCP server an agent connects to. |
| `src/Lightbox.Import` | Brush importers — `.abr`, `.gbr`, `.gih`, `.kpp`. |
| `tests/*` | Four xunit suites, including pixel-level brush tests and headless UI tests. |
| `tools/Lightbox.Bench` | Performance sweeps — scaling curves and cliffs, not just a ratchet. |

## Documentation

| | |
| --- | --- |
| [**User manual**](docs/MANUAL.md) | What the application does today, one file per section. Marks anything unbuilt as *Planned*. |
| [**Roadmap**](.claude/quality/ROADMAP.md) | Six pillars and the drawing floor. **The checkboxes are derived from the code**, not asserted — delete a feature and its box un-ticks. |
| [**Bugs**](.claude/quality/BUGS.md) | Every known defect, each naming the test that closes it. |
| [**Open questions**](.claude/quality/QUESTIONS.md) | Decisions not yet made, and the reasoning behind the ones that were. |
| [`docs/DESIGN-*.md`](docs/) | Design notes — the payload budget, performance, the brush tips, the infinite canvas. |

Those files are candid by design about what is broken and what was decided
badly. That is deliberate: a ledger that flatters the project is worth nothing.

## Licence

**GPL-3.0** — see [LICENSE](LICENSE). Pull requests are not being accepted while
Lightbox is alpha; [CONTRIBUTING.md](CONTRIBUTING.md) explains why and what to do
instead.
