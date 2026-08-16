#!/usr/bin/env python3
"""The line ceilings for the files that are already too big, one file each.

`.claude/quality/ratchets/<name>.md` holds a budget and the reasons it has
moved. `MonolithRatchetTests` reads them; nothing else writes them.

**Why they are not a table in the test any more.** Every branch that touched a
budgeted file had to edit the number *and* add a paragraph explaining it — in
one 267-line C# file shared by all four budgets — so two branches growing two
*different* files still met there. One file each means they no longer do (Q92).

**Why the number is still authored, when it looks derived.** Three of the four
ceilings equalled their file's exact line count when this was written, which
makes a `sync` that re-measures them look obvious. It would destroy the
mechanism: a ceiling re-measured from the tree can never be exceeded, so the
test could never fail and the ratchet would be paperwork. The number works
*because* raising it is a conscious edit that shows up as a line in a diff —
the same design as `LIGHTBOX_PUSH_TO_MAIN`. So nothing here syncs.

`remeasure` therefore exists for exactly one moment: resolving a merge. The
doctrine the old comments spelled out by hand is that a merged budget is
measured on the **merged tree**, never taken from a side — because each side
measured a file the other side had also changed, and taking either banks the
other's extraction as headroom nobody earned. That is mechanical, it was being
done by eye, and it is what this automates. It is deliberately not wired to any
hook.

Commands
    check       report every budget against its file; exit 1 if one is over
    list        the budgets, for a test or a person
    remeasure   set every budget to the merged tree's actual count — merges only
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIR = ROOT / ".claude" / "quality" / "ratchets"

TARGET = re.compile(r"^#\s+(?P<path>\S+)\s*$")
# `[ \t]*` rather than `\s*` on the tail: `\s` matches newlines, so the trailing
# `\s*$` ate the blank line after the budget and `remeasure` ran the number into
# the next heading (B210). The line ends at the line end.
BUDGET = re.compile(r"^budget:[ \t]*(?P<lines>\d+)[ \t]*$", re.MULTILINE)

# A budget this far above the file it guards has stopped guarding it: the slack
# is room to regrow, which is what a ratchet exists to deny.
MAX_SLACK = 250


@dataclass
class Ratchet:
    source: Path        # the .md that holds the budget
    target: str         # the repo-relative file it guards
    budget: int

    @property
    def actual(self) -> int:
        path = ROOT / self.target
        return len(path.read_text(encoding="utf-8").splitlines()) if path.exists() else -1


def ratchets() -> list[Ratchet]:
    found: list[Ratchet] = []
    for path in sorted(DIR.glob("*.md")):
        if path.name == "README.md":
            continue
        text = path.read_text(encoding="utf-8")
        first = next((l for l in text.splitlines() if l.strip()), "")
        target, budget = TARGET.match(first), BUDGET.search(text)
        if target is None or budget is None:
            print(f"  MALFORMED {path.name} — needs `# <path>` then `budget: <n>`", file=sys.stderr)
            continue
        found.append(Ratchet(path, target["path"], int(budget["lines"])))
    return found


def cmd_list() -> int:
    for r in ratchets():
        print(f"{r.target}\t{r.budget}")
    return 0


def cmd_check() -> int:
    problems = 0
    for r in ratchets():
        if r.actual < 0:
            problems += 1
            print(f"  MISSING   {r.target} — moved or renamed; {r.source.name} guards nothing")
            continue
        over, slack = r.actual - r.budget, r.budget - r.actual
        print(f"  {r.target}: {r.actual} of {r.budget} ({over:+d})")
        if over > 0:
            problems += 1
            print(f"            OVER by {over} — put the new code in a partial or a collaborator, "
                  f"or raise the number in {r.source.name} and say why")
        elif slack > MAX_SLACK:
            problems += 1
            print(f"            {slack} lines of slack — an extraction landed and left the budget "
                  "behind, which turns it into room to regrow. Lower it to the file's length")
    if problems:
        print(f"\n{problems} problem(s)")
        return 1
    return 0


def cmd_remeasure(argv: list[str]) -> int:
    """Set every budget to what the tree actually is. For resolving a merge only.

    Two branches that both grew a budgeted file each measured a tree the other
    had also changed, so **neither side's number is right on the merged tree** —
    taking either one banks the other's extraction as headroom nobody earned.
    The merged count is the answer, and it is the same answer whichever side's
    file survived the conflict.

    Only ever lowers or matches on its own: it will not raise a budget past a
    file that is genuinely over, because that is the case where somebody has to
    decide rather than measure. `--allow-raise` says the growth is meant, which
    is the same kind of typed decision as `LIGHTBOX_PUSH_TO_MAIN`.
    """
    allow_raise = "--allow-raise" in argv
    moved = 0
    for r in ratchets():
        if r.actual < 0 or r.actual == r.budget:
            continue
        if r.actual > r.budget and not allow_raise:
            print(f"  {r.target}: {r.actual} lines against {r.budget} — over, not merely stale.")
            print("            That is a decision, not a measurement. Extract, or re-run with "
                  "--allow-raise and say why in the commit message.")
            continue
        text = r.source.read_text(encoding="utf-8")
        r.source.write_text(BUDGET.sub(f"budget: {r.actual}", text, count=1), encoding="utf-8")
        print(f"  {r.target}: {r.budget} -> {r.actual}")
        moved += 1
    if moved:
        print(f"\n{moved} budget(s) re-measured on this tree. Say in the commit message that they "
              "were measured on the merged tree — the numbers on either side were both wrong.")
    else:
        print("  nothing to re-measure — every budget already matches its file")
    return 0


def main() -> None:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    if cmd == "check":
        sys.exit(cmd_check())
    elif cmd == "list":
        sys.exit(cmd_list())
    elif cmd == "remeasure":
        sys.exit(cmd_remeasure(sys.argv[2:]))
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
