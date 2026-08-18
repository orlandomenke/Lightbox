#!/usr/bin/env python3
"""Decide when the performance map is worth re-running, and what changed when it was.

The sweep takes minutes and produces a *decision*, so the useful question is
never "has a day passed" — it is "has anything changed that could move a curve,
and is somebody here to act on the answer". A nightly run whose verdict nobody
reads for three days is worse than one that fires when an agent is present to
file the bug.

    python3 scripts/bench.py should-run    # exit 0 if it is worth running
    python3 scripts/bench.py check         # diff the last run against the baseline
    python3 scripts/bench.py accept        # promote the last run to the baseline
    python3 scripts/bench.py status        # what is known, and how stale

The baseline is committed and the raw report is not. That split is deliberate:
a cliff and an exponent are facts about the code and belong in the tree, while
a column of milliseconds is a fact about whichever machine produced it and
would be a merge conflict every time two people ran it.
"""

from __future__ import annotations

import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
QUALITY = ROOT / ".claude" / "quality"
BASELINE = QUALITY / "bench-baseline.json"
CURRENT = QUALITY / "PERFORMANCE.json"

# Touch any of these and a curve could move. Deliberately narrow: the point of
# gating is that a round spent on the palette docker does not trigger a
# fifteen-minute sweep, and a watch-list that matches everything gates nothing.
WATCHED = [
    "src/Lightbox.Raster/",
    "src/Lightbox.Core/Documents/",
    "src/Lightbox.Core/Timeline/",
    "src/Lightbox.App/Rendering/",
    "tools/Lightbox.Bench/",
]

# A floor, so drift somewhere nobody is working still surfaces eventually.
STALE_DAYS = 7

# How much a number may move before it is worth a person's attention. Wide,
# because this runs on whatever machine is to hand and the alternative is an
# alert nobody believes.
EXPONENT_JUMP = 0.35
TIME_FACTOR = 1.5


def sh(*args: str) -> str:
    return subprocess.run(
        args, cwd=ROOT, capture_output=True,
        text=True, encoding="utf-8", errors="replace", check=False
    ).stdout.strip()


def load(path: Path) -> dict | None:
    if not path.exists():
        return None
    try:
        return json.loads(path.read_text())
    except (json.JSONDecodeError, OSError):
        return None


def by_name(doc: dict) -> dict[str, dict]:
    return {s["name"]: s for s in doc.get("scenarios", [])}


def changed_since(sha: str) -> list[str]:
    if not sha:
        return []
    diff = sh("git", "diff", "--name-only", f"{sha}..HEAD")
    if not diff:
        return []
    return [
        f for f in diff.splitlines()
        if any(f.startswith(w) for w in WATCHED)
    ]


def days_since(stamp: str) -> float:
    try:
        then = datetime.fromisoformat(stamp)
    except ValueError:
        return 1e9
    if then.tzinfo is None:
        then = then.replace(tzinfo=timezone.utc)
    return (datetime.now(timezone.utc) - then).total_seconds() / 86400


def cmd_should_run() -> int:
    base = load(BASELINE)
    if base is None:
        print("no baseline — run the sweep and `accept` it")
        return 0

    stale = days_since(base.get("takenAt", ""))
    files = changed_since(base.get("sha", ""))

    if files:
        print(f"{len(files)} watched file(s) changed since the baseline:")
        for f in files[:8]:
            print(f"  {f}")
        if len(files) > 8:
            print(f"  … and {len(files) - 8} more")
        return 0

    if stale >= STALE_DAYS:
        print(f"nothing watched has changed, but the baseline is {stale:.0f} days old")
        return 0

    print(f"nothing to do — baseline is {stale:.1f} days old and no watched file changed")
    return 1


def cmd_check() -> int:
    base, now = load(BASELINE), load(CURRENT)
    if now is None:
        print("no run to check — `dotnet run --project tools/Lightbox.Bench -c Release` first")
        return 2
    if base is None:
        print("no baseline to compare against — `accept` this run to make one")
        return 2

    old, new = by_name(base), by_name(now)
    findings: list[str] = []

    for name, cur in new.items():
        prev = old.get(name)
        if prev is None:
            findings.append(f"NEW      {name} — n^{cur['exponent']}, cliff {cur['cliff']}")
            continue

        # A cliff moving down is the headline: it means a workload that used to
        # be usable is not any more, stated in the artist's own units.
        # Cliffs sit on a geometric ladder of swept values, so a neighbouring
        # rung is the resolution of the measurement rather than a change: the
        # same scenario read 8 layers on one run and 4 on the next with the
        # pressure identical to within 1%. Reporting that every time is how a
        # check stops being believed. A factor of two is a real rung.
        pc, cc = prev.get("cliff"), cur.get("cliff")
        if pc is None and cc is not None:
            findings.append(f"CLIFF    {name} — now misses its budget at {cc} {cur['dimension']}")
        elif pc is not None and cc is None:
            findings.append(f"BETTER   {name} — cliff gone (was {pc} {cur['dimension']})")
        elif pc and cc and cc <= pc / 2:
            findings.append(
                f"CLIFF    {name} — breaks at {cc} {cur['dimension']}, used to hold to {pc}")
        elif pc and cc and cc >= pc * 2:
            findings.append(f"BETTER   {name} — holds to {cc} {cur['dimension']} (was {pc})")

        pe, ce = prev.get("exponent"), cur.get("exponent")
        if pe and ce and ce - pe >= EXPONENT_JUMP:
            findings.append(
                f"SHAPE    {name} — n^{pe} to n^{ce}; it stopped scaling the way it did")

        # Calibrated, so this survives being run on a different machine.
        pt, ct = prev.get("worstP95Calibrated"), cur.get("worstP95Calibrated")
        if pt and ct and ct > pt * TIME_FACTOR:
            findings.append(f"SLOWER   {name} — {ct / pt:.1f}x its calibrated cost")

    # A --filter run holds only what was asked for, so absence means "not run"
    # rather than "gone". Reporting nine GONEs at somebody investigating one
    # curve is how a check gets ignored.
    if not now.get("partial"):
        for name in old:
            if name not in new:
                findings.append(f"GONE     {name} — no longer swept; deleted, or the sweep broke")
    elif len(new) < len(old):
        print(f"(filtered run — {len(new)} of {len(old)} scenarios; absences not reported)\n")

    if not findings:
        print(f"no change worth reporting across {len(new)} scenario(s)")
        return 0

    print(f"{len(findings)} finding(s) against the baseline:\n")
    for f in sorted(findings):
        print(f"  {f}")
    print(
        "\nA cliff that moved the wrong way is a bug: open it in BUGS.md with both\n"
        "measurements as the evidence, then `accept` so the next run compares\n"
        "against reality rather than against a number nobody stands behind."
    )
    return 1


def cmd_accept() -> int:
    now = load(CURRENT)
    if now is None:
        print("nothing to accept — run the sweep first")
        return 2

    now["sha"] = sh("git", "rev-parse", "HEAD")
    now["takenAt"] = datetime.now(timezone.utc).isoformat(timespec="seconds")
    # Raw millisecond columns are dropped on the way in: what is committed has
    # to be a fact about the code, and a time is a fact about a machine.
    BASELINE.write_text(json.dumps(now, indent=2) + "\n")
    print(f"baseline set from {len(now.get('scenarios', []))} scenario(s) at {now['sha'][:8]}")
    return 0


def cmd_status() -> int:
    base = load(BASELINE)
    if base is None:
        print("no baseline yet")
        return 0

    print(f"baseline  {base.get('sha', '?')[:8]}  {base.get('takenAt', '?')}"
          f"  ({days_since(base.get('takenAt', '')):.1f} days old)")
    print(f"reference loop {base.get('calibrationMs', '?')} ms on the machine that took it\n")

    for s in sorted(base.get("scenarios", []), key=lambda s: -(s.get("worstP95Calibrated") or 0)):
        cliff = s.get("cliff")
        mark = "🔴" if cliff is not None else "🟢"
        print(f"  {mark} {s['name']:<40} n^{s.get('exponent')}"
              f"  cliff {cliff if cliff is not None else '—'} {s['dimension']}")

    files = changed_since(base.get("sha", ""))
    print(f"\n{len(files)} watched file(s) changed since." if files
          else "\nNothing watched has changed since.")
    return 0


COMMANDS = {
    "should-run": cmd_should_run,
    "check": cmd_check,
    "accept": cmd_accept,
    "status": cmd_status,
}


def main() -> int:
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        return 2
    return COMMANDS[sys.argv[1]]()


if __name__ == "__main__":
    sys.exit(main())
