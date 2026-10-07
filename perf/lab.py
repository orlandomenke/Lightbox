#!/usr/bin/env python3
"""The Lightbox performance lab (Q209): measure the real app, on demand, outside CI.

    python perf/lab.py fixture                      # generate the documents scenarios open
    python perf/lab.py run transform-undo           # one build, N runs, a summary
    python perf/lab.py ab transform-undo --a OLD.exe --b NEW.exe   # interleaved, with a verdict
    python perf/lab.py report perf/.runs/<run>/summary.json

Standard library only, Windows only (input goes through SendInput). Read perf/README.md
first: a run takes the mouse and keyboard for its duration, and aborts if you move the mouse.

What it relies on in the app, all absent unless the lab asks for it:
  LIGHTBOX_PERF_LOG     one JSON line per action, and a heartbeat logging every UI stall
  LIGHTBOX_PROFILE_DIR  a throwaway profile, so a run never touches the artist's own
  <file>.lightbox.json  on the command line: open it straight away and log "ready"
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import json
import os
import statistics
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

LAB = Path(__file__).resolve().parent
REPO = LAB.parent
CACHE = LAB / ".cache"
RUNS = LAB / ".runs"
DEFAULT_EXE = REPO / "src" / "Lightbox.App" / "bin" / "Release" / "net10.0" / "Lightbox.App.exe"

# Never let a lab run carry the artist's AI configuration into the app it starts:
# the profile is throwaway, but these come from the environment (the AI review's note).
def stripped(name: str) -> bool:
    """By rule, not by list: a hand-kept list had already missed OPENROUTER_API_KEY."""
    upper = name.upper()
    return upper.endswith("_API_KEY") or upper.startswith("LIGHTBOX_OLLAMA_")

STALL_MS = 50.0  # three refreshes at 60 Hz: what the eye can see


# ---- fixtures ------------------------------------------------------------------------

PRESETS = {
    # The owner's document of 2026-10-07, by its render report's counts (no art copied).
    "owner-shape": [],
    # A small named sheet for behaviour checks: Ink A . . B . . . ., Color X . Y . . . . .,
    # Shade and Line inside a folder named Character.
    "sheet": ["--preset", "sheet"],
}


def fixture_path(preset: str) -> Path:
    return CACHE / "fixtures" / f"{preset}.lightbox.json"


def ensure_fixture(preset: str, rebuild: bool = False) -> Path:
    if preset not in PRESETS:
        sys.exit(f"unknown fixture preset {preset!r}; known: {', '.join(PRESETS)}")
    out = fixture_path(preset)
    if out.exists() and not rebuild:
        return out
    out.parent.mkdir(parents=True, exist_ok=True)
    cmd = ["dotnet", "run", "--project", str(REPO / "tools" / "Lightbox.Bench"), "-c", "Release",
           "--", "fixture", "--out", str(out), *PRESETS[preset]]
    print(f"generating fixture {preset} ...")
    subprocess.run(cmd, check=True)
    return out


# ---- Windows input -------------------------------------------------------------------

user32 = ctypes.WinDLL("user32", use_last_error=True) if os.name == "nt" else None


def make_dpi_aware() -> None:
    # Per-monitor v2, so the pixel positions the app reports (physical pixels from
    # PointToScreen) are the ones SetCursorPos and SendInput mean.
    try:
        user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    except (AttributeError, OSError):
        pass


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD), ("dwFlags", wt.DWORD),
                ("time", wt.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD), ("time", wt.DWORD),
                ("dwExtraInfo", ctypes.c_size_t)]


class _INPUTUNION(ctypes.Union):
    _fields_ = [("mi", MOUSEINPUT), ("ki", KEYBDINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("u", _INPUTUNION)]


MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP = 0x0002, 0x0004
MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP = 0x0008, 0x0010
MOUSEEVENTF_MOVE, MOUSEEVENTF_ABSOLUTE, MOUSEEVENTF_VIRTUALDESK = 0x0001, 0x8000, 0x4000
KEYEVENTF_KEYUP = 0x0002
VK = {
    "ctrl": 0x11, "shift": 0x10, "alt": 0x12, "enter": 0x0D, "escape": 0x1B, "space": 0x20,
    "left": 0x25, "up": 0x26, "right": 0x27, "down": 0x28, "delete": 0x2E, "tab": 0x09,
    "insert": 0x2D, "home": 0x24, "end": 0x23, "pageup": 0x21, "pagedown": 0x22, "backspace": 0x08,
    **{chr(c): c for c in range(ord("A"), ord("Z") + 1)},
    **{str(d): 0x30 + d for d in range(10)},
}


def _send(*inputs: INPUT) -> None:
    arr = (INPUT * len(inputs))(*inputs)
    if user32.SendInput(len(inputs), arr, ctypes.sizeof(INPUT)) != len(inputs):
        raise OSError(ctypes.get_last_error(), "SendInput was refused")


# Keys Windows calls "extended". Sent without the flag, Delete is the numpad's Del,
# and Shift+numpad-Del with NumLock on makes Windows release Shift itself before the
# key: the app then sees plain Delete. The first Shift+Delete check failed exactly
# that way and was nearly reported as the app's fault.
EXTENDED = {0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28}  # ins del home end pgup pgdn arrows
KEYEVENTF_EXTENDEDKEY = 0x0001


def _key(vk: int, up: bool) -> INPUT:
    flags = (KEYEVENTF_KEYUP if up else 0) | (KEYEVENTF_EXTENDEDKEY if vk in EXTENDED else 0)
    scan = user32.MapVirtualKeyW(vk, 0) if user32 else 0
    return INPUT(type=1, u=_INPUTUNION(ki=KEYBDINPUT(wVk=vk, wScan=scan, dwFlags=flags)))


def _mouse(flags: int) -> INPUT:
    return INPUT(type=0, u=_INPUTUNION(mi=MOUSEINPUT(dwFlags=flags)))


def press(chord: str) -> None:
    """'ctrl+t', 'enter', '2' — modifiers first, then the key, released in reverse."""
    names = [p.strip().lower() for p in chord.split("+")]
    codes = [VK[n.upper()] if len(n) == 1 else VK[n] for n in names]
    _send(*[_key(c, False) for c in codes])
    _send(*[_key(c, True) for c in reversed(codes)])


class Interrupted(Exception):
    """Somebody moved the mouse: the run is no longer a measurement, and it stops."""


class Pointer:
    def __init__(self) -> None:
        self.expected: tuple[int, int] | None = None

    def where(self) -> tuple[int, int]:
        pt = wt.POINT()
        user32.GetCursorPos(ctypes.byref(pt))
        return pt.x, pt.y

    def guard(self) -> None:
        if self.expected is not None:
            x, y = self.where()
            # 15 px: a move still landing, or normalised rounding, is a few pixels off;
            # a hand reaching for the mouse is far more than that.
            if abs(x - self.expected[0]) > 15 or abs(y - self.expected[1]) > 15:
                raise Interrupted(f"the pointer moved to {x},{y} (expected {self.expected})")

    def move(self, x: float, y: float, guarded: bool = True) -> None:
        """A real mouse-move event, not a teleport.

        SetCursorPos only relocates the cursor: the first smoke run of transform-undo
        held the button and 'dragged' with it, and the app saw one publish and no drag.
        SendInput with absolute coordinates over the virtual desktop is what a mouse does.
        """
        if guarded:
            self.guard()
        xi, yi = int(round(x)), int(round(y))
        left, top = user32.GetSystemMetrics(76), user32.GetSystemMetrics(77)
        width, height = user32.GetSystemMetrics(78), user32.GetSystemMetrics(79)
        nx = int(round((xi - left) * 65535 / max(1, width - 1)))
        ny = int(round((yi - top) * 65535 / max(1, height - 1)))
        move = INPUT(type=0, u=_INPUTUNION(mi=MOUSEINPUT(
            dx=nx, dy=ny, dwFlags=MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK)))
        _send(move)
        # The target, not a read-back: SendInput is applied asynchronously, and reading
        # the cursor straight after would record where it was, not where it is going.
        self.expected = (xi, yi)

    def click(self, x: float, y: float, button: str = "left", mods: str = "") -> None:
        """A click, with modifiers held around it the way a hand holds them."""
        self.move(x, y)
        held = [VK[m] for m in mods.lower().split("+") if m]
        _send(*[_key(c, False) for c in held]) if held else None
        time.sleep(0.03)
        down, up = ((MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP) if button == "right"
                    else (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP))
        _send(_mouse(down))
        _send(_mouse(up))
        time.sleep(0.03)
        _send(*[_key(c, True) for c in reversed(held)]) if held else None

    def drag(self, start: tuple[float, float], end: tuple[float, float], ms: float, hz: float) -> None:
        """Press, move at `hz` events a second for `ms`, release — a hand's drag, paced."""
        self.move(*start)
        time.sleep(0.05)
        _send(_mouse(MOUSEEVENTF_LEFTDOWN))
        steps = max(2, int(ms / 1000 * hz))
        t0 = time.perf_counter()
        for i in range(1, steps + 1):
            f = i / steps
            # Unguarded inside the drag: a hung app can hold the cursor back, and that
            # is the stall being measured, not somebody's hand.
            self.move(start[0] + (end[0] - start[0]) * f, start[1] + (end[1] - start[1]) * f, guarded=False)
            # Paced against the clock, not slept per step, so a slow app does not stretch the drag.
            while time.perf_counter() - t0 < i / hz:
                time.sleep(0.0005)
        _send(_mouse(MOUSEEVENTF_LEFTUP))
        # Where the cursor ended is the app's business if it was busy; the guard
        # starts again from the next deliberate move.
        self.expected = None


def window_of(pid: int) -> int | None:
    found: list[int] = []
    proc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    def visit(hwnd, _):
        owner = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value == pid and user32.IsWindowVisible(hwnd) and user32.GetWindowTextLengthW(hwnd) > 0:
            found.append(hwnd)
        return True

    user32.EnumWindows(proc(visit), 0)
    return found[0] if found else None


def answering(hwnd: int, within_ms: int = 100) -> bool:
    """Whether the app's UI thread takes a message within `within_ms`: a no-op WM_NULL
    sent with a timeout returns only when the thread pumps its queue."""
    result = ctypes.c_size_t()
    SMTO_ABORTIFHUNG = 0x0002
    return bool(user32.SendMessageTimeoutW(hwnd, 0x0000, 0, 0, SMTO_ABORTIFHUNG, within_ms,
                                           ctypes.byref(result)))


def bring_forward(hwnd: int, maximise: bool = False) -> None:
    # A tap of Alt first: Windows refuses SetForegroundWindow to a process that has
    # not had input, and the Alt counts.
    # Left at the size it opened at unless asked: a fresh profile always opens at
    # the same default, and resizing moves the canvas after the app reported it.
    # Behaviour checks ask for it, because their targets are found by name and a
    # small window can leave docker rows out of sight.
    press("alt")
    if maximise:
        user32.ShowWindow(hwnd, 3)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)


# ---- the app's log -------------------------------------------------------------------

@dataclass
class Log:
    path: Path
    lines: list[dict] = field(default_factory=list)
    _offset: int = 0

    def poll(self) -> list[dict]:
        if not self.path.exists():
            return []
        # Bytes, not text: offsets must count what is on disk, and the app writes
        # CRLF, which text mode would quietly shorten.
        with self.path.open("rb") as f:
            f.seek(self._offset)
            chunk = f.read()
        # Only whole lines: the app flushes every half second, possibly mid-line.
        whole = chunk[: chunk.rfind(b"\n") + 1]
        self._offset += len(whole)
        new = [json.loads(line) for line in whole.decode("utf-8").splitlines() if line.strip()]
        self.lines.extend(new)
        return new

    def wait_for(self, event: str, timeout: float) -> dict:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            for line in self.poll():
                if line["ev"] == event:
                    return line
            time.sleep(0.2)
        raise TimeoutError(f"the app never logged {event!r} within {timeout:.0f} s")

    def settle(self, quiet: float, timeout: float, hwnd: int | None = None) -> None:
        """Wait until the app is answering and has logged nothing for `quiet` seconds.

        Quiet alone is not finished: a hung UI thread logs nothing either, and the first
        transform-undo runs walked straight on into an app frozen by the undo. The window
        is asked to answer as well, through the message queue a hang blocks."""
        deadline = time.monotonic() + timeout
        last = time.monotonic()
        while time.monotonic() < deadline:
            if self.poll():
                last = time.monotonic()
            elif time.monotonic() - last >= quiet and (hwnd is None or answering(hwnd)):
                return
            time.sleep(0.1)


@dataclass
class Placement:
    """Document point -> screen pixel, from three corners the app reported."""
    w: float
    h: float
    origin: tuple[float, float]
    right: tuple[float, float]
    down: tuple[float, float]

    def at(self, fx: float, fy: float) -> tuple[float, float]:
        """`fx`, `fy` are fractions of the document: 0.5, 0.5 is its centre."""
        ox, oy = self.origin
        return (ox + (self.right[0] - ox) * fx + (self.down[0] - ox) * fy,
                oy + (self.right[1] - oy) * fx + (self.down[1] - oy) * fy)


# ---- asking the app ------------------------------------------------------------------

class Pipe:
    """The lab instance's own pipe (lightbox-ipc-<pid>): one JSON request per line.

    Only a lab instance serves it — one started with a throwaway profile and the perf
    log on — so this can never reach the artist's open Lightbox."""

    def __init__(self, pid: int) -> None:
        self.path = rf"\\.\pipe\lightbox-ipc-{pid}"
        self.f = None

    def ask(self, op: str, payload: dict | None = None) -> dict:
        if self.f is None:
            deadline = time.monotonic() + 20
            while True:
                try:
                    self.f = open(self.path, "r+b", buffering=0)
                    break
                except OSError:
                    if time.monotonic() > deadline:
                        raise RuntimeError(
                            f"the app's lab pipe {self.path} never opened — most likely a build "
                            "from before behaviour checks, which serves only the shared pipe; "
                            "behaviour checks need a build that includes them")
                    time.sleep(0.2)
        line = json.dumps({"op": op, "payload": payload}) + "\n"
        self.f.write(line.encode("utf-8"))
        reply = b""
        while not reply.endswith(b"\n"):
            chunk = self.f.read(1)
            if not chunk:
                raise RuntimeError("the app closed its lab pipe")
            reply += chunk
        answer = json.loads(reply)
        if not answer.get("ok"):
            raise RuntimeError(f"{op}: {answer.get('error')}")
        return answer.get("payload") or {}

    def close(self) -> None:
        if self.f is not None:
            self.f.close()


def locate(pipe: Pipe, target: dict) -> tuple[float, float]:
    """Screen centre of a named thing: an X-sheet cel, a layer row, a folder, a menu item."""
    if "xsheet" in target:
        layer, frame = target["xsheet"]
        q = {"kind": "xsheet-cel", "layer": layer, "frame": frame}
    elif "layer" in target:
        q = {"kind": "layer-row", "layer": target["layer"]}
    elif "folder" in target:
        q = {"kind": "folder-row", "folder": target["folder"]}
    elif "menu" in target:
        q = {"kind": "menu-item", "text": target["menu"]}
    else:
        raise ValueError(f"unknown target {target}")
    r = pipe.ask("lab_locate", q)
    return r["x"] + r["w"] / 2, r["y"] + r["h"] / 2


def check_expect(step: dict, state: dict) -> tuple[bool, str]:
    """One expectation against the app's own answer. Returns (passed, what was seen)."""
    layers = {l["name"]: l for l in state["layers"]}
    if "frame_count" in step:
        return state["frameCount"] == step["frame_count"], f"frameCount {state['frameCount']}"
    if "row" in step:
        name, want = step["row"]
        got = layers[name]["row"] if name in layers else None
        return got == want, f"{name}: {got}"
    if "selected" in step:
        got = sorted(l["name"] for l in state["layers"] if l["selected"])
        return got == sorted(step["selected"]), f"selected {got}"
    if "folder_selected" in step:
        got = [f["name"] for f in state["folders"] if f["selected"]]
        return step["folder_selected"] in got, f"folders selected {got}"
    if "status_contains" in step:
        got = state.get("status") or ""
        return step["status_contains"] in got, f"status {got!r}"
    raise ValueError(f"unknown expectation {step}")


# ---- one run -------------------------------------------------------------------------

def load_scenario(name: str) -> dict:
    path = Path(name) if name.endswith(".json") else LAB / "scenarios" / f"{name}.json"
    if not path.exists():
        sys.exit(f"no scenario {name!r} (looked for {path})")
    scenario = json.loads(path.read_text(encoding="utf-8"))
    scenario.setdefault("name", path.stem)
    return scenario


def resolve_exe(build: str | None) -> Path:
    exe = Path(build) if build else DEFAULT_EXE
    if exe.is_dir():
        exe = exe / "Lightbox.App.exe"
    if not exe.exists():
        sys.exit(f"no Lightbox.App.exe at {exe} — build Release first, or pass --build")
    return exe.resolve()


def run_once(scenario: dict, exe: Path, out: Path, presentmon: str | None) -> dict:
    out.mkdir(parents=True, exist_ok=True)
    log = Log(out / "perf.jsonl")
    env = {k: v for k, v in os.environ.items() if not stripped(k)}
    env["LIGHTBOX_LAB"] = "1"  # the explicit opt-in: only the lab makes a lab instance
    env["LIGHTBOX_PROFILE_DIR"] = str((out / "profile").resolve())
    env["LIGHTBOX_PERF_LOG"] = str(log.path.resolve())
    fixture = ensure_fixture(scenario.get("fixture", "owner-shape"))

    proc = subprocess.Popen([str(exe), str(fixture)], env=env, cwd=str(out),
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    pm = None
    if presentmon:
        pm = subprocess.Popen([presentmon, "--stop_existing_session", "--process_id", str(proc.pid),
                               "--output_file", str(out / "presentmon.csv"), "--timed", "600",
                               "--terminate_after_timed"],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    pointer = Pointer()
    pipe = Pipe(proc.pid)
    checks: list[dict] = []
    outcome = "ok"
    try:
        ready = log.wait_for("ready", timeout=float(scenario.get("open_timeout_s", 120)))
        place = Placement(**json.loads(ready["d"]))
        hwnd = window_of(proc.pid)
        if hwnd is None:
            raise RuntimeError("the app's window was not found")
        bring_forward(hwnd, maximise=scenario.get("window") == "maximised")
        # Panels the scenario needs on screen, whatever the fresh workspace shows.
        for panel in scenario.get("panels", []):
            pipe.ask("lab_show_panel", {"panel": panel})
        log.settle(quiet=1.0, timeout=20, hwnd=hwnd)
        for name, step in iterate_steps(scenario):
            # Nothing is sent to an app that is not answering: queued input would
            # replay into whatever state it wakes up in.
            log.settle(quiet=0.0, timeout=120, hwnd=hwnd)
            pointer.guard()
            if step["do"] == "expect":
                state = pipe.ask("lab_state")
                passed, seen = check_expect(step, state)
                if not passed:
                    # The whole sheet and selection, so a miss says where the edit went.
                    seen += " | " + "; ".join(
                        f"{l['name']}{'*' if l['selected'] else ''}: {l['row']}" for l in state["layers"]
                    ) + f" | active {state['active']}, frame {state['currentFrame']}, status {state['status']!r}"
                checks.append({"expect": {k: v for k, v in step.items() if k != "do"},
                               "passed": passed, "seen": seen})
                continue
            if step["do"] == "click" and "target" in step:
                step = {**step, "_screen": locate(pipe, step["target"])}
            elif step["do"] == "hover" and "target" in step:
                step = {**step, "_screen": locate(pipe, step["target"])}
            do_step(step, place, pointer, log, hwnd)
        log.settle(quiet=1.5, timeout=120, hwnd=hwnd)
    except Interrupted as stop:
        outcome = f"interrupted: {stop}"
    except (TimeoutError, RuntimeError) as failed:
        outcome = f"failed: {failed}"
    finally:
        pipe.close()
        proc.kill()
        proc.wait(timeout=10)
        if pm is not None:
            pm.terminate()
        time.sleep(0.6)
        log.poll()
    metrics = summarise(log.lines)
    result = {"scenario": scenario["name"], "exe": str(exe), "build": build_of(log.lines),
              "outcome": outcome, "metrics": metrics, "checks": checks}
    (out / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def iterate_steps(scenario: dict):
    for i in range(int(scenario.get("repeat", 1))):
        for step in scenario["steps"]:
            yield f"{i}", step


def do_step(step: dict, place: Placement, pointer: Pointer, log: Log, hwnd: int) -> None:
    kind = step["do"]
    if kind == "key":
        pointer.guard()
        press(step["keys"])
    elif kind == "click":
        where = step["_screen"] if "_screen" in step else place.at(*step["at"])
        pointer.click(*where, button=step.get("button", "left"), mods=step.get("mods", ""))
    elif kind == "drag":
        a = place.at(*step["from"])
        b = place.at(step["from"][0] + step["by"][0], step["from"][1] + step["by"][1])
        pointer.drag(a, b, ms=float(step.get("ms", 1000)), hz=float(step.get("hz", 120)))
    elif kind == "hover":
        pointer.move(*(step["_screen"] if "_screen" in step else place.at(*step["at"])))
    elif kind == "wait":
        time.sleep(step["ms"] / 1000)
    elif kind == "settle":
        log.settle(quiet=float(step.get("quiet_s", 1.0)), timeout=float(step.get("timeout_s", 120)), hwnd=hwnd)
    else:
        raise ValueError(f"unknown step {kind!r}")
    time.sleep(float(step.get("after_ms", 50)) / 1000)


def build_of(lines: list[dict]) -> str | None:
    return next((l.get("d") for l in lines if l["ev"] == "start"), None)


# ---- what a run says -----------------------------------------------------------------

def summarise(lines: list[dict]) -> dict:
    """Per action: count, total, median, worst. Stalls: count, total, longest, and which
    actions overlapped the stalls — the answer to 'what was it doing when it stopped'."""
    ready_t = next((l["t"] for l in lines if l["ev"] == "ready"), 0.0)
    after = [l for l in lines if l["t"] >= ready_t]  # opening is its own scenario
    by: dict[str, list[float]] = {}
    for l in after:
        if l["ev"] in ("stall", "hang", "start", "ready", "document.opened", "playhead", "play.start"):
            continue
        by.setdefault(l["ev"], []).append(l["ms"])
    actions = {k: {"n": len(v), "total_ms": round(sum(v), 1), "median_ms": round(statistics.median(v), 2),
                   "worst_ms": round(max(v), 1)} for k, v in sorted(by.items())}

    stalls = [l for l in after if l["ev"] == "stall"]
    blame: dict[str, float] = {}
    for s in stalls:
        s0, s1 = s["t"], s["t"] + s["ms"]
        for l in after:
            if l["ev"] in ("stall", "hang") or l["ms"] <= 0:
                continue
            overlap = min(s1, l["t"] + l["ms"]) - max(s0, l["t"])
            if overlap > 0:
                blame[l["ev"]] = blame.get(l["ev"], 0.0) + overlap
    opening = next((l for l in lines if l["ev"] == "document.opened"), None)
    first_stall_before_ready = [l for l in lines if l["ev"] == "stall" and l["t"] < ready_t]
    return {
        "actions": actions,
        "stalls": {
            "n": len(stalls),
            "total_ms": round(sum(s["ms"] for s in stalls), 1),
            "longest_ms": round(max((s["ms"] for s in stalls), default=0.0), 1),
            "over_250ms": sum(1 for s in stalls if s["ms"] > 250),
            "blamed_ms": {k: round(v, 1) for k, v in sorted(blame.items(), key=lambda kv: -kv[1])},
        },
        "open": {
            "ready_ms": round(ready_t, 1),
            "opened_ms": round(opening["t"], 1) if opening else None,
            "longest_stall_ms": round(max((s["ms"] for s in first_stall_before_ready), default=0.0), 1),
        },
    }


def flat(metrics: dict) -> dict[str, float]:
    """The numbers a verdict compares, by name. 'worst' and 'longest' are judged on the
    minimum across runs (contention only ever adds to them); the rest on the median."""
    out = {f"open.ready_ms": metrics["open"]["ready_ms"],
           f"open.longest_stall_ms": metrics["open"]["longest_stall_ms"],
           "stalls.total_ms": metrics["stalls"]["total_ms"],
           "stalls.longest_ms": metrics["stalls"]["longest_ms"]}
    for name, a in metrics["actions"].items():
        out[f"{name}.median_ms"] = a["median_ms"]
        out[f"{name}.worst_ms"] = a["worst_ms"]
    return out


def aggregate(results: list[dict]) -> dict[str, float]:
    good = [r for r in results if r["outcome"] == "ok"]
    series: dict[str, list[float]] = {}
    for r in good:
        for k, v in flat(r["metrics"]).items():
            series.setdefault(k, []).append(v)
    return {k: (min(v) if ("worst" in k or "longest" in k) else statistics.median(v))
            for k, v in series.items()}


def verdict(a: dict[str, float], b: dict[str, float], tolerance: float) -> list[tuple[str, float, float, str]]:
    rows = []
    for k in sorted(set(a) | set(b)):
        va, vb = a.get(k), b.get(k)
        if va is None or vb is None:
            rows.append((k, va or 0.0, vb or 0.0, "only one side"))
            continue
        # Under a few ms the timer and the scheduler are the measurement.
        if max(va, vb) < 5:
            call = "same"
        elif vb > va * (1 + tolerance):
            call = "REGRESSION"
        elif vb < va * (1 - tolerance):
            call = "better"
        else:
            call = "same"
        rows.append((k, va, vb, call))
    return rows


# ---- commands ------------------------------------------------------------------------

def stamp() -> str:
    return time.strftime("%Y%m%d-%H%M%S")


def cmd_fixture(args) -> None:
    for preset in args.preset or list(PRESETS):
        print(ensure_fixture(preset, rebuild=True))


def cmd_run(args) -> None:
    make_dpi_aware()
    scenario = load_scenario(args.scenario)
    exe = resolve_exe(args.build)
    folder = RUNS / f"{stamp()}-{scenario['name']}-{args.tag}"
    print(f"{scenario['name']}: {args.runs} run(s) of {exe}\n  results in {folder}\n"
          "  hands off the mouse and keyboard until it says done.")
    results = []
    for i in range(args.runs):
        r = run_once(scenario, exe, folder / f"run-{i + 1}", args.presentmon)
        print(f"  run {i + 1}: {r['outcome']}")
        results.append(r)
    summary = {"scenario": scenario["name"], "exe": str(exe), "runs": results, "aggregate": aggregate(results)}
    (folder / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print_report(summary)
    print(f"done — {folder / 'summary.json'}")


def cmd_check(args) -> None:
    """Run a behaviour scenario once and say, expectation by expectation, whether the
    app did what the scenario expects — on any build, the installed one included."""
    make_dpi_aware()
    scenario = load_scenario(args.scenario)
    exe = resolve_exe(args.build)
    folder = RUNS / f"{stamp()}-{scenario['name']}-check"
    print(f"{scenario['name']}: checking {exe}\n  hands off the mouse and keyboard until it says done.")
    r = run_once(scenario, exe, folder / "run-1", None)
    print(f"  run: {r['outcome']}  build {r['build']}")
    failed = 0
    for c in r["checks"]:
        mark = "PASS" if c["passed"] else "FAIL"
        failed += not c["passed"]
        print(f"  {mark}  {json.dumps(c['expect'])}  ->  {c['seen']}")
    if r["outcome"] != "ok":
        print(f"\nINCONCLUSIVE: {r['outcome']}")
        sys.exit(2)
    print(f"\n{'FAIL' if failed else 'PASS'} — {len(r['checks']) - failed}/{len(r['checks'])} expectations held — {folder}")
    sys.exit(1 if failed else 0)


def cmd_ab(args) -> None:
    make_dpi_aware()
    scenario = load_scenario(args.scenario)
    a, b = resolve_exe(args.a), resolve_exe(args.b)
    folder = RUNS / f"{stamp()}-{scenario['name']}-ab"
    print(f"{scenario['name']}: A/B, {args.runs} interleaved pair(s)\n  A {a}\n  B {b}\n"
          f"  results in {folder}\n  hands off the mouse and keyboard until it says done.")
    ra, rb = [], []
    for i in range(args.runs):
        # Interleaved, and the order alternates, so drift over the session lands on both.
        order = [("A", a, ra), ("B", b, rb)] if i % 2 == 0 else [("B", b, rb), ("A", a, ra)]
        for label, exe, sink in order:
            r = run_once(scenario, exe, folder / f"{label}-{i + 1}", args.presentmon)
            print(f"  {label} {i + 1}: {r['outcome']}")
            sink.append(r)
    agg_a, agg_b = aggregate(ra), aggregate(rb)
    rows = verdict(agg_a, agg_b, args.tolerance)
    summary = {"scenario": scenario["name"], "a": str(a), "b": str(b), "aggregate_a": agg_a,
               "aggregate_b": agg_b, "verdict": rows, "runs_a": ra, "runs_b": rb}
    (folder / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"\n{'metric':44} {'A':>10} {'B':>10}  call")
    for k, va, vb, call in rows:
        print(f"{k:44} {va:10.1f} {vb:10.1f}  {call}")
    ok_a = sum(r["outcome"] == "ok" for r in ra)
    ok_b = sum(r["outcome"] == "ok" for r in rb)
    if min(ok_a, ok_b) < 2:
        print(f"\nINCONCLUSIVE: only {ok_a} good A run(s) and {ok_b} good B run(s)")
        sys.exit(2)
    regressed = [k for k, *_rest, call in rows if call == "REGRESSION"]
    print(f"\n{'REGRESSION in ' + ', '.join(regressed) if regressed else 'PASS'} — {folder / 'summary.json'}")
    sys.exit(1 if regressed else 0)


def print_report(summary: dict) -> None:
    good = [r for r in summary["runs"] if r["outcome"] == "ok"]
    print(f"\n{summary['scenario']}: {len(good)}/{len(summary['runs'])} good run(s)")
    for k, v in summary["aggregate"].items():
        print(f"  {k:44} {v:10.1f}")
    if good:
        blamed = good[-1]["metrics"]["stalls"]["blamed_ms"]
        if blamed:
            print("  stalls overlapped, last run:", ", ".join(f"{k} {v:.0f} ms" for k, v in blamed.items()))


def cmd_report(args) -> None:
    print_report(json.loads(Path(args.summary).read_text(encoding="utf-8")))


def main() -> None:
    if os.name != "nt":
        sys.exit("the lab drives the app through Windows input; run it on Windows")
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    f = sub.add_parser("fixture", help="generate the fixture documents")
    f.add_argument("preset", nargs="*")
    f.set_defaults(go=cmd_fixture)
    r = sub.add_parser("run", help="run a scenario against one build")
    r.add_argument("scenario")
    r.add_argument("--build", help="Lightbox.App.exe, or the folder holding it (default: this repo's Release build)")
    r.add_argument("--runs", type=int, default=3)
    r.add_argument("--tag", default="adhoc")
    r.add_argument("--presentmon", help="path to presentmon.exe, to record what reached the screen")
    r.set_defaults(go=cmd_run)
    ab = sub.add_parser("ab", help="run a scenario against two builds, interleaved, with a verdict")
    ab.add_argument("scenario")
    ab.add_argument("--a", required=True, help="the baseline build")
    ab.add_argument("--b", required=True, help="the build under test")
    ab.add_argument("--runs", type=int, default=3)
    ab.add_argument("--tolerance", type=float, default=0.15, help="relative change that counts (default 0.15)")
    ab.add_argument("--presentmon")
    ab.set_defaults(go=cmd_ab)
    c = sub.add_parser("check", help="run a behaviour scenario once and check its expectations")
    c.add_argument("scenario")
    c.add_argument("--build", help="Lightbox.App.exe, or the folder holding it (default: this repo's Release build)")
    c.set_defaults(go=cmd_check)
    rep = sub.add_parser("report", help="print a run's summary")
    rep.add_argument("summary")
    rep.set_defaults(go=cmd_report)
    args = p.parse_args()
    args.go(args)


if __name__ == "__main__":
    main()
