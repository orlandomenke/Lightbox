#!/usr/bin/env python3
"""Keep the roadmap honest by deriving its status from the code.

`.claude/quality/ROADMAP.md` is the source of truth for WHAT we intend to
build. It is not the source of truth for what is DONE — that is a claim, and
claims rot. Every item carries `evidence:` naming the types, tests or files
that would exist if it were built, and this script resolves those against the
generated code index and rewrites the checkbox.

    [x]  every anchor resolves          — built
    [~]  some resolve                   — started, or the anchors are stale
    [ ]  none resolve                   — not started
    [?]  no evidence declared           — unverifiable; add anchors or admit it

Because status is derived, deleting a feature un-ticks its box on the next
run. That is the whole point: a roadmap that only ever moves forward is a
wish list.

Commands
    check   report status; exit 1 if the file disagrees with the code
    sync    rewrite the checkboxes in place
    next    the strongest not-started items, for a loop to pick from
    stats   one line per pillar
"""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ROADMAP = ROOT / ".claude" / "quality" / "ROADMAP.md"
MAP = ROOT / ".claude" / "codemap" / "map.json"

ITEM = re.compile(
    r"^(?P<indent>\s*)- \[(?P<mark>[ x~?])\] (?P<title>.*?)"
    r"(?:\s*`evidence:\s*(?P<evidence>[^`]*)`)?\s*$"
)
PILLAR = re.compile(r"^## (?P<title>.+)$")


@dataclass
class Item:
    line_no: int
    indent: str
    mark: str
    title: str
    evidence: list[str]
    pillar: str
    resolved: list[str] = field(default_factory=list)
    missing: list[str] = field(default_factory=list)

    @property
    def status(self) -> str:
        if not self.evidence:
            return "?"
        if not self.missing:
            return "x"
        return "~" if self.resolved else " "

    def render(self) -> str:
        tail = f" `evidence: {', '.join(self.evidence)}`" if self.evidence else ""
        return f"{self.indent}- [{self.status}] {self.title}{tail}"


class Index:
    """Everything the code index knows that an evidence anchor might name."""

    def __init__(self) -> None:
        if not MAP.exists():
            sys.exit("No code index — run: python3 scripts/codemap.py build")
        data = json.loads(MAP.read_text(encoding="utf-8"))
        self.names: set[str] = set()
        self.paths: set[str] = set()
        for path, info in data["files"].items():
            self.paths.add(path)
            self.paths.add(Path(path).name)
            # The stem too: a test file is routinely named after the class it
            # holds, and an anchor naming one should not care which it meant.
            self.paths.add(Path(path).stem)
            for t in info.get("types", []):
                self.names.add(t["name"] if isinstance(t, dict) else str(t))
            for m in info.get("members", []):
                self.names.add(m["name"] if isinstance(m, dict) else str(m))
        for test in data.get("tests", []):
            self.names.add(test["test"])
            self.names.add(test["class"])

    def has(self, anchor: str) -> bool:
        anchor = anchor.strip()
        if not anchor:
            return False
        # `Type.Member` resolves if either half is known; a member alone is
        # ambiguous enough that requiring both would produce false negatives.
        if "." in anchor and "/" not in anchor:
            return any(part in self.names for part in anchor.split("."))
        if "/" in anchor:
            return anchor in self.paths or (ROOT / anchor).exists()
        return anchor in self.names or anchor in self.paths


def parse() -> tuple[list[str], list[Item]]:
    if not ROADMAP.exists():
        sys.exit(f"No roadmap at {ROADMAP}")
    lines = ROADMAP.read_text(encoding="utf-8").splitlines()
    items: list[Item] = []
    pillar = "(none)"
    for n, line in enumerate(lines):
        if (head := PILLAR.match(line)) is not None:
            pillar = head.group("title").strip()
            continue
        if (m := ITEM.match(line)) is None:
            continue
        raw = (m.group("evidence") or "").strip()
        anchors = [a.strip() for a in raw.split(",") if a.strip()]
        items.append(Item(n, m.group("indent"), m.group("mark"),
                          m.group("title").strip(), anchors, pillar))
    return lines, items


def resolve(items: list[Item]) -> None:
    index = Index()
    for item in items:
        for anchor in item.evidence:
            (item.resolved if index.has(anchor) else item.missing).append(anchor)


def cmd_sync() -> None:
    lines, items = parse()
    resolve(items)
    changed = [i for i in items if i.status != i.mark]
    for item in items:
        lines[item.line_no] = item.render()
    ROADMAP.write_text("\n".join(lines) + "\n", encoding="utf-8")
    if not changed:
        print(f"Roadmap already current — {len(items)} items.")
        return
    print(f"Updated {len(changed)} of {len(items)} items:")
    for item in changed:
        note = ""
        if item.status == "~":
            note = f"  (missing: {', '.join(item.missing)})"
        print(f"  [{item.mark}] -> [{item.status}]  {item.title}{note}")


def cmd_check() -> None:
    _, items = parse()
    resolve(items)
    drift = [i for i in items if i.status != i.mark]
    counts = {k: 0 for k in "x~ ?"}
    for item in items:
        counts[item.status] += 1
    print(f"{len(items)} items — {counts['x']} built, {counts['~']} partial, "
          f"{counts[' ']} not started, {counts['?']} unverifiable")
    if not drift:
        return
    print(f"\n{len(drift)} item(s) disagree with the code:")
    for item in drift:
        print(f"  [{item.mark}] should be [{item.status}] — {item.title}"
              + (f"  (missing: {', '.join(item.missing)})" if item.missing else ""))
    print("\nRun: python3 scripts/roadmap.py sync")
    sys.exit(1)


def cmd_next(limit: int = 12) -> None:
    """Not-started items, nearest-first: partials before untouched ones."""
    _, items = parse()
    resolve(items)
    partial = [i for i in items if i.status == "~"]
    fresh = [i for i in items if i.status == " "]
    print("IN FLIGHT (some evidence already exists)")
    for item in partial[:limit]:
        print(f"  {item.pillar} :: {item.title}  (missing: {', '.join(item.missing)})")
    if not partial:
        print("  (none)")
    print("\nNOT STARTED")
    for item in fresh[:limit]:
        print(f"  {item.pillar} :: {item.title}")


def cmd_stats() -> None:
    _, items = parse()
    resolve(items)
    by_pillar: dict[str, list[Item]] = {}
    for item in items:
        by_pillar.setdefault(item.pillar, []).append(item)
    for pillar, group in by_pillar.items():
        done = sum(1 for i in group if i.status == "x")
        part = sum(1 for i in group if i.status == "~")
        pct = 100 * done / len(group)
        print(f"{done:3}/{len(group):3} ({pct:3.0f}%) +{part} partial   {pillar}")


def main() -> None:
    args = sys.argv[1:]
    command = args[0] if args else "check"
    if command == "sync":
        cmd_sync()
    elif command == "check":
        cmd_check()
    elif command == "next":
        cmd_next(int(args[1]) if len(args) > 1 else 12)
    elif command == "stats":
        cmd_stats()
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
