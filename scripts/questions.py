#!/usr/bin/env python3
"""Keep the open questions in one file each, and derive the index over them.

`.claude/quality/questions/` holds one file per question; `QUESTIONS.md` is a
generated index over them and is not committed. That is a change of *shape*
rather than of content, and it was made for one reason: a single file meant
every branch that raised a question appended a section at the same place, so two
branches raising two questions conflicted by construction — on top of the id
collision they had already had. Filing into a new file conflicts with nothing.

**The index is not committed, for the reason `INDEX.md` is not** (Q55). A derived
file that is stored is a file that can drift, and every branch regenerates it, so
parallel branches collide there instead — which is the pain this is removing, one
artefact along. `codemap.py build` writes the code index from nothing and this
writes the question index from nothing, so neither is worth storing. The prose
that used to open `QUESTIONS.md` lives in `questions/README.md`, which is also
what GitHub renders when somebody browses the directory.

**The ids still come from `bugs.py freeid question`**, which allocates above
every branch rather than above the highest number in view. The split removes the
textual conflict; it does not by itself stop two branches choosing `Q91`.

Commands
    build           write the index from the question files
    check           report; exit 1 if a file is malformed or an id is reused
    find <term>     which question covers a topic
    open            the unanswered ones, for the session-start hook
    new "<title>"   allocate an id and open a file for it
"""

from __future__ import annotations

import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIR = ROOT / ".claude" / "quality" / "questions"
INDEX = ROOT / ".claude" / "quality" / "QUESTIONS.md"
README = DIR / "README.md"

START = "<!-- contents:start -->"
END = "<!-- contents:end -->"

# `# Q90 · Ledger ids collide … — **answered 2026-08-14: issue them**`
HEADING = re.compile(r"^#\s+(?P<id>Q\d+)\s+·\s+(?P<rest>.*?)\s*$")
# Not anchored to the end of the line, and `re-answered` counts. Three questions
# were reported open by a stricter version of this — one re-answered, two
# answered and then marked superseded — which is the wrong way for the detector
# to fail: an answered question in the "ask these first" list is noise, and noise
# in that list is how the list stops being read.
ANSWERED = re.compile(r"\*\*(?:re-)?answered\b(?P<how>[^*]*)\*\*")
FILENAME = re.compile(r"^(?P<id>Q\d+)-")


@dataclass
class Question:
    path: Path
    id: str
    title: str
    answer: str | None

    @property
    def number(self) -> int:
        return int(self.id[1:])

    @property
    def answered(self) -> bool:
        return self.answer is not None


def slug(title: str, limit: int = 60) -> str:
    """A filename that says which question it is, without becoming the question.

    Truncated on a word boundary: the id is what identifies the file and the slug
    is what makes a directory listing readable, so a slug long enough to wrap in a
    terminal has stopped doing its job.
    """
    words = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-").split("-")
    out: list[str] = []
    for word in words:
        if out and len("-".join(out)) + 1 + len(word) > limit:
            break
        out.append(word)
    return "-".join(out) or "question"


def read(path: Path) -> Question | None:
    """The heading, or nothing. A file with no `# Qn · …` is not a question."""
    first = ""
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            first = line
            break
    match = HEADING.match(first)
    if match is None:
        return None
    rest = match["rest"]
    if (answered := ANSWERED.search(rest)) is not None:
        return Question(path, match["id"], rest[:answered.start()].strip().rstrip("—- "),
                        answered["how"].strip(" :·—"))
    return Question(path, match["id"], rest.strip(), None)


def questions() -> list[Question]:
    """Every question, newest id first — which is the order they are read in."""
    found = [q for path in sorted(DIR.glob("Q*.md")) if (q := read(path)) is not None]
    return sorted(found, key=lambda q: -q.number)


def contents() -> str:
    rows = ["| | question |", "| --- | --- |"]
    for q in questions():
        status = f"answered{(' ' + q.answer) if q.answer else ''}" if q.answered else "**open**"
        rows.append(f"| **{q.id}** | [{q.title}](questions/{q.path.name}) — {status} |")
    return "\n".join(rows)


def cmd_build() -> int:
    """Preamble, then the derived table. The preamble is authored; this is not."""
    if not README.exists():
        print(f"no preamble at {README} — the index has nothing to open with", file=sys.stderr)
        return 1
    head = README.read_text(encoding="utf-8").rstrip()
    open_now = [q for q in questions() if not q.answered]
    note = (f"\n\n**{len(open_now)} unanswered.** Ask them before doing work they block.\n"
            if open_now else "\n\nNothing unanswered.\n")
    INDEX.write_text(
        f"<!-- Generated by scripts/questions.py. The questions themselves are in\n"
        f"     .claude/quality/questions/ — edit those, not this. -->\n\n"
        f"{head}{note}\n{START}\n{contents()}\n{END}\n",
        encoding="utf-8")
    print(f"  {INDEX.relative_to(ROOT)}: {len(questions())} questions, {len(open_now)} open")
    return 0


def cmd_check() -> int:
    """Every file is a question, every id is used once, every name matches its heading."""
    problems = 0
    seen: dict[str, Path] = {}
    for path in sorted(DIR.glob("*.md")):
        if path == README:
            continue
        entry = read(path)
        if entry is None:
            problems += 1
            print(f"  MALFORMED {path.name} — no `# Qn · title` heading on its first line")
            continue
        if (named := FILENAME.match(path.name)) is None or named["id"] != entry.id:
            problems += 1
            print(f"  MISNAMED  {path.name} — heading says {entry.id}")
            print("               the id is read from the filename by the ledger gate, so the "
                  "two disagreeing means one of them is not checked")
        if (first := seen.get(entry.id)) is not None:
            problems += 1
            print(f"  DUPLICATE {entry.id} — {path.name} and {first.name}")
            print("               `bugs.py ids --fix` renames the one this branch added")
        seen[entry.id] = path

    if problems:
        print(f"\n{problems} problem(s) in {DIR.relative_to(ROOT)}")
        return 1
    print(f"  {len(seen)} questions, ids unique, names agree with headings")
    return 0


def cmd_find(term: str) -> int:
    """Which question covers a topic — the manual's `find`, pointed at decisions."""
    needle = term.lower()
    hits = 0
    for q in questions():
        text = q.path.read_text(encoding="utf-8")
        if needle not in text.lower():
            continue
        hits += 1
        where = "answered" if q.answered else "OPEN"
        print(f"{q.path.relative_to(ROOT)}  [{where}]")
        print(f"    {q.id} · {q.title}")
        for n, line in enumerate(text.splitlines(), 1):
            if needle in line.lower():
                print(f"    {n}: {line.strip()[:100]}")
    if not hits:
        print(f"nothing on {term!r} — it may be undecided rather than decided")
    return 0


def cmd_open() -> int:
    unanswered = [q for q in questions() if not q.answered]
    for q in unanswered:
        print(f"{q.id} · {q.title}")
    return 0


def cmd_new(title: str) -> int:
    """Allocate an id and write the file. The argument is what has to be thought about."""
    allocated = subprocess.run(
        (sys.executable, str(ROOT / "scripts" / "bugs.py"), "freeid", "question"),
        capture_output=True, text=True)
    entry_id = allocated.stdout.strip()
    if not re.fullmatch(r"Q\d+", entry_id):
        print(f"could not allocate an id: {allocated.stderr.strip()}", file=sys.stderr)
        return 1

    path = DIR / f"{entry_id}-{slug(title)}.md"
    path.write_text(
        f"# {entry_id} · {title}\n\n"
        f"Raised by:\n\n"
        f"What it blocks:\n\n"
        f"**Recommendation:** — and what the alternatives cost.\n",
        encoding="utf-8")
    cmd_build()
    print(f"  {path.relative_to(ROOT)}")
    print("  ask it with AskUserQuestion, then record the answer here — a question written "
          "to the file and mentioned in passing is a guess waiting to be corrected")
    return 0


def main() -> None:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    if cmd in ("build", "sync"):
        sys.exit(cmd_build())
    elif cmd == "check":
        sys.exit(cmd_check())
    elif cmd == "find":
        if len(sys.argv) < 3:
            sys.exit("usage: questions.py find <term>")
        sys.exit(cmd_find(sys.argv[2]))
    elif cmd == "open":
        sys.exit(cmd_open())
    elif cmd == "new":
        if len(sys.argv) < 3:
            sys.exit('usage: questions.py new "<title>"')
        sys.exit(cmd_new(sys.argv[2]))
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
