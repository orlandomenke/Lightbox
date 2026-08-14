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

**An id is issued, not chosen.** `new` and `freeid` allocate above every branch
this clone can see rather than above the working tree, because "the highest
number in the file I have" is the same number on two branches that started from
the same `main` — which is how six bugs and three questions had to be renumbered
by hand in the six days to 2026-08-14.

Commands
    check           report; exit 1 if a mark disagrees with the code
    sync            rewrite the checkboxes in place
    ids             ids only: unique, allocated once, none lost. No index, instant
    ids --fix       move the entry that took a number twice, citations included
    new <domain> "<title>"    file a bug with an allocated id
    freeid [bug|question]     print the next free id and nothing else
    next            highest-priority open bugs, for a loop to pick from
    mine <domain>   open bugs in one domain — what a working agent greps
    stats           counts per priority and per domain
"""

from __future__ import annotations

import os
import re
import subprocess
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
    def manual(self) -> bool:
        """`evidence: manual` — a bug no headless test can reach.

        Synthetic pen and hover input through Xvfb is unreliable here (see
        CLAUDE.md), so a few UI bugs genuinely cannot be closed by a test. They
        say so instead of naming an anchor that would never resolve, and they
        never auto-close — a human ticks them by editing the mark, which is the
        one case where a claim is the best evidence available.
        """
        return self.evidence == ["manual"]

    @property
    def status(self) -> str:
        """A bug is fixed only when EVERY anchor resolves.

        Stricter than the roadmap's partial `[~]` on purpose: a half-guarded
        fix is an open bug that has stopped looking like one.
        """
        if self.manual:
            return self.mark  # human-owned; sync must not move it either way
        if not self.evidence:
            return " "
        return "x" if not self.missing else " "

    @property
    def unverifiable(self) -> bool:
        return not self.evidence

    @property
    def partly_resolved(self) -> bool:
        """Some anchors resolve and some do not — two very different stories.

        Either the fix is half-landed, which `status` already reports as open and
        correctly, or **one of the names is wrong**, in which case the entry can never
        close however green the suite is. B27 sat open for exactly that: its list named
        the private method `BleedReach` beside three real tests, and `codemap.py` indexes
        public symbols only, so the anchor was unresolvable by construction while the
        behaviour had been fixed and guarded all along.

        Reported but never fatal, and that distinction was learned immediately: a bug filed
        before its fix is written names new test methods *and* often an existing test file, so
        it resolves partially the moment it is written. Six of them appeared the day this check
        did. Failing on that would make `check` cry wolf on ordinary planned work, so this is a
        diagnostic for a person reading the report, not a rule.
        """
        return bool(self.resolved) and bool(self.missing)

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
        if bug.manual:
            continue
        for anchor in bug.evidence:
            (bug.resolved if index.has(anchor) else bug.missing).append(anchor)


def bad_domains(bugs: list[Bug]) -> list[Bug]:
    return [b for b in bugs if b.domain not in DOMAINS]


def duplicate_ids(bugs: list[Bug]) -> dict[str, list[Bug]]:
    """Ids used twice.

    Two entries once shared **B39** — an effect-brush artefact and a CI runtime
    mismatch — and nothing noticed, because every other check in here looks at one
    entry at a time. An id is how a bug is referred to from a test name, a design doc
    and a commit message, so a reused one silently points at the wrong defect: the
    `.NET 8` entry cited a doc that cited it back, while a test file claimed the same
    number for something unrelated. Cheap to detect, and impossible to spot by eye in
    a file this long.
    """
    seen: dict[str, list[Bug]] = {}
    for bug in bugs:
        seen.setdefault(bug.id, []).append(bug)
    return {i: b for i, b in seen.items() if len(b) > 1}


QUESTIONS = ROOT / ".claude" / "quality" / "QUESTIONS.md"

# ## Q18 · Do flat point arrays cost schema adherence?
QUESTION = re.compile(r"^## (?P<id>Q\d+) · (?P<title>.*?)\s*$")


def duplicate_questions() -> dict[str, list[str]]:
    """The same detector pointed at `QUESTIONS.md`, because it happened twice.

    `duplicate_ids` above exists because two bugs shared **B39**. Two questions then
    shared **Q19** — "are Linux and macOS shipping targets" and "when a textured line
    is re-shaped, may its texture change" — for exactly the same reason: a new entry
    numbered by eye against a file long enough that the last id is off-screen. A
    question id is cited from `CLAUDE.md`, from design docs, from bug entries and from
    the roadmap, so a reused one sends a reader to the wrong argument.

    Checked here rather than in a script of its own: this is the file that already
    knows how to say "renumber all but one", and a second script is a second thing to
    remember to run.
    """
    if not QUESTIONS.exists():
        return {}
    return duplicates_in(question_ids_in(QUESTIONS.read_text(encoding="utf-8")))


# ---------------------------------------------------------------------------
# The ids, read from text rather than from disk
#
# Everything below works on a *string* so that one parse serves three callers:
# the working file, `git show <parent>:<ledger>` for the other side of a merge,
# and a synthetic fixture in a test. `duplicate_ids` above stays as it is —
# it reports `Bug` objects, which is what the rest of `check` wants.
# ---------------------------------------------------------------------------


def bug_ids_in(text: str) -> list[tuple[str, str]]:
    return [(m["id"], m["title"].strip())
            for line in text.splitlines() if (m := ENTRY.match(line))]


def question_ids_in(text: str) -> list[tuple[str, str]]:
    return [(m["id"], m["title"]) for line in text.splitlines() if (m := QUESTION.match(line))]


def duplicates_in(pairs: list[tuple[str, str]]) -> dict[str, list[str]]:
    seen: dict[str, list[str]] = {}
    for entry_id, title in pairs:
        seen.setdefault(entry_id, []).append(title)
    return {i: t for i, t in seen.items() if len(t) > 1}


def _git(*args: str) -> str | None:
    """Git, or None. Every caller here has a sensible answer for "no git"."""
    try:
        done = subprocess.run(("git", *args), cwd=ROOT, capture_output=True, text=True)
    except OSError:
        return None
    return done.stdout if done.returncode == 0 else None


def text_at(spec: str, path: Path) -> str | None:
    """A ledger as of `spec` — a path on disk, or a git ref.

    A path is tried first and it is not a fallback: it is how a test hands in a
    fixture without needing a repository at all.
    """
    candidate = Path(spec)
    if candidate.exists():
        return candidate.read_text(encoding="utf-8")
    return _git("show", f"{spec}:{path.relative_to(ROOT).as_posix()}")


def merge_parents() -> list[str]:
    """The parents of HEAD, or nothing at all unless HEAD is a merge.

    This is the whole trick. A duplicate id is created by two branches and only
    *exists* in the merged file, so the moment to look at two ledgers at once is
    the moment a merge commit exists. Outside a merge there is nothing to compare
    against and this returns an empty list rather than guessing at a base — a
    branch that simply has not merged `main` yet is not missing anything.
    """
    line = _git("rev-list", "--parents", "-n", "1", "HEAD")
    if not line:
        return []
    # `<commit> <parent>...` — so the parents are everything after the first
    # field, and two or more of them is what makes HEAD a merge. Both sides are
    # returned rather than just the one merged in: an entry can be dropped from
    # either, and taking "ours" loses theirs exactly as easily as the reverse.
    parents = line.split()[1:]
    return parents if len(parents) > 1 else []


ALLOW_DELETION = "LIGHTBOX_ALLOW_LEDGER_DELETION"


def lost_ids(before: list[tuple[str, str]], after: list[tuple[str, str]]) -> list[tuple[str, str]]:
    """Ids that were there and are not any more.

    **This is the failure the duplicate check cannot see, and it is the worse of
    the two.** When two branches have each filed a bug under the same id, the
    ledger conflicts, and the mechanical way to resolve a conflict is to take one
    side. That deletes the other branch's entry — and leaves a file with no
    duplicate in it, so every check passes and the loss is permanent and silent.
    A duplicate, by contrast, is loud and costs a renumber.

    Nothing in this repository deletes a ledger entry: a fixed bug keeps its id
    and moves below the rule, and an answered question keeps its heading with the
    answer appended. So a vanished id is always worth refusing. When one really
    must go, `LIGHTBOX_ALLOW_LEDGER_DELETION=1` says so deliberately.
    """
    present = {entry_id for entry_id, _ in after}
    return [(i, t) for i, t in before if i not in present]


LEDGERS = (
    ("ID", BUGS, bug_ids_in),
    ("Q ", QUESTIONS, question_ids_in),
)


# ---------------------------------------------------------------------------
# Allocating an id — the half of this that was missing
#
# WHY THIS EXISTS. Every check below this line detects a collision. None of them
# prevented one, and the reason is that nothing here ever *issued* an id: an
# author read the file, found the highest number in it and added one. That is
# `max(what my branch happens to have fetched) + 1`, which two branches compute
# to the same answer whenever they start from the same snapshot — which is what
# starting from `main` means.
#
# The measurement that produced this, taken over the six days to 2026-08-14: six
# bug renumbers and three question renumbers, one of them renumbered twice
# because the second guess collided as well. Every one of them was a hand-edited
# commit on a branch whose objective was something else.
#
# So `next_id` reads every ref this clone can see rather than the working tree
# alone. That does not make a collision impossible — two branches allocating
# between the same pair of fetches still land on the same number — but it moves
# the window from "as long as your branch is open" to "as long as your fetch is
# stale", and `cmd_ids --fix` below makes what is left cost nothing.
# ---------------------------------------------------------------------------


def _other_ref_tips() -> list[str]:
    """Every branch tip this clone knows, local and remote, HEAD's own excluded.

    Deliberately tips rather than history: a merged branch's ids are in the
    default branch's tip already, and an id used on a branch that was abandoned
    without merging is still spoken for as long as the branch exists. Both are
    what an allocator wants to avoid.
    """
    listed = _git("for-each-ref", "--format=%(objectname) %(refname)",
                  "refs/heads", "refs/remotes")
    if not listed:
        return []
    # HEAD's own ref is the tree being allocated *from*, and its upstream is the
    # same branch as somebody else's clone sees it — counting either as "another
    # branch" would make a branch clash with itself the moment it is pushed.
    ours = {(_git("symbolic-ref", "--quiet", "HEAD") or "").strip(),
            (_git("rev-parse", "--symbolic-full-name", "@{upstream}") or "").strip()}
    tips: list[str] = []
    for line in listed.splitlines():
        sha, _, name = line.partition(" ")
        # `refs/remotes/*/HEAD` is a symbolic alias for a branch already listed.
        if not sha or name in ours or name.endswith("/HEAD"):
            continue
        if sha not in tips:
            tips.append(sha)
    return tips


def ledger_texts(path: Path, extra: list[str] | None = None) -> list[str]:
    """This ledger as it reads everywhere it exists.

    Git is consulted only for a ledger that lives *in this repository*. A fixture
    handed in by a test lives in a temporary directory, so it gets exactly the
    texts the test passes and nothing from this repository's history — the same
    reasoning `cmd_ids` applies to `--ledger`, and the thing that makes the
    allocator testable without building a repository to test it in.
    """
    texts: list[str] = []
    if path.exists():
        texts.append(path.read_text(encoding="utf-8"))
    for spec in extra or []:
        if (text := text_at(spec, path)) is not None:
            texts.append(text)
    if path.is_relative_to(ROOT):
        rel = path.relative_to(ROOT).as_posix()
        for sha in _other_ref_tips():
            if (text := _git("show", f"{sha}:{rel}")) is not None:
                texts.append(text)
    return texts


def ids_everywhere(path: Path, reader, extra: list[str] | None = None) -> set[str]:
    return {entry_id
            for text in ledger_texts(path, extra)
            for entry_id, _ in reader(text)}


def next_id(prefix: str, path: Path, reader, extra: list[str] | None = None) -> str:
    """One above the highest id on any ref, not one above this working tree's."""
    numbers = [int(i[len(prefix):]) for i in ids_everywhere(path, reader, extra)
               if i[len(prefix):].isdigit()]
    return f"{prefix}{max(numbers, default=0) + 1}"


def fetch_origin() -> str:
    """Refresh the refs the allocator reads, and say what happened either way.

    An allocator is only as fresh as its last fetch, so it does its own — and
    reports rather than fails when it cannot, because filing a bug on a train is
    a reasonable thing to do and refusing to would send the author back to
    counting by eye, which is the failure this exists to end.
    """
    try:
        done = subprocess.run(("git", "fetch", "--quiet", "--no-tags", "origin"),
                              cwd=ROOT, capture_output=True, text=True, timeout=45)
    except (OSError, subprocess.TimeoutExpired):
        return "could not reach origin — allocating from the refs already fetched"
    if done.returncode != 0:
        return "could not reach origin — allocating from the refs already fetched"
    return "fetched origin"


# ---------------------------------------------------------------------------
# Moving an id that collided anyway
# ---------------------------------------------------------------------------


def default_branch_ref() -> str | None:
    """What `origin/HEAD` records, or the usual names — asked, never assumed."""
    recorded = (_git("symbolic-ref", "--quiet", "refs/remotes/origin/HEAD") or "").strip()
    if recorded:
        return recorded.rsplit("refs/remotes/", 1)[-1]
    for candidate in ("origin/main", "origin/master", "main", "master"):
        if _git("rev-parse", "--verify", "--quiet", candidate):
            return candidate
    return None


def branch_base() -> str | None:
    """Where this branch left the default branch — the line between mine and theirs."""
    ref = default_branch_ref()
    if not ref:
        return None
    base = _git("merge-base", "HEAD", ref)
    return base.strip() if base else None


HUNK = re.compile(r"^@@ -\S+ \+(?P<start>\d+)(?:,\d+)? @@")


def added_lines(base: str) -> dict[str, list[tuple[int, str]]]:
    """path -> [(line number, exact text)] this branch added, working tree included.

    `git diff <base>` rather than `<base>...HEAD` on purpose: an entry filed a
    moment ago and not yet committed is exactly the one most likely to need
    renumbering, and it is the author's own line just as much as a committed one.
    """
    diff = _git("diff", "--unified=0", "--no-color", "--no-renames", base, "--")
    if not diff:
        return {}
    found: dict[str, list[tuple[int, str]]] = {}
    path: str | None = None
    line_no = 0
    for line in diff.splitlines():
        if line.startswith("+++ "):
            path = line[6:] if line.startswith("+++ b/") else None
        elif (hunk := HUNK.match(line)) is not None:
            line_no = int(hunk["start"])
        elif path and line.startswith("+"):
            found.setdefault(path, []).append((line_no, line[1:]))
            line_no += 1
    return found


def move_ids(moves: dict[str, str], base: str) -> list[str]:
    """Rewrite an id everywhere *this branch wrote it*, and nowhere else.

    Scoped to lines the branch added because that is precisely the set of
    citations that belong to the entry being moved. The id it collided with is
    older, and every mention of it in the tree — a test name, a design note, the
    other entry's own line — means the one that is keeping the number. A
    whole-file replace would rewrite those too and point them at the wrong bug,
    which is the failure a renumber is supposed to prevent rather than cause.
    """
    touched: list[str] = []
    for rel, additions in added_lines(base).items():
        path = ROOT / rel
        if not path.exists():
            continue
        try:
            lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
        except (UnicodeDecodeError, OSError):
            continue
        changed = False
        for line_no, text in additions:
            if not 1 <= line_no <= len(lines):
                continue
            current = lines[line_no - 1]
            # The diff describes the file as it was read; anything that has moved
            # since is left alone rather than rewritten at a guessed position.
            if current.rstrip("\r\n") != text:
                continue
            rewritten = current
            for old, new in moves.items():
                rewritten = re.sub(rf"\b{re.escape(old)}\b", new, rewritten)
            if rewritten != current:
                lines[line_no - 1] = rewritten
                changed = True
                touched.append(f"{rel}:{line_no}")
        if changed:
            path.write_text("".join(lines), encoding="utf-8")
    return touched


def located(text: str, prefix: str) -> list[tuple[int, str, str]]:
    """(line number, id, title) for every entry, so an occurrence can be moved."""
    pattern = ENTRY if prefix == "B" else QUESTION
    return [(n, m["id"], m["title"].strip())
            for n, line in enumerate(text.splitlines(), 1) if (m := pattern.match(line))]


def _occurrences_to_move(source: Path, prefix: str, duplicated: dict[str, list[str]],
                         clashed: dict[str, str], base: str | None) -> list[tuple[int, str, str]]:
    """Which entry gives up the number — the one this branch wrote, every time.

    The other one is older. It is on the default branch, or on a branch that
    pushed first, and every citation of it out in the tree already means it; the
    entry that has to move is the one whose citations are all still on this
    branch, where they can be moved with it.

    Without a repository to ask — a fixture, a clone with no default branch — the
    first occurrence keeps the id. That is arbitrary, and it is the only part of
    this that is: it decides which of two entries renumbers, never whether both
    survive.
    """
    text = source.read_text(encoding="utf-8")
    spots = located(text, prefix)
    rel = source.relative_to(ROOT).as_posix() if source.is_relative_to(ROOT) else None
    added = {n for n, _ in added_lines(base).get(rel, [])} if base and rel else set()

    moving: list[tuple[int, str, str]] = []
    for entry_id in duplicated:
        occurrences = [s for s in spots if s[1] == entry_id]
        ours = [s for s in occurrences if s[0] in added]
        moving.extend(ours if 0 < len(ours) < len(occurrences) else occurrences[1:])
    for entry_id in clashed:
        moving.extend(s for s in spots if s[1] == entry_id)
    return moving


def clashing_ids(path: Path, reader, mine: list[tuple[str, str]],
                 elsewhere: list[str], base: str | None) -> dict[str, str]:
    """Ids this branch created that another branch created too — id -> its title.

    **This is the check that runs a merge too late everywhere else.** A duplicate
    only exists in the merged file, so `duplicates_in` cannot see the collision
    until somebody merges, by which point the number is on two branches and one
    of them has to be unpicked by hand. Two branches that each filed `B208` are
    each perfectly consistent read alone.

    Read together they are not, and this reads them together: an id that is in my
    ledger, absent from where my branch left the default branch, and present in
    somebody else's, was allocated twice. The merge-base is what keeps that from
    firing on ordinary work — an id both sides carry because it was already on
    `main` is shared, not clashed, however either side has edited its line since.

    A matching title is the same entry rather than a collision, which is what my
    own branch looks like once it has been pushed.
    """
    if base is None and not elsewhere:
        return {}
    base_ids = set()
    if base is not None and (text := text_at(base, path)) is not None:
        base_ids = {entry_id for entry_id, _ in reader(text)}

    theirs: dict[str, set[str]] = {}
    for text in ledger_texts(path, elsewhere)[1 if path.exists() else 0:]:
        for entry_id, title in reader(text):
            theirs.setdefault(entry_id, set()).add(title.strip())

    return {entry_id: title for entry_id, title in mine
            if entry_id not in base_ids
            and entry_id in theirs
            and title.strip() not in theirs[entry_id]}


def renumber(source: Path, reader, prefix: str,
             moving: list[tuple[int, str, str]], elsewhere: list[str],
             base: str | None) -> list[str]:
    """Move each occurrence to a fresh id, and take its citations with it.

    The new ids clear every ref rather than just the file being repaired —
    renumbering into a number the *next* branch is about to take would be a busy
    way of achieving nothing.
    """
    taken = ids_everywhere(source, reader, elsewhere) | {i for _, i, _ in moving}
    moves: dict[str, str] = {}
    for _, entry_id, _ in moving:
        number = max((int(i[len(prefix):]) for i in taken if i[len(prefix):].isdigit()),
                     default=0) + 1
        moves[entry_id] = f"{prefix}{number}"
        taken.add(moves[entry_id])

    said: list[str] = []
    if base is not None:
        for where in move_ids(moves, base):
            said.append(f"    moved a citation at {where}")

    # The entry's own line, if the pass above did not already reach it — it will
    # not have when there is no repository to diff against, which is every test
    # fixture, and it must not be left carrying the old number either way.
    lines = source.read_text(encoding="utf-8").splitlines(keepends=True)
    for line_no, entry_id, _ in moving:
        if 1 <= line_no <= len(lines) and re.search(rf"\b{entry_id}\b", lines[line_no - 1]):
            lines[line_no - 1] = re.sub(
                rf"\b{entry_id}\b", moves[entry_id], lines[line_no - 1])
    source.write_text("".join(lines), encoding="utf-8")

    for old, new in moves.items():
        said.insert(0, f"  RENUMBERED {old} -> {new}")
    return said


def cmd_ids(argv: list[str]) -> int:
    """Ids only: unique within the file, allocated once, and none dropped by a merge.

    Deliberately separate from `check`, which resolves every evidence anchor
    against the generated code index and rebuilds it when stale. That is the
    right thing for CI and far too slow for a git hook — and the hook is where
    this has to run, because CI only sees a collision after it is published.
    """
    ledgers = {name: (path, reader) for name, path, reader in LEDGERS}
    overrides = {
        "ID": next((a.split("=", 1)[1] for a in argv if a.startswith("--ledger=")), None),
        "Q ": next((a.split("=", 1)[1] for a in argv if a.startswith("--questions=")), None),
    }
    fix = "--fix" in argv
    elsewhere = [a.split("=", 1)[1] for a in argv if a.startswith("--elsewhere=")]
    base_given = next((a.split("=", 1)[1] for a in argv if a.startswith("--base=")), None)

    # Explicit refs win; otherwise the parents of HEAD, and only when the ledgers
    # are the real ones. An overridden ledger compared against this repository's
    # history would report every id in `main` as lost, which is true of the
    # fixture and says nothing about the tree.
    against = [a for a in argv if not a.startswith("-")]
    real = not any(overrides.values())
    if not against and real:
        against = merge_parents()
    base = base_given or (branch_base() if real else None)

    problems = 0
    for name, (path, reader) in ledgers.items():
        source = Path(overrides[name] or path)
        if not source.exists():
            continue
        prefix = "B" if name == "ID" else "Q"
        now = reader(source.read_text(encoding="utf-8"))
        print(f"  {len(now)} {'bug' if name == 'ID' else 'question'} ids in {source.name}")

        duplicated = duplicates_in(now)
        clashed = clashing_ids(source, reader, now, elsewhere, base)

        if fix and (duplicated or clashed):
            for line in renumber(source, reader, prefix,
                                 _occurrences_to_move(source, prefix, duplicated, clashed, base),
                                 elsewhere, base):
                print(line)
            now = reader(source.read_text(encoding="utf-8"))
            duplicated, clashed = duplicates_in(now), {}

        for entry_id, titles in duplicated.items():
            problems += 1
            print(f"  DUPLICATE {name} {entry_id}  used {len(titles)} times: "
                  + " / ".join(t[:40] for t in titles))
            print("               renumber all but one — an id is cited from tests and docs")

        for entry_id, title in clashed.items():
            problems += 1
            print(f"  CLASHES   {name} {entry_id}  another branch already took it: {title[:50]}")
            print("               allocated twice — `bugs.py ids --fix` moves this branch's entry")

        for spec in against:
            if (before := text_at(spec, path)) is None:
                continue
            for entry_id, title in lost_ids(reader(before), now):
                if os.environ.get(ALLOW_DELETION) == "1":
                    print(f"  DELETED   {name} {entry_id}  {title[:50]} — allowed by {ALLOW_DELETION}")
                    continue
                problems += 1
                where = Path(spec).name if Path(spec).exists() else spec[:12]
                print(f"  LOST      {name} {entry_id}  was in {where} and is gone: {title[:50]}")
                print("               a merge resolved by taking one side drops the other side's "
                      f"entry — keep both and renumber, or set {ALLOW_DELETION}=1 if it really goes")

    if problems:
        print(f"\n{problems} problem(s) — the ledger ids are not safe to push")
        return 1
    print("  ids unique, unclashed, none lost"
          + (f" (against {len(against)} ref(s))" if against else ""))
    return 0


def cmd_freeid(argv: list[str]) -> int:
    """The next id nobody has taken, on any branch. Prints it and nothing else.

    Separate from `new` because a question is not one line: its heading, the
    argument that raised it, the options and their costs are all authored, and a
    command that generated a stub would only be generating the easy part. The id
    is the part that has to be allocated rather than guessed, so that is the part
    this issues.
    """
    which = next((a for a in argv if not a.startswith("-")), "bug")
    if which not in ("bug", "question"):
        print("usage: bugs.py freeid [bug|question] [--no-fetch]", file=sys.stderr)
        return 2
    prefix, path, reader = ("B", BUGS, bug_ids_in) if which == "bug" else ("Q", QUESTIONS, question_ids_in)
    override = next((a.split("=", 1)[1] for a in argv
                     if a.startswith("--ledger=") or a.startswith("--questions=")), None)
    extra = [a.split("=", 1)[1] for a in argv if a.startswith("--elsewhere=")]
    if override:
        path = Path(override)
    elif "--no-fetch" not in argv:
        print(f"  {fetch_origin()}", file=sys.stderr)
    print(next_id(prefix, path, reader, extra))
    return 0


def cmd_new(argv: list[str]) -> int:
    """File a bug, with an id that is allocated rather than counted by eye."""
    positional = [a for a in argv if not a.startswith("-")]
    if len(positional) < 2:
        print('usage: bugs.py new <domain> "<title>" [-p P2] [-e Test,Other] [--no-fetch]',
              file=sys.stderr)
        return 2
    domain, title = positional[0], positional[1]
    if domain not in DOMAINS:
        print(f"unknown domain {domain!r} — one of: {' '.join(sorted(DOMAINS))}", file=sys.stderr)
        return 2

    priority = next((a.split("=", 1)[1] if "=" in a else a[2:] for a in argv
                     if a.startswith("-p")), "P2")
    evidence = next((a.split("=", 1)[1] for a in argv if a.startswith("-e=") or a.startswith("--evidence=")), "")
    if priority not in ("P1", "P2", "P3", "P4"):
        print(f"unknown priority {priority!r} — P1 to P4", file=sys.stderr)
        return 2

    if "--no-fetch" not in argv:
        print(f"  {fetch_origin()}")
    entry_id = next_id("B", BUGS, bug_ids_in)
    tail = f" `evidence: {evidence}`" if evidence else ""
    line = f"- [ ] **{entry_id}** `{priority}` `{domain}` {title}{tail}"

    insert_entry(line, domain)
    print(f"  {entry_id} filed under {domain}")
    print(f"  {line}")
    if not evidence:
        print("  no evidence anchor yet — `check` refuses an entry that names nothing "
              "that would prove the fix")
    return 0


def insert_entry(line: str, domain: str) -> None:
    """Put the entry in its domain's group, under `## Open`.

    Placed rather than appended because `sync` sorts by domain, then priority,
    then id — appending to the end of the file would put a new bug under
    `## Fixed`, where the next sync would move it and the diff would show a
    relocation instead of a filing.
    """
    lines = BUGS.read_text(encoding="utf-8").splitlines()
    start = lines.index(OPEN_HEADING) + 1
    end = next((n for n in range(start, len(lines)) if lines[n].startswith("## ")), len(lines))

    heading = f"### {domain}"
    if heading in lines[start:end]:
        at = lines.index(heading, start, end) + 1
        while at < end and not lines[at].strip():
            at += 1
        lines[at:at] = [line]
    else:
        # Alphabetical among the groups, because that is the order `sync` writes.
        after = [n for n in range(start, end)
                 if lines[n].startswith("### ") and lines[n] < heading]
        at = (next((n for n in range(after[-1] + 1, end) if lines[n].startswith("### ")), end)
              if after else start)
        lines[at:at] = [heading, "", line, ""]

    BUGS.write_text("\n".join(lines) + "\n", encoding="utf-8")


OPEN_HEADING = "## Open"
FIXED_HEADING = "## Fixed"


def _entry_block(lines: list[str], start: int) -> tuple[list[str], int]:
    """One entry: its header line plus the indented notes under it.

    A bug is a header and however many `  - …` lines of reasoning follow it, so
    relocating one means moving the block rather than the line. Trailing blanks are
    dropped here and re-added on assembly, which is what keeps the spacing even
    however many times this runs.
    """
    block = [lines[start]]
    i = start + 1
    while i < len(lines) and (lines[i].startswith("  ") or not lines[i].strip()):
        # Stop at a blank line that is followed by something which is not a note —
        # that blank belongs to the next entry or the next heading, not to this one.
        if not lines[i].strip():
            nxt = next((l for l in lines[i + 1:] if l.strip()), "")
            if not nxt.startswith("  "):
                break
        block.append(lines[i])
        i += 1
    while block and not block[-1].strip():
        block.pop()
    return block, i


#: Priority order for sorting. Anything unrecognised sorts last rather than first,
#: because an entry with a malformed priority should not lead the section.
_PRIORITY_ORDER = {"P1": 0, "P2": 1, "P3": 2, "P4": 3}

#: Emitted per domain group. Regenerated every sync, so it is structure rather than
#: prose and the parser drops it on the way in.
SUBHEADING = re.compile(r"^### ")


def _sort_key(header: str) -> tuple[str, int, int]:
    """By domain, then priority, then newest first.

    <b>Domain first because that is how the ledger is read.</b> Nobody opens this
    file asking "what is the highest-numbered bug"; they open it about to edit the
    brush engine, or the project docker, and they want that area's defects together
    — which is what `bugs.py mine <domain>` already answers on the command line and
    what the file itself did not. Grouping by domain also makes an entry filed under
    the wrong one obvious on sight, where before it was a token in a line of tokens.

    Priority second so the order inside a group is the order to work in, and id
    descending last so a tie is broken by recency, which is the order the file used
    by hand before any of this was mechanical.
    """
    m = ENTRY.match(header)
    if not m:
        return ("", 99, 0)
    return (
        m.group("domain"),
        _PRIORITY_ORDER.get(m.group("priority"), 98),
        -int(m.group("id")[1:]),
    )


def relocate(lines: list[str], bugs: list[Bug]) -> list[str] | None:
    """
    Put every open entry under `## Open` and every closed one under `## Fixed`.

    <b>The file said this already happened and it did not.</b> `## Fixed` has carried
    the sentence "entries move here when sync closes them" since it was added, while
    sync only ever rewrote a checkbox in place — so closed bugs piled up at the top
    and the section that was supposed to hold them stayed empty. Documented behaviour
    that does not exist is the same defect this whole file exists to prevent, one
    level up.

    Returns None when nothing needs moving, so `sync` can stay quiet.
    """
    try:
        open_at = lines.index(OPEN_HEADING)
        fixed_at = lines.index(FIXED_HEADING)
    except ValueError:
        return None  # a ledger without the two sections is left exactly as it is

    status = {b.line_no: b.status for b in bugs}
    blocks: list[tuple[str, list[str]]] = []
    prose: dict[str, list[str]] = {OPEN_HEADING: [], FIXED_HEADING: []}
    section = None

    i = open_at
    while i < len(lines):
        line = lines[i]
        if line in (OPEN_HEADING, FIXED_HEADING):
            section = line
            i += 1
            continue
        if ENTRY.match(line):
            block, i = _entry_block(lines, i)
            blocks.append((FIXED_HEADING if status.get(i - len(block)) == "x" else OPEN_HEADING, block))
            continue
        # A domain subheading is emitted by this function, so it must not be read
        # back as prose — collecting it would append one copy per run and `sync`
        # would stop being idempotent, which is the exact bug the `---` note below
        # records. Structure out, structure in.
        if SUBHEADING.match(line):
            i += 1
            continue
        # Section prose — the note under each heading. Kept with its heading rather
        # than treated as an entry, so the explanations do not migrate.
        #
        # The rule between the sections is structure, not prose, and skipping it here
        # is load-bearing: collecting it meant assembly wrote a fresh `---` under a
        # `---` it had just kept, so every run added one and `sync` was never
        # idempotent. Caught by running it twice and diffing, which is the only way
        # that class of bug shows up.
        if section is not None and line.strip() and line.strip() != "---":
            prose[section].append(line)
        i += 1

    rebuilt = lines[:open_at]
    for heading in (OPEN_HEADING, FIXED_HEADING):
        rebuilt.append(heading)
        rebuilt.append("")
        if prose[heading]:
            rebuilt.extend(prose[heading])
            rebuilt.append("")
        mine = [b for where, b in blocks if where == heading]
        mine.sort(key=lambda b: _sort_key(b[0]))
        # One subheading per domain, written as the group starts. A heading with
        # nothing under it is impossible by construction, which is the point of
        # emitting it here rather than iterating DOMAINS: a domain with no bugs in
        # this section simply never appears.
        current_domain = None
        for block in mine:
            domain = _sort_key(block[0])[0]
            if domain and domain != current_domain:
                current_domain = domain
                rebuilt.append(f"### {domain}")
                rebuilt.append("")
            rebuilt.extend(block)
            rebuilt.append("")
        if heading == OPEN_HEADING:
            rebuilt.append("---")
            rebuilt.append("")

    while rebuilt and not rebuilt[-1].strip():
        rebuilt.pop()
    return rebuilt if rebuilt != lines else None


def cmd_sync() -> None:
    lines, bugs = parse()
    resolve(bugs)
    changed = [b for b in bugs if b.status != b.mark]
    for bug in changed:
        lines[bug.line_no] = bug.render()
        bug.mark = bug.status  # so relocate sees the new state, not the stale mark

    moved = relocate(lines, bugs)
    if moved is not None:
        lines = moved

    if changed or moved is not None:
        BUGS.write_text("\n".join(lines) + "\n", encoding="utf-8")
        if changed:
            print(f"Updated {len(changed)} of {len(bugs)} bugs:")
            for bug in changed:
                verb = "CLOSED " if bug.status == "x" else "REOPENED"
                print(f"  {verb} {bug.id}  {bug.title}")
                if bug.missing:
                    print(f"           missing: {', '.join(bug.missing)}")
        if moved is not None:
            fixed = sum(1 for b in bugs if b.status == "x")
            print(f"Sorted the ledger — {len(bugs) - fixed} open above the rule, {fixed} fixed below.")
    else:
        print(f"Ledger already current — {len(bugs)} bugs.")


def cmd_check() -> int:
    _, bugs = parse()
    resolve(bugs)
    drifted = [b for b in bugs if b.status != b.mark]
    unverifiable = [b for b in bugs if b.unverifiable]
    wrong_domain = bad_domains(bugs)
    duplicates = duplicate_ids(bugs)
    clashing_questions = duplicate_questions()
    partial = [b for b in bugs if b.partly_resolved]

    # `manual` is matched as EXACTLY ["manual"], so naming it alongside a real
    # anchor silently drops an entry out of every category above: not manual, so
    # it leaves the verify-by-hand list a person reads; and `manual` itself is
    # then looked up as a code symbol, which can never resolve, so the checkbox
    # stays open for an accidental reason rather than a stated one.
    #
    # Found by making the mistake: B30 gained `evidence: PaintingRebuild, manual`
    # when its measurement moved into the bench, and vanished from the report
    # without changing its mark. Nothing was wrong in the file and nothing was
    # reported — the shape of failure this whole script exists to refuse.
    #
    # A supporting measurement belongs in the entry's prose, where B30 now names
    # the scenario in a sentence rather than in an anchor slot.
    mixed_manual = [b for b in bugs if "manual" in b.evidence and not b.manual]

    # The other half of the merge failure, and the half nothing used to look for.
    # Reported here as well as in `ids` so a push to main is checked even when the
    # hook was bypassed — which is exactly the push that matters.
    lost: list[tuple[str, str, str]] = []
    if os.environ.get(ALLOW_DELETION) != "1":
        for spec in merge_parents():
            for name, path, reader in LEDGERS:
                if not path.exists() or (before := text_at(spec, path)) is None:
                    continue
                now = reader(path.read_text(encoding="utf-8"))
                lost += [(name, i, t) for i, t in lost_ids(reader(before), now)]

    open_bugs = [b for b in bugs if b.status != "x"]
    counts = {p: sum(1 for b in open_bugs if b.priority == p) for p in ("P1", "P2", "P3", "P4")}
    print(f"{len(bugs)} bugs — {len(open_bugs)} open "
          f"(P1 {counts['P1']}, P2 {counts['P2']}, P3 {counts['P3']}, P4 {counts['P4']})")

    manual = [b for b in bugs if b.manual and b.status != "x"]
    for bug in manual:
        print(f"  MANUAL       {bug.id}  {bug.title}")
        print("               no headless test can reach it — verify by hand and tick it")
    for bug in unverifiable:
        print(f"  UNVERIFIABLE {bug.id}  {bug.title}")
        print("               no evidence: — name the regression test that closes it")
    for bug in partial:
        print(f"  PARTIAL EVID {bug.id}  {bug.title}")
        print(f"               resolves {', '.join(bug.resolved)} but not {', '.join(bug.missing)}")
        print("               half-landed fix, a bug filed ahead of its tests, or a name that "
              "can never resolve — a private method is invisible here")
    for bug in mixed_manual:
        others = ", ".join(a for a in bug.evidence if a != "manual")
        print(f"  MIXED MANUAL {bug.id}  'manual' named beside {others}")
        print("               'manual' must stand alone or it is treated as a code anchor, "
              "and the entry falls out of every report — put the measurement in the prose")
    for bug in wrong_domain:
        print(f"  BAD DOMAIN   {bug.id}  '{bug.domain}' is not one of {sorted(DOMAINS)}")
    for bug_id, clashing in duplicates.items():
        titles = " / ".join(b.title[:44] for b in clashing)
        print(f"  DUPLICATE ID {bug_id}  used {len(clashing)} times: {titles}")
        print("               renumber all but one — an id is cited from tests and docs")
    for question_id, titles in clashing_questions.items():
        print(f"  DUPLICATE Q  {question_id}  used {len(titles)} times: "
              + " / ".join(t[:40] for t in titles))
        print("               renumber all but one — QUESTIONS.md ids are cited by name")
    for name, entry_id, title in lost:
        print(f"  LOST      {name} {entry_id}  a merge parent has it and this tree does not: "
              f"{title[:50]}")
        print("               taking one side of a ledger conflict drops the other side's entry "
              f"— keep both and renumber, or set {ALLOW_DELETION}=1 if it really goes")
    for bug in drifted:
        want = "fixed" if bug.status == "x" else "open"
        print(f"  DRIFTED      {bug.id}  marked '{bug.mark}' but the code says {want}")
        if bug.missing:
            print(f"               missing: {', '.join(bug.missing)}")

    # `partial` is deliberately absent from this condition — see `partly_resolved`.
    if (drifted or unverifiable or wrong_domain or duplicates or clashing_questions
            or lost or mixed_manual):
        # sync fixes drift and placement; the rest need a person, so say so honestly
        # rather than pointing at a command that will not help.
        if drifted:
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
    elif cmd == "ids":
        sys.exit(cmd_ids(sys.argv[2:]))
    elif cmd == "new":
        sys.exit(cmd_new(sys.argv[2:]))
    elif cmd == "freeid":
        sys.exit(cmd_freeid(sys.argv[2:]))
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
