#!/usr/bin/env python3
"""Keep the manual's contents list derived from the manual's sections.

`docs/MANUAL.md` is the index; the sections live in `docs/manual/*.md`. The
contents list between the markers is **generated**, for the reason
`bugs.py` derives its checkboxes and `roadmap.py` derives its marks: a
hand-kept list of what exists rots the first time somebody adds a file and
forgets, and a contents list that lies is worse than none because it is what a
reader navigates by.

The section files are the fact. This only reports them.

`find` exists for the other half of the same problem. The manual is ~100 KB,
and `CLAUDE.md` requires the relevant section to be updated in the same commit
as the change — so something has to answer "which file covers this" without
reading all of it. Same reasoning as `codemap.py find` for source and
`codemap.py promises` for the behaviour inventory.

Commands
    check         report; exit 1 if the contents list disagrees with docs/manual/
    sync          rewrite the contents list in place
    find <term>   which section covers a topic, and the headings that mention it
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INDEX = ROOT / "docs" / "MANUAL.md"
SECTIONS = ROOT / "docs" / "manual"

START = "<!-- contents:start -->"
END = "<!-- contents:end -->"


def sections() -> list[tuple[str, str]]:
    """(filename, title) for every section file, in filename order.

    Ordering is the filename's numeric prefix rather than anything inside the
    file, so the reading order is visible in a directory listing and renaming a
    file is how you reorder the manual.
    """
    out = []
    for path in sorted(SECTIONS.glob("*.md")):
        text = path.read_text(encoding="utf-8")
        match = re.search(r"^#\s+(.+)$", text, re.MULTILINE)
        if match is None:
            print(f"{path.relative_to(ROOT)} has no H1 title", file=sys.stderr)
            sys.exit(2)
        out.append((path.name, match.group(1).strip()))
    return out


def contents_block() -> str:
    lines = [START, ""]
    for n, (name, title) in enumerate(sections(), start=1):
        lines.append(f"{n}. [{title}](manual/{name})")
    lines += ["", END]
    return "\n".join(lines)


def current(text: str) -> str | None:
    match = re.search(re.escape(START) + r".*?" + re.escape(END), text, re.DOTALL)
    return match.group(0) if match else None


def cmd_find(term: str) -> None:
    """Which section file covers a topic.

    Reports the heading a match sits under rather than the raw line, because
    the question this answers is "which part of the manual do I edit", and a
    heading is the answer to that where a line number is not.
    """
    needle = term.lower()
    hits = 0
    for path in sorted(SECTIONS.glob("*.md")):
        lines = path.read_text(encoding="utf-8").split("\n")
        title, heading, found = path.stem, None, []
        for n, line in enumerate(lines, start=1):
            if line.startswith("# "):
                title = line[2:].strip()
                heading = None
            elif line.startswith("#"):
                heading = line.lstrip("#").strip()
            if needle in line.lower():
                where = heading or "(opening)"
                if not found or found[-1][0] != where:
                    found.append((where, n))
        if found:
            hits += len(found)
            print(f"\n{title} — docs/manual/{path.name}")
            for where, n in found[:8]:
                print(f"    {where}  :{n}")
            if len(found) > 8:
                print(f"    … and {len(found) - 8} more places in this section")

    if not hits:
        print(f"No section mentions '{term}'.")
        print("Try a shorter term — this searches the manual's own words, not the code.")
    else:
        print(f"\n{hits} place(s). Update the section that owns the behaviour, "
              f"not every one that mentions it.")


def main() -> None:
    args = sys.argv[1:]
    if not args or args[0] in {"-h", "--help", "help"}:
        print(__doc__)
        return

    if args[0] == "find" and len(args) > 1:
        cmd_find(" ".join(args[1:]))
        return

    text = INDEX.read_text(encoding="utf-8")
    have = current(text)
    if have is None:
        print(f"No {START} / {END} markers in docs/MANUAL.md — nothing to derive.",
              file=sys.stderr)
        sys.exit(2)
    want = contents_block()

    if args[0] == "check":
        found = len(sections())
        if have == want:
            print(f"Contents current — {found} sections.")
            return
        print(f"Contents is stale — {found} sections in docs/manual/ do not match "
              f"the list in docs/MANUAL.md.\nRun: python3 scripts/manual.py sync",
              file=sys.stderr)
        sys.exit(1)

    if args[0] == "sync":
        if have == want:
            print(f"Contents already current — {len(sections())} sections.")
            return
        INDEX.write_text(text.replace(have, want), encoding="utf-8")
        print(f"Rewrote the contents list — {len(sections())} sections.")
        return

    print(__doc__)
    sys.exit(2)


if __name__ == "__main__":
    main()
