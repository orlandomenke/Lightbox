# Q209 · Performance lab: where it lives, how it drives the app, what it measures first — **answered 2026-10-07**

**Answered:** a performance framework **in the public repository, outside CI**,
that **sets up through the app's own surface and times the gesture with real
input**, and whose **first scenarios come from a measured capture** rather than
from a list. All three were the recommended options.

Raised by the owner, 2026-10-07: tool stalls with ~20 frames and ~10 layers,
predominantly the transform; a headroom band whose advice did not match what
was slow; and *"I want a performance framework that can test the UI and
end-to-end performance. Separate from the CI. Ideally open source… CI requests
quite a lot from the hardware. So a separate testing would allow me to plan and
perform the test when the machine is calm."*

## The three answers

- **Where:** a top-level `perf/` folder in this repository that no workflow
  runs. Chosen over staying local-only (the September harness in the ledger
  worktree, which reverses that earlier call) and over a separate repository
  (which drifts whenever the report format changes). Cost: the scripts are
  written for a reader who is not here.
- **How it drives the app:** setup through a supported surface (open the
  fixture, pick layer, tool and frame), the measured gesture through real OS
  input. Chosen over real input only (a missed click looks exactly like a bug,
  B255's lesson) and over IPC only (it skips input routing and presentation,
  where every fault the owner has felt actually lived).
- **What first:** capture the owner's stall, then build around it. The capture
  (alpha.115, CPU trace + PresentMon + render report) found undo replaying whole
  drawings and rebuilding thumbnails (6–7 s), the transform start rebuilding the
  dock (B393, p90 600 ms) and the commit dropping drawings whole (230–450 ms).
  Those, and the owner's later list — first playback, adding a frame,
  click-to-jump (scrubbing is fine), X-sheet operations — are the first scenarios.

## Implementation choices inside the answer

Taken as defaults, each for a stated reason:

- **Python, standard library only**, like `scripts/`: nothing to install, and
  `SendInput` is reachable through `ctypes`.
- **The app measures itself** (`LIGHTBOX_PERF_LOG`): one line per action, and a
  UI-thread heartbeat that names what was running during every stall. Absent
  unless set. An outside profiler alone cannot say which action a stall belonged
  to; the September harness had to read a render report for exactly that reason.
- **A throwaway profile per run** (`LIGHTBOX_PROFILE_DIR`). The September
  harness left nine brushes at Size 70 in the owner's real profile, and
  overriding `APPDATA` did not redirect the app.
- **Fixtures are generated from a seed**, never copied from the owner's work:
  their documents are private, and a generated one can be regenerated anywhere.
- **Verdicts take the minimum across runs for "worst" and the median for
  pipeline costs, interleaving A and B**, the rules the September harness and the
  timing-test lessons settled on.
