#!/usr/bin/env python3
"""Keep the bug ledger honest by deriving its status from the code.

`.claude/quality/BUGS.md` records what is wrong. It is not a place to *claim*
a fix — a claim rots, and a bug marked fixed with nothing guarding it is worse
than an open bug, because it stops anyone looking. Every entry therefore names
the regression test that must exist for it to be closed, and this script
resolves that against the generated code index and rewrites the checkbox.

    [x]  the regression test exists   — fixed
    [ ]  it does not                  — open

Because status is derived, deleting the test reopens the bug on the next sync.
That is the property that makes the ledger worth keeping.

Commands
    check           report; exit 1 if a mark disagrees with the code
    sync            rewrite the checkboxes in place
    next            highest-priority open bugs, for a loop to pick from
    mine <domain>   open bugs in one domain — what a working agent greps
    stats           counts per priority and per domain
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

from evidence import Index

ROOT = Path(__file__).resolve().parent.parent
BUGS = ROOT / ".claude" / "quality" / "BUGS.md"

# - [ ] **B3** `P1` `timeline` Title text `evidence: SomeTest, OtherTest`
ENTRY = re.compile(
    r"^- \[(?P<mark>[ x])\] \*\*(?P<id>B\d+)\*\* "
    r"`(?P<priority>P[1-4])` `(?P<domain>[a-z]+)` (?P<title>.*?)"
    r"(?:\s*`evidence:\s*(?P<evidence>[^`]*)`)?\s*$"
)

DOMAINS = {
    "brush", "timeline", "layers", "canvas", "transform",
    "colour", "export", "project", "ui", "ai",
}


@dataclass
class Bug:
    line_no: int
    mark: str
    id: str
    priority: str
    domain: str
    title: str
    evidence: list[str]
    resolved: list[str] = field(default_factory=list)
    missing: list[str] = field(default_factory=list)

    @property
    def status(self) -> str:
        """A bug is fixed only when EVERY anchor resolves.

        Stricter than the roadmap's partial `[~]` on purpose: a half-guarded
        fix is an open bug that has stopped looking like one.
        """
        if not self.evidence:
            return " "
        return "x" if not self.missing else " "

    @property
    def unverifiable(self) -> bool:
        return not self.evidence

    def render(self) -> str:
        tail = f" `evidence: {', '.join(self.evidence)}`" if self.evidence else ""
        return f"- [{self.status}] **{self.id}** `{self.priority}` `{self.domain}` {self.title}{tail}"


def parse() -> tuple[list[str], list[Bug]]:
    if not BUGS.exists():
        sys.exit(f"No bug ledger at {BUGS}")
    lines = BUGS.read_text(encoding="utf-8").splitlines()
    bugs: list[Bug] = []
    for n, line in enumerate(lines):
        if (m := ENTRY.match(line)) is None:
            continue
        raw = (m.group("evidence") or "").strip()
        anchors = [a.strip() for a in raw.split(",") if a.strip()]
        bugs.append(Bug(n, m.group("mark"), m.group("id"), m.group("priority"),
                        m.group("domain"), m.group("title").strip(), anchors))
    return lines, bugs


def resolve(bugs: list[Bug]) -> None:
    index = Index()
    for bug in bugs:
        for anchor in bug.evidence:
            (bug.resolved if index.has(anchor) else bug.missing).append(anchor)


def bad_domains(bugs: list[Bug]) -> list[Bug]:
    return [b for b in bugs if b.domain not in DOMAINS]


def cmd_sync() -> None:
    lines, bugs = parse()
    resolve(bugs)
    changed = [b for b in bugs if b.status != b.mark]
    for bug in changed:
        lines[bug.line_no] = bug.render()
    if changed:
        BUGS.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"Updated {len(changed)} of {len(bugs)} bugs:")
        for bug in changed:
            verb = "CLOSED " if bug.status == "x" else "REOPENED"
            print(f"  {verb} {bug.id}  {bug.title}")
            if bug.missing:
                print(f"           missing: {', '.join(bug.missing)}")
    else:
        print(f"Ledger already current — {len(bugs)} bugs.")


def cmd_check() -> int:
    _, bugs = parse()
    resolve(bugs)
    drifted = [b for b in bugs if b.status != b.mark]
    unverifiable = [b for b in bugs if b.unverifiable]
    wrong_domain = bad_domains(bugs)

    open_bugs = [b for b in bugs if b.status != "x"]
    counts = {p: sum(1 for b in open_bugs if b.priority == p) for p in ("P1", "P2", "P3", "P4")}
    print(f"{len(bugs)} bugs — {len(open_bugs)} open "
          f"(P1 {counts['P1']}, P2 {counts['P2']}, P3 {counts['P3']}, P4 {counts['P4']})")

    for bug in unverifiable:
        print(f"  UNVERIFIABLE {bug.id}  {bug.title}")
        print("               no evidence: — name the regression test that closes it")
    for bug in wrong_domain:
        print(f"  BAD DOMAIN   {bug.id}  '{bug.domain}' is not one of {sorted(DOMAINS)}")
    for bug in drifted:
        want = "fixed" if bug.status == "x" else "open"
        print(f"  DRIFTED      {bug.id}  marked '{bug.mark}' but the code says {want}")
        if bug.missing:
            print(f"               missing: {', '.join(bug.missing)}")

    if drifted or unverifiable or wrong_domain:
        print("\nRun: python3 scripts/bugs.py sync")
        return 1
    return 0


def _rank(bug: Bug) -> tuple[int, str]:
    return (int(bug.priority[1]), bug.id)


def cmd_next(limit: int = 10) -> None:
    _, bugs = parse()
    resolve(bugs)
    open_bugs = sorted((b for b in bugs if b.status != "x"), key=_rank)
    if not open_bugs:
        print("No open bugs.")
        return
    for bug in open_bugs[:limit]:
        print(f"  {bug.priority}  [{bug.domain}] {bug.id}  {bug.title}")


def cmd_mine(domain: str) -> None:
    """Open bugs in one domain — what an agent about to edit that area reads.

    The loop's rule is that P1 and P2 here get fixed alongside whatever the
    agent came for; P3 and P4 are reported and left alone, so a request to
    change one thing does not come back as a diff touching six.
    """
    if domain not in DOMAINS:
        sys.exit(f"Unknown domain '{domain}'. One of: {', '.join(sorted(DOMAINS))}")
    _, bugs = parse()
    resolve(bugs)
    mine = sorted((b for b in bugs if b.status != "x" and b.domain == domain), key=_rank)
    if not mine:
        print(f"No open bugs in '{domain}'.")
        return
    take = [b for b in mine if b.priority in ("P1", "P2")]
    note = [b for b in mine if b.priority not in ("P1", "P2")]
    if take:
        print(f"FIX THESE TOO ({domain}):")
        for bug in take:
            print(f"  {bug.priority}  {bug.id}  {bug.title}")
    if note:
        print(f"MENTION ONLY ({domain}):")
        for bug in note:
            print(f"  {bug.priority}  {bug.id}  {bug.title}")


def cmd_stats() -> None:
    _, bugs = parse()
    resolve(bugs)
    open_bugs = [b for b in bugs if b.status != "x"]
    print(f"{len(bugs)} recorded, {len(open_bugs)} open, {len(bugs) - len(open_bugs)} closed")
    for p in ("P1", "P2", "P3", "P4"):
        n = sum(1 for b in open_bugs if b.priority == p)
        if n:
            print(f"  {p}  {n}")
    print("open by domain:")
    for d in sorted({b.domain for b in open_bugs}):
        print(f"  {d:<10} {sum(1 for b in open_bugs if b.domain == d)}")


def main() -> None:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    if cmd == "sync":
        cmd_sync()
    elif cmd == "check":
        sys.exit(cmd_check())
    elif cmd == "next":
        cmd_next()
    elif cmd == "mine":
        if len(sys.argv) < 3:
            sys.exit("usage: bugs.py mine <domain>")
        cmd_mine(sys.argv[2])
    elif cmd == "stats":
        cmd_stats()
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
