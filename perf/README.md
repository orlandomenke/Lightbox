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
- **Run it when the machine is quiet.** Another build or test suite on the same machine
  shows up as slowness. The first transform-undo runs measured opening at 18 s and
  27 s on the same build, minutes apart, with other work going on.
- **Nothing touches your profile or your keys.** Every run gets its own throwaway
  profile (`LIGHTBOX_PROFILE_DIR`), and `ANTHROPIC_API_KEY` and the other AI variables
  are removed from the app's environment. AI features therefore do not run — never
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

## Results

`perf/.runs/<time>-<scenario>-<tag>/` holds every run's `perf.jsonl` (the raw log), its
`result.json`, the throwaway profile, and a `summary.json` for the set. Nothing under
`perf/.runs` or `perf/.cache` is committed.
