# The performance lab

Measures the real Lightbox, end to end, on your machine, when you choose to — never
in CI. It launches a build, opens a generated document, plays a scripted gesture with
real mouse and keyboard input, and reads back what the app itself says each action
cost. The decision record is `.claude/quality/questions/Q209-*.md`.

```
python perf/lab.py fixture                                  # once: generate the documents
python perf/lab.py run transform-undo --runs 3              # one build
python perf/lab.py ab transform-undo --a OLD.exe --b NEW.exe --runs 3   # before/after, with a verdict
python perf/lab.py report perf/.runs/<run>/summary.json
```

Windows only; Python 3.10+, standard library only. Build Release first
(`dotnet build Lightbox.sln -c Release`); `--build` takes any `Lightbox.App.exe` or the
folder holding it, so an installed alpha build can be measured against a branch.

## Before you start a run

- **It takes the mouse and keyboard.** Keep your hands off until it prints `done`. If you
  move the mouse between steps the run stops and is reported as `interrupted` — it is
  not counted, and nothing is left half-done in your own profile.
- **A locked machine cannot be measured.** Windows refuses synthetic input while the
  session is locked; the lab checks before it starts and stops with exit code 3 and a
  plain message, rather than reporting every run as interrupted.
- **Run it when the machine is quiet.** Another build or test suite on the same machine
  shows up as slowness. The first transform-undo runs measured opening at 18 s and
  27 s on the same build, minutes apart, with other work going on.
- **Nothing touches your profile or your keys.** Every run gets its own throwaway
  profile (`LIGHTBOX_PROFILE_DIR`) — which is also the only place the app honours
  `LIGHTBOX_BRUSH`, the brush a paint scenario asks for, because the brush in hand is
  saved to the profile at exit and a run must never leave one in yours — and `ANTHROPIC_API_KEY` and the other AI variables
  are removed from the app's environment — every `*_API_KEY` and the Ollama
  variables, by rule. AI features therefore do not run — never
  use the lab to judge AI output (the art-director's note).
- **No private document is ever used.** Fixtures are generated from a seed by
  `tools/Lightbox.Bench` (`fixture` command). `owner-shape` matches the counts of the
  document that prompted all this — 11 layers, 64 drawings, ~1,260 strokes, 1920×1080,
<<<<<<< HEAD
  ~7.9 MB — and contains none of its art.
- **Large documents** (2026-10-09, for responsiveness at scale): `large` is 30 layers
  and 200 drawings at 1080p, `large-4k` the same at 3840×2160. Run any scenario on one
  with `--fixture large`; the run is named `<scenario>@large`. `--onion off` writes a
  profile with onion skin off (the owner's own setting) — a fresh profile has it on.
  `jump-to-frame` and `xsheet-ops` click X-sheet cells by layer name and assume the
  owner-shaped document: with 30 layers the cell they aim at is off screen, so they
  stop rather than measure on `large`. `paint-strokes` is the painting scenario
  written for these documents: three strokes, an undo, a flip, a stroke straight after.
=======
  ~7.9 MB — and contains none of its art. `blank-1080p` and `blank-4k` are one empty
  drawing layer over the paper, for the paint scenario.
>>>>>>> origin/main

## What a run reports

The app writes one JSON line per action while `LIGHTBOX_PERF_LOG` is set
(`src/Lightbox.App/Services/PerfLog.cs`), plus a heartbeat that logs every moment the
UI thread did not answer within **50 ms** — three refreshes at 60 Hz, what the eye
can see.

| Metric | Meaning |
|---|---|
| `open.ready_ms` | launch → the document is open and drawn once |
| `open.longest_stall_ms` | the longest freeze while opening |
| `<action>.median_ms` / `.worst_ms` | the action's own cost: `undo`, `transform.begin`, `transform.commit`, `thumbnails`, `publish` (one screen update), `frame.add`, … |
| `stalls.total_ms` / `.longest_ms` | how long the app did not answer, after opening |
| `stroke.move.median_ms` / `.worst_ms` | what one pointer batch costs the UI thread while painting: stamp, preview, publish request |
| `pen.screen.median_ms` / `.worst_ms` | per drawn frame that carried fresh ink: the oldest pointer event in it → on screen. The artist's number |
| `tip.screen.median_ms` / `.worst_ms` | the same frame's newest event → on screen: how stale the ink under the nib is, without the coalescing term |
| `live.pass.median_ms` / `.worst_ms` | one live pass of a textured or simulated brush (wet edge, grain, medium), off the UI thread |
| `live.behind.median` / `.worst` | when that pass landed, how many stroke points the pen had added since it started — the trailing you see. Points, not ms: at the drag's `hz` one point is one event, give or take the near-duplicate samples the stroke drops |
| `stroke.commit.median_ms` / `.worst_ms` | the pen-up hitch: append, probe, undo record |
| stalls overlapped | which actions were running during the stalls — nested actions both count, so these overlap. `pen.screen` and `tip.screen` are left out: they are how long ink waited, not what the app was doing |

The paint metrics are **upper bounds**: while the log is on, every pointer batch and every
drawn ink frame writes a line, and that write sits inside the very numbers it reports.
Small, and the same on both sides of an A/B — but not zero, so compare, do not quote.

A swept scenario reports each variant on its own, as `300px-3000pxs/pen.screen.worst_ms`
and so on, and an A/B judges each variant against the same one on the other build; too
few good runs at any one variant makes the whole sweep inconclusive.

Across runs, **"worst" and "longest" are judged on the minimum** (contention only ever
adds to them) and everything else on the median. `ab` interleaves the two builds and
alternates the order, so drift over the session lands on both; a metric is a
`REGRESSION` when B is more than `--tolerance` (15 %) above A, ignoring anything under
5 ms. Exit codes: 0 pass, 1 regression, 2 too few good runs to say.

## Scenarios

A scenario is a JSON file in `perf/scenarios/`: a fixture, a `repeat` count, and steps.
Positions are fractions of the document (`[0.5, 0.5]` is its centre); the app reports
where three corners of the document are on screen, so zoom, pan and display scaling
need no handling here.

| Step | |
|---|---|
| `{"do": "key", "keys": "ctrl+t"}` | a shortcut — use the defaults in `ShortcutMap` |
| `{"do": "click", "at": [x, y]}` | |
| `{"do": "drag", "from": [x, y], "by": [dx, dy], "ms": 1500, "hz": 120, "legs": 1}` | a paced drag, real mouse events; `legs` > 1 bounces between the ends within the same `ms` |
| `{"do": "hover", "at": [x, y]}` | |
| `{"do": "settle", "quiet_s": 1.0}` | wait for the app to answer and go quiet |
| `{"do": "wait", "ms": 200}` | |

Before every step the runner waits for the app to answer; nothing is queued into a
frozen app. Each layer's first drawing in a generated fixture is centred, so a gesture
at the centre lands on ink — but a transform's pivot sits at the centre of its box, so
start a move drag a little off it (`transform-undo` uses `[0.53, 0.53]`).

| Scenario | Measures |
|---|---|
| `open-document` | opening the owner-shaped document |
| `transform-undo` | select all, transform, move, commit, undo — three rounds |
| `flip-keys` | flipping key to key with 1 and 2 |
| `paint-stroke` | four strokes across a blank canvas with the brush in `brush`, swept over `sizes` (60, 150, 300 px) and `speeds` (900 and 3000 px/s of document), size outermost — large brushes and a fast pen are where drawing falls over. A faster variant bounces the same 1.5 s drag across the canvas more times (whole legs, so the speed reached is the nearest the drag allows), keeping the stroke's duration and sample count. `--sizes 60,300` and `--speeds 900,6000` replace the lists, `--hz 240` the event rate, `--brush builtin-ink` is the control (a `--brush id:size` runs that one size), `--fixture blank-4k` puts it on a 4K canvas. Sizes are 1–500; above that the app clamps and the run is refused |

Planned, from the owner's list: first playback, adding a frame, click-to-jump on the
timeline (scrubbing is fine; jumping is not), and the X-sheet operations that lag.

## Behaviour checks

Timing is half of it. A fix can pass every headless test and still fail in an
artist's hands, because the tests call the view model directly and skip the real
input path — routing, focus, which control the pointer actually lands on. Twice in
one day the owner reported, after being told something was fixed, that it still was
not (Delete and pull on empty cells; Shift/Ctrl+click in the layer docker).

```
python perf/lab.py check xsheet-delete-pull-menu
python perf/lab.py check layers-multiselect --build path/to/Lightbox.App.exe
```

A behaviour scenario finds its targets **by name** and checks the document **as the
app itself reports it**, over the lab instance's own pipe:

| Step | |
|---|---|
| `{"do": "click", "target": {"xsheet": ["Ink", 2]}, "button": "right"}` | an X-sheet cel by layer and frame |
| `{"do": "click", "target": {"layer": "Color"}, "mods": "ctrl"}` | a layer-docker row, Ctrl or Shift held |
| `{"do": "click", "target": {"folder": "Character"}}` | a folder header |
| `{"do": "click", "target": {"menu": "Delete and pull"}}` | an item of the menu the last right-click opened |
| `{"do": "expect", "row": ["Ink", "A . B . . . . ."]}` | a layer's row: drawing ids, `.` for an empty cel |
| `{"do": "expect", "frame_count": 7}` / `"selected": [...]` / `"folder_selected": "..."` / `"status_contains": "..."` | |

`"window": "maximised"` and `"panels": ["Xsheet"]` set the stage, whatever a fresh
workspace shows. A target that is laid out but covered — a row scrolled out of
sight, a panel still settling — is refused rather than clicked: the first run
clicked two hidden rows and read the misses as the app ignoring them. A failed
expectation prints every layer's row, the selection and the status line. Every lab
run also arms the input trace, so the layer-selection notes — which row, which
modifiers arrived, which handler took the press; row indices and method names, never
layer names — land in its profile's `logs/diagnostics.log`. A lab instance needs
`LIGHTBOX_LAB=1`, which only `lab.py` sets, and serves its own pipe
(`lightbox-ipc-<pid>`): the MCP bridge never reaches it, and it never answers on the
shared one.

**Two traps the lab itself fell into, both fixed, both worth knowing:** a drag must
be real `SendInput` moves (a `SetCursorPos` teleport is not a drag to the app), and
Delete, Insert, the arrows, Home, End and the page keys must carry the
extended-key flag — without it Windows treats Shift+Delete as Shift+numpad-Del,
releases Shift itself, and the app sees plain Delete.

**Pen input is not in the lab yet.** Windows' synthetic pen API accepted the
injection on the development machine and delivered nothing; pen-only faults (B391's
Windows Ink echo) still need a real tablet.

| Behaviour scenario | Checks |
|---|---|
| `xsheet-delete-pull-menu` | right-click → Delete and pull on a hold between drawings, then on trailing empties (Q212) |
| `xsheet-delete-pull-key` | Shift+Delete over the X-sheet on a hold |
| `xsheet-delete-pull-block` | a Shift-selected block of holds, Shift+Delete |
| `layers-multiselect` | Ctrl+click, Shift+click range, folder click, Ctrl+click after a folder |

## Results

`perf/.runs/<time>-<scenario>-<tag>/` holds every run's `perf.jsonl` (the raw log), its
`result.json`, the throwaway profile, and a `summary.json` for the set. Nothing under
`perf/.runs` or `perf/.cache` is committed.
