#!/usr/bin/env python3
"""Derive the decomposition map for the files that are too big to read.

`docs/DESIGN-mainviewmodel-decomposition.md` used to carry a hand-written table
of sections, line anchors and field ownership. It went stale before it was
merged: it measured `MainViewModel.cs` at 10,098 lines when the file was already
12,001, so every anchor in it was off by one to two thousand lines and the map
pointed at the wrong code. A map nobody can trust is worse than no map, because
it is followed.

So the table is derived instead of asserted, in the manner of `bugs.py` and
`roadmap.py`. The document keeps the *argument* — which cluster is a hub, which
tool fits which section, what must not move — and this script supplies the
numbers underneath it.

What it measures, per `// ---- section ----` marker:

    owns    private fields DECLARED inside the section
    hub     private fields it USES that are declared somewhere else

That distinction is the whole analysis. A section with many `owns` and few `hub`
is a leaf: its state comes with it and the extraction is local. A section with
few `owns` and many `hub` is either pure behaviour over shared state or a piece
of a larger mechanism, and moving it alone would just relocate the coupling.

Two things it is deliberately good at catching:

  * **Markers that lie.** A section's fields tell you what the code actually
    does. `video clip bars (Q57)` runs 966 lines and holds 23 foreign fields
    named `_pendingDirty`, `_publishSeq`, `_tileFlats`, `_composeRing` — because
    the render and publish core is parked at the end of it, unmarked. `report`
    surfaces that as a mismatch rather than leaving it to be found by hand.
  * **Drift.** `anchors` re-derives the document's table so a stale line number
    is a diff rather than a wrong turn.

Commands
    report [path]   sections ranked by extractability, with owns/hub
    anchors [path]  markdown table rows, for pasting into the design document
    hot [path]      the fields that cross the most sections — the hub itself
    wide [path]     sections whose fields say they are not what they claim

Defaults to `src/Lightbox.App/ViewModels/MainViewModel.cs`; pass any C# file.
"""

from __future__ import annotations

import collections
import re
import sys
from pathlib import Path

DEFAULT = "src/Lightbox.App/ViewModels/MainViewModel.cs"

MARKER = re.compile(r"\s*// ---- (.+?) -+\s*$")
FIELD = re.compile(
    r"^\s+(?:private|internal|protected)\s+"
    r"(?:readonly\s+|static\s+|volatile\s+)*"
    r"[\w<>\[\],\.\?\s]+?\s(_\w+)\s*(?:=|;)"
)
# [ObservableProperty] backing fields need no access modifier in the generator's
# expected shape, so they are picked up from the attribute line instead.
OBSERVABLE = re.compile(r"^\s+(?:private|internal)?\s*(?:partial\s+)?[\w<>\[\],\.\?]+\s+(_\w+)\s*(?:=|;)")


def repo_root() -> Path:
    here = Path(__file__).resolve().parent
    for candidate in [here, *here.parents]:
        if (candidate / "src").is_dir():
            return candidate
    return Path.cwd()


class Section:
    def __init__(self, name: str, start: int, end: int) -> None:
        self.name = name
        self.start = start          # 1-based line of the first line after the marker
        self.end = end              # exclusive
        self.owns: list[str] = []
        self.hub: list[str] = []
        self.commands = 0
        self.properties = 0

    @property
    def length(self) -> int:
        return self.end - self.start

    @property
    def leafiness(self) -> tuple[int, int]:
        """Sorts leaves first: few foreign fields, then many of its own."""
        return (len(self.hub), -len(self.owns))


def analyse(path: Path) -> tuple[list[Section], dict[str, int], dict[str, set[str]]]:
    lines = path.read_text(encoding="utf-8").split("\n")

    marks = [(i, m.group(1).strip()) for i, line in enumerate(lines) if (m := MARKER.match(line))]
    sections = [
        Section(name, i + 2, (marks[j + 1][0] + 1) if j + 1 < len(marks) else len(lines) + 1)
        for j, (i, name) in enumerate(marks)
    ]

    declared: dict[str, int] = {}
    for i, line in enumerate(lines):
        if (m := FIELD.match(line)) and m.group(1) not in declared:
            declared[m.group(1)] = i + 1
    for i, line in enumerate(lines):
        if "[ObservableProperty]" in line and i + 1 < len(lines):
            if (m := OBSERVABLE.match(lines[i + 1])) and m.group(1) not in declared:
                declared[m.group(1)] = i + 2

    def section_of(line_no: int) -> str | None:
        for s in sections:
            if s.start <= line_no < s.end:
                return s.name
        return None

    home = {field: section_of(line_no) for field, line_no in declared.items()}
    used: dict[str, set[str]] = collections.defaultdict(set)

    for s in sections:
        body = "\n".join(lines[s.start - 1 : s.end - 1])
        s.commands = body.count("[RelayCommand")
        s.properties = body.count("[ObservableProperty]")
        for field in declared:
            if re.search(r"\b" + re.escape(field) + r"\b", body):
                used[field].add(s.name)
                (s.owns if home[field] == s.name else s.hub).append(field)
        s.owns.sort()
        s.hub.sort()

    return sections, declared, used


def cmd_report(sections, declared, used, path) -> None:
    total = sum(1 for f in used if used[f])
    single = sum(1 for f in used if len(used[f]) == 1)
    print(f"{path}: {len(sections)} sections, {len(declared)} private fields")
    print(f"  {single}/{total} fields ({100 * single // max(1, total)}%) are touched by exactly one section")
    print(f"  {sum(1 for f in used if len(used[f]) >= 5)} fields cross five or more\n")
    print(f"  {'section':<46} {'at':>7} {'ln':>5} {'cmd':>4} {'own':>4} {'hub':>4}")
    for s in sorted(sections, key=lambda s: s.leafiness):
        print(f"  {s.name[:46]:<46} {':' + str(s.start):>7} {s.length:>5} {s.commands:>4} {len(s.owns):>4} {len(s.hub):>4}")


def cmd_anchors(sections, declared, used, path) -> None:
    print("| Section | At | Size | Owns | Needs from the hub |")
    print("| --- | --- | --- | --- | --- |")
    for s in sorted(sections, key=lambda s: s.leafiness):
        if s.length < 140 or len(s.hub) > 5:
            continue
        owns = ", ".join(f"`{f}`" for f in s.owns) or "—"
        hub = ", ".join(f"`{f}`" for f in s.hub) or "nothing"
        print(f"| {s.name} | `:{s.start}` | {s.length} ln, {s.commands} cmd | {owns} | {hub} |")


def cmd_hot(sections, declared, used, path) -> None:
    print("Fields crossing five or more sections — this is the hub, whatever it is called:\n")
    for field, where in sorted(used.items(), key=lambda kv: -len(kv[1])):
        if len(where) < 5:
            break
        print(f"  {len(where):3d}  {field}")


def cmd_wide(sections, declared, used, path) -> None:
    print("Sections whose foreign-field count says they are not what the marker claims:\n")
    for s in sorted(sections, key=lambda s: -len(s.hub)):
        if len(s.hub) < 10:
            break
        print(f"  {s.name}  (`:{s.start}`, {s.length} ln) owns {len(s.owns)}, reaches {len(s.hub)}")
        print(f"      {', '.join(s.hub)}\n")


COMMANDS = {"report": cmd_report, "anchors": cmd_anchors, "hot": cmd_hot, "wide": cmd_wide}


def main(argv: list[str]) -> int:
    args = argv[1:]
    name = args[0] if args and args[0] in COMMANDS else "report"
    rest = [a for a in args if a not in COMMANDS]
    target = Path(rest[0]) if rest else repo_root() / DEFAULT
    if not target.is_absolute():
        target = repo_root() / target
    if not target.is_file():
        print(f"monolith.py: no such file: {target}", file=sys.stderr)
        return 2

    sections, declared, used = analyse(target)
    if not sections:
        print(f"monolith.py: {target} has no `// ---- section ----` markers", file=sys.stderr)
        return 1
    COMMANDS[name](sections, declared, used, target.relative_to(repo_root()))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
