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
  profile (`LIGHTBOX_PROFILE_DIR`), and `ANTHROPIC_API_KEY` and the other AI variables
  are removed from the app's environment — every `*_API_KEY` and the Ollama
  variables, by rule. AI features therefore do not run — never
  use the lab to judge AI output (the art-director's note).
- **No private document is ever used.** Fixtures are generated from a seed by
  `tools/Lightbox.Bench` (`fixture` command). `owner-shape` matches the counts of the
  document that prompted all this — 11 layers, 64 drawings, ~1,260 strokes, 1920×1080,
  ~7.9 MB — and contains none of its art.

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
| stalls overlapped | which actions were running during the stalls — nested actions both count, so these overlap |

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
| `{"do": "drag", "from": [x, y], "by": [dx, dy], "ms": 1500, "hz": 120}` | a paced drag, real mouse events |
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
