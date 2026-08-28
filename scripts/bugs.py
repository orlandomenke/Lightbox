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
    selftest        prove ids --fix moves this branch's entry and not the other's
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


QUESTIONS = ROOT / ".claude" / "quality" / "questions"

# ## Q18 · Do flat point arrays cost schema adherence?
QUESTION = re.compile(r"^## (?P<id>Q\d+) · (?P<title>.*?)\s*$")


def question_names(names: list[str]) -> str:
    """The question ledger, as text, from filenames alone.

    **A directory of one file per question is cheaper to gate than the single file
    it replaced, not dearer.** `Q91-ledger-ids-collide.md` carries its id in its
    name, so listing a ref's questions is one `git ls-tree` and no file reads at
    all — where the old ledger had to be fetched and parsed in full for every ref
    compared against. The slug stands in for the title in a report, which is all a
    report needs it for.

    Projected back into the `## Qn · title` shape so that one parser serves the
    directory, a git ref, and the fixture files the tests hand in.
    """
    out = []
    for name in sorted(names):
        stem = Path(name).name.removesuffix(".md")
        entry_id, _, slug = stem.partition("-")
        if re.fullmatch(r"Q\d+", entry_id):
            out.append(f"## {entry_id} · {slug.replace('-', ' ')}")
    return "\n".join(out)


def questions_now() -> str:
    return question_names([p.name for p in QUESTIONS.glob("Q*.md")]) if QUESTIONS.is_dir() else ""


def questions_at(spec: str) -> str | None:
    """The question ledger as of a ref — one git call, whatever the question count."""
    listed = _git("ls-tree", "-r", "--name-only", spec, "--",
                  QUESTIONS.relative_to(ROOT).as_posix())
    return question_names(listed.splitlines()) if listed is not None else None


def duplicate_questions() -> dict[str, list[str]]:
    """The same detector pointed at the questions, because it happened twice.

    `duplicate_ids` above exists because two bugs shared **B39**. Two questions then
    shared **Q19** — "are Linux and macOS shipping targets" and "when a textured line
    is re-shaped, may its texture change" — for exactly the same reason: a new entry
    numbered by eye against a file long enough that the last id is off-screen. It
    happened twice more after that, to Q46 and to Q73-75, and once more to Q87 —
    which reached `main` and left `LedgerGateTests` red on the default branch.

    Checked here rather than in a script of its own: this is the file that already
    knows how to say "renumber all but one", and a second script is a second thing to
    remember to run.
    """
    return duplicates_in(question_ids_in(questions_now()))


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
        done = subprocess.run(("git", *args), cwd=ROOT, capture_output=True,
                              text=True, encoding="utf-8", errors="replace")
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

    **B227: an id that moved is not an id that went.** This compared ids alone,
    so a *renumber* — the entry still here under a different number — looked
    exactly like a deletion. That is not a hypothetical: `ids --fix` renumbers,
    the message printed beside this one recommends renumbering ("keep both and
    renumber"), and the pre-push hook runs the fix for you. So the tool's own
    remedy tripped its own check, and on a merge it tripped it permanently: the
    pre-renumber commit stays a merge parent for ever, so the id is missing from
    every future comparison against it.

    The title is what settles it, and both sides already carry one. An entry
    whose title reappears under another id has moved; only a title that is gone
    as well is a loss. Comparing titles rather than adding a rename ledger keeps
    this working for a renumber done by hand, which is what somebody resolving a
    conflict at 2am will actually do.
    """
    present = {entry_id for entry_id, _ in after}
    # Titles that arrived under an id `before` did not have — a renumber's
    # destination looks exactly like that, and so does a genuinely new entry,
    # which is harmless here: a new entry's title will not match a lost one's.
    moved = {_title_key(title) for entry_id, title in after if entry_id not in {i for i, _ in before}}
    return [
        (i, t) for i, t in before
        if i not in present and _title_key(t) not in moved
    ]


def _title_key(title: str) -> str:
    """A title reduced to what a renumber cannot change."""
    return " ".join(title.lower().split())


@dataclass
class Ledger:
    """One ledger, however it is stored — a file of entries or a directory of them.

    The bugs are lines in `BUGS.md`; the questions are one file each under
    `questions/`. They are gated identically all the same, because the failure
    being gated is about *ids* rather than about storage: two branches allocating
    the same number is the same mistake whether it lands as two lines in one file
    or as two files with the same prefix.

    Everything below therefore works on the ledger *as text* — `## Qn · title` for
    a question, the entry line for a bug — so one parser serves the working tree,
    a git ref, and the fixture a test hands in.
    """

    tag: str            # "ID" / "Q ", the column the report prints in
    noun: str           # bug / question
    prefix: str         # B / Q
    path: Path          # the file, or the directory
    reader: object      # text -> [(id, title)]
    now: object         # () -> text
    at: object          # spec -> text, for a git ref or a fixture path
    move: object        # (moves, occurrences, base) -> what it did

    @property
    def label(self) -> str:
        return self.path.name


def file_ledger(tag: str, noun: str, prefix: str, path: Path) -> Ledger:
    reader = bug_ids_in if prefix == "B" else question_ids_in
    return Ledger(
        tag, noun, prefix, path, reader,
        now=lambda: path.read_text(encoding="utf-8") if path.exists() else "",
        at=lambda spec: text_at(spec, path),
        move=lambda moves, occurrences, base: move_in_file(path, moves, occurrences, base))


def dir_ledger(tag: str, noun: str, prefix: str, path: Path) -> Ledger:
    return Ledger(
        tag, noun, prefix, path, question_ids_in,
        now=questions_now,
        at=lambda spec: (Path(spec).read_text(encoding="utf-8")
                         if Path(spec).exists() else questions_at(spec)),
        move=lambda moves, occurrences, base: move_in_dir(path, moves, occurrences, base))


def ledgers() -> tuple[Ledger, Ledger]:
    return (file_ledger("ID", "bug", "B", BUGS),
            dir_ledger("Q ", "question", "Q", QUESTIONS))


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


def ledger_texts(ledger: Ledger, extra: list[str] | None = None) -> list[str]:
    """This ledger as it reads everywhere it exists — mine first, then everyone's.

    Git is consulted only for a ledger that lives *in this repository*. A fixture
    handed in by a test lives in a temporary directory, so it gets exactly the
    texts the test passes and nothing from this repository's history — the same
    reasoning `cmd_ids` applies to `--ledger`, and the thing that makes the
    allocator testable without building a repository to test it in.
    """
    texts: list[str] = [ledger.now()]
    for spec in extra or []:
        if (text := ledger.at(spec)) is not None:
            texts.append(text)
    if ledger.path.is_relative_to(ROOT):
        for sha in _other_ref_tips():
            if (text := ledger.at(sha)) is not None:
                texts.append(text)
    return texts


def ids_everywhere(ledger: Ledger, extra: list[str] | None = None) -> set[str]:
    return {entry_id
            for text in ledger_texts(ledger, extra)
            for entry_id, _ in ledger.reader(text)}


def next_id(ledger: Ledger, extra: list[str] | None = None) -> str:
    """One above the highest id on any ref, not one above this working tree's."""
    n = len(ledger.prefix)
    numbers = [int(i[n:]) for i in ids_everywhere(ledger, extra) if i[n:].isdigit()]
    return f"{ledger.prefix}{max(numbers, default=0) + 1}"


def fetch_origin() -> str:
    """Refresh the refs the allocator reads, and say what happened either way.

    An allocator is only as fresh as its last fetch, so it does its own — and
    reports rather than fails when it cannot, because filing a bug on a train is
    a reasonable thing to do and refusing to would send the author back to
    counting by eye, which is the failure this exists to end.
    """
    try:
        done = subprocess.run(("git", "fetch", "--quiet", "--no-tags", "origin"),
                              cwd=ROOT, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=45)
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


def merge_in_progress() -> bool:
    """Whether a merge is half-done — MERGE_HEAD written, nothing committed yet."""
    return _git("rev-parse", "--quiet", "--verify", "MERGE_HEAD") is not None


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


def move_ids(moves: dict[str, str], base: str,
             protect: frozenset[tuple[str, int]] = frozenset()) -> list[str]:
    """Rewrite an id everywhere *this branch wrote it*, and nowhere else.

    Scoped to lines the branch added because that is precisely the set of
    citations that belong to the entry being moved. The id it collided with is
    older, and every mention of it in the tree — a test name, a design note, the
    other entry's own line — means the one that is keeping the number. A
    whole-file replace would rewrite those too and point them at the wrong bug,
    which is the failure a renumber is supposed to prevent rather than cause.

    **`protect` is the entry that is keeping the number, and it is not optional.**
    "The other entry is older, so its line is not in the added set" holds for a
    clash between two branches and fails for a duplicate that landed *inside*
    this branch's range — then both entries' lines read as added, the sweep
    rewrites both, and the duplicate survives at a new number instead of being
    repaired. Found on 2026-08-24, with a bug id filed twice on the way: the
    repair announced one renumber, applied it to both entries, and left the
    duplicate standing at the new number — having rewritten six source files to
    get there. The keeper's own line is passed in here so the sweep steps over
    it.

    **What made the base reach that far back is worth knowing, because it is
    ordinary.** `origin/main` was simply stale — a container that cloned once
    and had not fetched since, which is every fresh session. `branch_base()`
    asks `merge-base` against that ref, so a ref 148 commits behind moves the
    line between "mine" and "theirs" back by 148 commits, and everything filed
    in between reads as this branch's own. `cmd_freeid` and `cmd_new` fetch
    before allocating for exactly this reason; the repair does not, so it must
    not depend on the base being tight.

    No id is named above on purpose. This sweep rewrites every citation it finds
    on a line the branch added, and a bare id in this file is a citation as far
    as it is concerned — `cmd_selftest` assembles its ids for the same reason.
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
            if (rel, line_no) in protect:
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


@dataclass
class Occurrence:
    """One entry, and the handle that lets it be moved.

    `where` is a line number in a file ledger and a filename in a directory one.
    `ours` says the branch wrote it, which is what decides who gives up the number.
    """

    where: object
    id: str
    title: str
    ours: bool


def _occurrences(ledger: Ledger, base: str | None) -> list[Occurrence]:
    if ledger.path.is_dir():
        names = sorted(p.name for p in ledger.path.glob("Q*.md"))
        added = _files_added(base, ledger.path) if base else set()
        return [Occurrence(name, name.split("-", 1)[0],
                           name.removesuffix(".md").partition("-")[2].replace("-", " "),
                           name in added)
                for name in names if re.fullmatch(r"Q\d+", name.split("-", 1)[0])]

    pattern = ENTRY if ledger.prefix == "B" else QUESTION
    rel = (ledger.path.relative_to(ROOT).as_posix()
           if ledger.path.is_relative_to(ROOT) else None)
    added = {n for n, _ in added_lines(base).get(rel, [])} if base and rel else set()
    return [Occurrence(n, m["id"], m["title"].strip(), n in added)
            for n, line in enumerate(ledger.now().splitlines(), 1)
            if (m := pattern.match(line))]


def _files_added(base: str, folder: Path) -> set[str]:
    """Files this branch added under a directory — a question filed here, not merged in."""
    listed = _git("diff", "--name-status", "--diff-filter=A", base, "--",
                  folder.relative_to(ROOT).as_posix())
    return {Path(line.split("\t")[-1]).name for line in (listed or "").splitlines() if line}


def _occurrences_to_move(ledger: Ledger, duplicated: dict[str, list[str]],
                         clashed: dict[str, str], base: str | None) -> list[Occurrence]:
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
    spots = _occurrences(ledger, base)
    moving: list[Occurrence] = []
    for entry_id in duplicated:
        same = [s for s in spots if s.id == entry_id]
        ours = [s for s in same if s.ours]
        moving.extend(ours if 0 < len(ours) < len(same) else same[1:])
    for entry_id in clashed:
        # Every occurrence, and that is safe *because* the repair refuses to run
        # mid-merge (see cmd_ids). Outside a merge this only ever finds one: the
        # other side's entry is on a branch that is not checked out, so it is not
        # in the tree to move. Mid-merge it finds both and cannot tell them
        # apart — which is the corruption the guard upstream exists to prevent,
        # and filtering here would not have stopped, since both are marked ours.
        moving.extend(s for s in spots if s.id == entry_id)
    return moving


def _keeping_spots(ledger: Ledger, moving: list[Occurrence],
                   base: str | None) -> frozenset[tuple[str, int]]:
    """Where the entries that KEEP a moving number live, so the sweep can skip them.

    Only the occurrences sharing an id with something that is moving matter: an
    unrelated entry is never rewritten because its id is not in `moves`. For a
    file ledger that is one line; for the questions directory it is every line
    this branch added to the keeper's own file, since its heading carries the id
    too and a rewritten heading disagrees with the filename `questions.py check`
    reads.
    """
    if base is None:
        return frozenset()
    ids = {s.id for s in moving}
    if not ids:
        return frozenset()
    taken = {(s.id, s.where) for s in moving}
    keeping = [s for s in _occurrences(ledger, base)
               if s.id in ids and (s.id, s.where) not in taken]
    if not keeping:
        return frozenset()

    if ledger.path.is_dir():
        folder = ledger.path.relative_to(ROOT).as_posix()
        added = added_lines(base)
        spots = set()
        for spot in keeping:
            rel = f"{folder}/{spot.where}"
            spots.update((rel, n) for n, _ in added.get(rel, []))
        return frozenset(spots)

    rel = ledger.path.relative_to(ROOT).as_posix()
    return frozenset((rel, int(spot.where)) for spot in keeping
                     if isinstance(spot.where, int))


def move_in_file(path: Path, moves: dict[str, str],
                 occurrences: list[Occurrence], base: str | None) -> list[str]:
    """The entry's own line, if the citation pass did not already reach it.

    It will not have when there is no repository to diff against, which is every
    test fixture, and the line must not be left carrying the old number either way.
    """
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    for spot in occurrences:
        n = spot.where
        if isinstance(n, int) and 1 <= n <= len(lines) and re.search(rf"\b{spot.id}\b", lines[n - 1]):
            lines[n - 1] = re.sub(rf"\b{spot.id}\b", moves[spot.id], lines[n - 1])
    path.write_text("".join(lines), encoding="utf-8")
    return []


def move_in_dir(folder: Path, moves: dict[str, str],
                occurrences: list[Occurrence], base: str | None) -> list[str]:
    """Rename the file and rewrite its heading — the two places a question's id lives.

    A rename rather than a line edit, and `git mv` where git will have it, so the
    move reads as a move in the diff rather than as one file deleted and another
    invented. The ledger gate reads the id from the *filename*, so the heading
    following it is not cosmetic: `questions.py check` refuses the two disagreeing.
    """
    said: list[str] = []
    for spot in occurrences:
        old_name = str(spot.where)
        new_id = moves[spot.id]
        source = folder / old_name
        target = folder / f"{new_id}-{old_name.split('-', 1)[1]}"
        if not source.exists():
            continue
        text = source.read_text(encoding="utf-8")
        source.write_text(re.sub(rf"^#\s+{spot.id}\b", f"# {new_id}", text, count=1, flags=re.M),
                          encoding="utf-8")
        if _git("mv", str(source), str(target)) is None:
            source.rename(target)
        said.append(f"    renamed {old_name} -> {target.name}")
    return said


def shared_ids(ledger: Ledger, elsewhere: list[str], base: str | None) -> set[str]:
    """Ids this branch did not allocate: ones it already had when it parted company.

    **The merge-base is the whole idea, and using only the default branch's was
    the bug.** An id both sides carry because it was already on `main` is shared
    rather than clashed — that much was right. But a branch stacked on another
    branch shares every id the branch beneath it filed, and none of those are on
    `main` yet, so the default-branch base cannot see them. The two branches then
    look like independent allocators of the same number.

    B338 is that, observed: B337 was filed on the publish-cycle branch and
    retitled on the branch stacked above it, and `ids --fix` — which the pre-push
    hook runs on its own — renumbered it mid-push. It was uncommitted and caught
    in a diff. Committed, the ledger would have carried two entries for one bug
    with every check green, which is the silent-duplication mirror of the silent
    deletion the branching rules already warn about.

    So the question is asked once per ref rather than once: an id present where
    HEAD parted from **any** ref it is being compared against was inherited, not
    allocated. That is strictly more permissive than before and cannot start
    reporting a clash the old form missed — it only stops reporting ones that
    were never clashes.

    A genuine collision still fires, because two branches that each ran
    `bugs.py new` have no common ancestor holding that id: it is absent from
    every merge-base between them, however recently they diverged.
    """
    bases: list[str] = [base] if base is not None else []
    if ledger.path.is_relative_to(ROOT):
        for spec in [*elsewhere, *_other_ref_tips()]:
            # `elsewhere` may name a file rather than a ref — that is how a test
            # hands in a fixture — and a path has no merge-base. Ask git and let
            # it decline.
            if (found := _git("merge-base", "HEAD", spec)) and (found := found.strip()):
                bases.append(found)

    ids: set[str] = set()
    for spec in bases:
        if (text := ledger.at(spec)) is not None:
            ids |= {entry_id for entry_id, _ in ledger.reader(text)}
    return ids


def clashing_ids(ledger: Ledger, mine: list[tuple[str, str]],
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

    **A title is a claim, and claims get corrected** — which is most of what this
    ledger is for. So the title match alone is not enough to recognise my own
    entry, and B338 is what happens when it is relied on: B337 was filed on one
    branch, its mechanism refuted, and its title corrected on the branch that did
    the measuring. The two branches then disagreed about the title of an entry
    they both already had, and the id read as allocated twice. `shared_ids` is
    the part that settles it without consulting the title at all.
    """
    if base is None and not elsewhere:
        return {}
    base_ids = shared_ids(ledger, elsewhere, base)

    theirs: dict[str, set[str]] = {}
    for text in ledger_texts(ledger, elsewhere)[1:]:
        for entry_id, title in ledger.reader(text):
            theirs.setdefault(entry_id, set()).add(title.strip())

    return {entry_id: title for entry_id, title in mine
            if entry_id not in base_ids
            and entry_id in theirs
            and title.strip() not in theirs[entry_id]}


def renumber(ledger: Ledger, moving: list[Occurrence],
             elsewhere: list[str], base: str | None,
             protect: frozenset[tuple[str, int]] = frozenset()) -> list[str]:
    """Move each occurrence to a fresh id, and take its citations with it.

    The new ids clear every ref rather than just the ledger being repaired —
    renumbering into a number the *next* branch is about to take would be a busy
    way of achieving nothing.
    """
    n = len(ledger.prefix)
    taken = ids_everywhere(ledger, elsewhere) | {s.id for s in moving}
    moves: dict[str, str] = {}
    for spot in moving:
        number = max((int(i[n:]) for i in taken if i[n:].isdigit()), default=0) + 1
        moves[spot.id] = f"{ledger.prefix}{number}"
        taken.add(moves[spot.id])

    said: list[str] = []
    if base is not None:
        said += [f"    moved a citation at {where}"
                 for where in move_ids(moves, base, protect)]
    said += ledger.move(moves, moving, base)
    return [f"  RENUMBERED {old} -> {new}" for old, new in moves.items()] + said


def cmd_selftest() -> int:
    """Prove `ids --fix` repairs a clash without touching the other branch.

    A real repository rather than a stubbed one, because the thing under test
    *is* the git reasoning — what `merge-base` returns, and which files that
    makes "ours". A fixture that mocked those away would have passed on the day
    this broke.

    Two scenarios, and the first is the one that broke it:

    1. **Mid-merge**, the repair must refuse. HEAD is still this branch's last
       commit, so the base predates both sides and the arriving files read as
       this branch's own — renumbering then moves *both* entries and rewrites
       the other one's citations.
    2. **Once the merge is committed**, it must move this branch's entry, leave
       the other side's id alone, and rewrite only this branch's citations.

    The ids are assembled rather than written literally: `ids --fix` rewrites
    every citation it finds in the tree, and a bare `Q7` in this file is a
    citation as far as it is concerned — a clash on that number in the real
    ledger would otherwise silently edit this test.
    """
    import shutil
    import subprocess
    import tempfile

    tag = "Q" + "7"

    def run(cwd, *args):
        subprocess.run(args, cwd=cwd, check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def repair(repo):
        return subprocess.run(
            [sys.executable, str(repo / "scripts" / "bugs.py"), "ids", "--fix"],
            cwd=repo, capture_output=True, text=True,
            encoding="utf-8", errors="replace").stdout

    failures: list[str] = []
    with tempfile.TemporaryDirectory() as tmp:
        repo = Path(tmp)
        run(repo, "git", "init", "-q", "-b", "main")
        run(repo, "git", "config", "user.email", "t@t")
        run(repo, "git", "config", "user.name", "t")
        qdir = repo / ".claude" / "quality" / "questions"
        qdir.mkdir(parents=True)
        (repo / "seed.md").write_text("seed\n", encoding="utf-8")
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "seed")

        # Main files the id and cites it, and edits a file this branch also edits
        # — the conflict is what makes the merge stop half-way.
        (qdir / f"{tag}-theirs.md").write_text(f"# {tag} - theirs\n", encoding="utf-8")
        (repo / "theirs-doc.md").write_text(f"see {tag}\n", encoding="utf-8")
        (repo / "shared.md").write_text("main\n", encoding="utf-8")
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "theirs")

        # This branch, from before that landed, takes the same number.
        run(repo, "git", "checkout", "-q", "-b", "mine", "HEAD~1")
        qdir.mkdir(parents=True, exist_ok=True)
        (qdir / f"{tag}-ours.md").write_text(f"# {tag} - ours\n", encoding="utf-8")
        (repo / "ours-doc.md").write_text(f"see {tag}\n", encoding="utf-8")
        (repo / "shared.md").write_text("mine\n", encoding="utf-8")
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "ours")

        # ROOT comes from the script's own location, so the repair has to run
        # from a copy inside the fixture.
        (repo / "scripts").mkdir(exist_ok=True)
        here = Path(__file__).resolve().parent
        for module in ("bugs.py", "evidence.py"):
            shutil.copy2(here / module, repo / "scripts" / module)
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "tools")

        subprocess.run(["git", "merge", "--no-edit", "main"], cwd=repo,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

        # ---- 1. mid-merge: refuse, and change nothing --------------------------
        out = repair(repo)
        names = sorted(p.name for p in qdir.glob("*.md"))
        if names != [f"{tag}-ours.md", f"{tag}-theirs.md"]:
            failures.append(f"mid-merge repair renamed something: {names}")
        if (repo / "theirs-doc.md").read_text(encoding="utf-8").strip() != f"see {tag}":
            failures.append("mid-merge repair rewrote the other branch's citation")
        if "refusing to renumber" not in out:
            failures.append("mid-merge repair did not say why it stood down")
        print(f"  mid-merge:  {', '.join(names)} — untouched")

        # ---- 2. merge committed: repair this branch's entry only ---------------
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "merge")
        repair(repo)
        names = sorted(p.name for p in qdir.glob("*.md"))
        theirs = (repo / "theirs-doc.md").read_text(encoding="utf-8").strip()
        ours = (repo / "ours-doc.md").read_text(encoding="utf-8").strip()

        if f"{tag}-theirs.md" not in names:
            failures.append(f"the other branch's entry was renamed: {names}")
        if any(n.startswith(f"{tag}-ours") for n in names):
            failures.append(f"this branch's entry kept the clashed id: {names}")
        if theirs != f"see {tag}":
            failures.append(f"the other branch's citation was rewritten: {theirs!r}")
        if ours == f"see {tag}":
            failures.append("this branch's citation was not moved")
        print(f"  committed:  {', '.join(names)} — theirs cites {theirs!r}, ours {ours!r}")

    # ---- 3. both entries inside this branch's own range -----------------------
    #
    # The case scenarios 1 and 2 do not reach, and the one that broke on
    # 2026-08-24. Nothing is mid-merge and nothing belongs to another branch:
    # the base is simply far enough back that BOTH entries read as added here,
    # which is what a branch cut from a default branch that is behind its remote
    # looks like. `move_ids` then rewrote both ledger lines and the duplicate
    # survived at the new number. The repair must move exactly one.
    #
    # A bug ledger rather than the questions directory, because this is the file
    # path: two questions are two files and cannot share a line.
    bug = "B" + "12"
    with tempfile.TemporaryDirectory() as tmp:
        repo = Path(tmp)
        run(repo, "git", "init", "-q", "-b", "main")
        run(repo, "git", "config", "user.email", "t@t")
        run(repo, "git", "config", "user.name", "t")
        ledger_dir = repo / ".claude" / "quality"
        ledger_dir.mkdir(parents=True)
        (ledger_dir / "BUGS.md").write_text("# Bugs\n\n", encoding="utf-8")
        (repo / "scripts").mkdir()
        here = Path(__file__).resolve().parent
        for module in ("bugs.py", "evidence.py"):
            shutil.copy2(here / module, repo / "scripts" / module)
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "seed")

        # On a BRANCH, so the base is the seed rather than HEAD. On `main` the
        # merge base is HEAD itself, nothing reads as added, the citation sweep
        # touches nothing and the repair looks correct however it is written —
        # a fixture that stayed on `main` would pass with the guard removed.
        run(repo, "git", "checkout", "-q", "-b", "mine")

        # Two entries filed one after the other, both after the base, each with a
        # citation of its own — exactly what two merges into a shared branch leave.
        (ledger_dir / "BUGS.md").write_text(
            "# Bugs\n\n"
            f"- [ ] **{bug}** `P2` `brush` first one `evidence: manual`\n"
            f"- [ ] **{bug}** `P1` `ui` second one `evidence: manual`\n",
            encoding="utf-8")
        (repo / "first-doc.md").write_text(f"see {bug}\n", encoding="utf-8")
        (repo / "second-doc.md").write_text(f"see {bug}\n", encoding="utf-8")
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "both filed")

        repair(repo)
        after = (ledger_dir / "BUGS.md").read_text(encoding="utf-8")
        ids = re.findall(r"\*\*(B\d+)\*\*", after)

        if len(ids) != 2:
            failures.append(f"an entry was lost by the repair: {ids}")
        elif ids[0] == ids[1]:
            failures.append(f"the duplicate survived the repair: both are {ids[0]}")
        elif bug not in ids:
            failures.append(f"both entries moved — one must keep the number: {ids}")
        else:
            print(f"  in-range:   {ids[0]} / {ids[1]} — one kept the number, one moved")

    # ---- 4. a stacked branch that RETITLES an inherited entry ----------------
    #
    # B338, and it is the case every earlier scenario is blind to. Nothing is
    # duplicated and nothing collides: one entry, filed on a branch, inherited by
    # a branch stacked on it, and its title corrected there because the
    # measurement refuted what the title claimed. The id is absent from the
    # merge-base with `main` — the branch beneath has not merged — so the only
    # thing left saying "this is my own entry" was the title, and editing a title
    # is most of what this ledger is for.
    #
    # It fired for real on 2026-08-28 and the pre-push hook runs `--fix` on its
    # own, so it renumbered mid-push. Uncommitted, it was caught in a diff;
    # committed, the ledger would have held two entries for one bug with every
    # check green.
    #
    # `--fix` is deliberately NOT used here: the claim is that nothing is
    # reported in the first place. Repairing a clash correctly is scenarios 1-3;
    # this one is about not seeing one.
    inherited = "B" + "20"
    with tempfile.TemporaryDirectory() as tmp:
        repo = Path(tmp)
        run(repo, "git", "init", "-q", "-b", "main")
        run(repo, "git", "config", "user.email", "t@t")
        run(repo, "git", "config", "user.name", "t")
        ledger_dir = repo / ".claude" / "quality"
        ledger_dir.mkdir(parents=True)
        (ledger_dir / "BUGS.md").write_text("# Bugs\n\n", encoding="utf-8")
        (repo / "scripts").mkdir()
        here = Path(__file__).resolve().parent
        for module in ("bugs.py", "evidence.py"):
            shutil.copy2(here / module, repo / "scripts" / module)
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "seed")

        # The lower branch files it. This never reaches `main`, which is the
        # whole point — a stack in flight.
        run(repo, "git", "checkout", "-q", "-b", "lower")
        (ledger_dir / "BUGS.md").write_text(
            "# Bugs\n\n"
            f"- [ ] **{inherited}** `P2` `canvas` the mechanism I guessed at "
            "`evidence: none yet`\n",
            encoding="utf-8")
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "file it")

        # The branch above it corrects the title, having measured.
        run(repo, "git", "checkout", "-q", "-b", "upper")
        (ledger_dir / "BUGS.md").write_text(
            "# Bugs\n\n"
            f"- [ ] **{inherited}** `P2` `canvas` what it turned out to be "
            "`evidence: none yet`\n",
            encoding="utf-8")
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "retitle it")

        report = subprocess.run(
            [sys.executable, str(repo / "scripts" / "bugs.py"), "ids"],
            cwd=repo, capture_output=True, text=True,
            encoding="utf-8", errors="replace").stdout

        if "CLASHES" in report:
            failures.append(
                "a retitled entry inherited from the branch beneath was reported as "
                "a clash — `ids --fix` would renumber it and split one bug in two")
        else:
            print(f"  retitled:   {inherited} inherited and reworded — not a clash")

        # And the guard has to still catch a real one, or it has been widened
        # into uselessness: a second branch off `main` that allocates the same
        # number independently shares no ancestor holding it.
        run(repo, "git", "checkout", "-q", "-b", "unrelated", "main")
        (ledger_dir / "BUGS.md").write_text(
            "# Bugs\n\n"
            f"- [ ] **{inherited}** `P1` `export` a different bug entirely "
            "`evidence: none yet`\n",
            encoding="utf-8")
        run(repo, "git", "add", "-A")
        run(repo, "git", "commit", "-qm", "same number, different bug")

        report = subprocess.run(
            [sys.executable, str(repo / "scripts" / "bugs.py"), "ids"],
            cwd=repo, capture_output=True, text=True,
            encoding="utf-8", errors="replace").stdout

        if "CLASHES" not in report:
            failures.append(
                "two branches allocating the same id independently was NOT reported — "
                "the fix for B338 has widened the guard into silence")
        else:
            print(f"  allocated:  {inherited} taken twice off main — still a clash")

    for line in failures:
        print(f"  FAILED  {line}")
    print("  ids --fix repairs this branch and leaves the other alone" if not failures
          else f"  {len(failures)} failure(s)")
    return 1 if failures else 0


def cmd_ids(argv: list[str]) -> int:
    """Ids only: unique within the file, allocated once, and none dropped by a merge.

    Deliberately separate from `check`, which resolves every evidence anchor
    against the generated code index and rebuilds it when stale. That is the
    right thing for CI and far too slow for a git hook — and the hook is where
    this has to run, because CI only sees a collision after it is published.
    """
    sources = ledgers()
    overrides = {
        "ID": next((a.split("=", 1)[1] for a in argv if a.startswith("--ledger=")), None),
        "Q ": next((a.split("=", 1)[1] for a in argv if a.startswith("--questions=")), None),
    }
    fix = "--fix" in argv
    # **Never repair mid-merge.** Half-way through a merge, HEAD is still this
    # branch's last commit, so the merge base predates both sides — and every
    # file the *other* side is bringing in reads as "added since the base",
    # exactly like this branch's own. Both entries are therefore marked ours,
    # and the repair renumbers both: two entries move to one new id, a fresh
    # duplicate appears, and the other branch's citations are rewritten in files
    # this branch never touched.
    #
    # That is not hypothetical. On 2026-08-19 it renamed both sides of a Q126
    # clash to Q130 and rewrote docs/DESIGN-pen-dynamics.md, which belonged to
    # the other question entirely. Filtering to "ours" does not save it, because
    # mid-merge everything looks like ours — the only safe answer is to wait
    # until the merge is a commit and the two sides can be told apart.
    #
    # The report still runs, because knowing about the clash is what the author
    # needs; it is the rewrite that has to wait.
    if fix and merge_in_progress():
        print("  a merge is in progress — `ids --fix` is refusing to renumber")
        print("    mid-merge every arriving file looks like this branch's own, so the")
        print("    repair would move BOTH sides of the clash and rewrite the other")
        print("    entry's citations. Commit the merge first, then run it again.")
        fix = False
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
    for ledger in sources:
        if (override := overrides[ledger.tag]) is not None:
            ledger = file_ledger(ledger.tag, ledger.noun, ledger.prefix, Path(override))
        if not ledger.path.exists():
            continue
        now = ledger.reader(ledger.now())
        print(f"  {len(now)} {ledger.noun} ids in {ledger.label}")

        duplicated = duplicates_in(now)
        clashed = clashing_ids(ledger, now, elsewhere, base)

        if fix and (duplicated or clashed):
            moving = _occurrences_to_move(ledger, duplicated, clashed, base)
            for line in renumber(ledger, moving, elsewhere, base,
                                 _keeping_spots(ledger, moving, base)):
                print(line)
            now = ledger.reader(ledger.now())
            duplicated, clashed = duplicates_in(now), {}

        for entry_id, titles in duplicated.items():
            problems += 1
            print(f"  DUPLICATE {ledger.tag} {entry_id}  used {len(titles)} times: "
                  + " / ".join(t[:40] for t in titles))
            print("               renumber all but one — an id is cited from tests and docs")

        for entry_id, title in clashed.items():
            problems += 1
            print(f"  CLASHES   {ledger.tag} {entry_id}  another branch already took it: {title[:50]}")
            print("               allocated twice — `bugs.py ids --fix` moves this branch's entry")

        name = ledger.tag
        for spec in against:
            if (before := ledger.at(spec)) is None:
                continue
            for entry_id, title in lost_ids(ledger.reader(before), now):
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
    bugs, questions = ledgers()
    ledger = bugs if which == "bug" else questions
    override = next((a.split("=", 1)[1] for a in argv
                     if a.startswith("--ledger=") or a.startswith("--questions=")), None)
    extra = [a.split("=", 1)[1] for a in argv if a.startswith("--elsewhere=")]
    if override:
        ledger = file_ledger(ledger.tag, ledger.noun, ledger.prefix, Path(override))
    elif "--no-fetch" not in argv:
        print(f"  {fetch_origin()}", file=sys.stderr)
    print(next_id(ledger, extra))
    return 0


def _flag(argv: list[str], names: tuple[str, ...], fallback: str) -> str:
    """`-p P3`, `-p=P3` and `-pP3` all mean the same thing.

    The first of those was refused with "unknown priority ''" (B211), because
    only the joined forms were read — and the space-separated form is the one
    anybody types first. A flag parser that rejects the obvious spelling is a
    command nobody reaches for twice.
    """
    for n, arg in enumerate(argv):
        for name in names:
            if arg == name:
                return argv[n + 1] if n + 1 < len(argv) else ""
            if arg.startswith(f"{name}="):
                return arg.split("=", 1)[1]
            if name.startswith("-") and not name.startswith("--") and arg.startswith(name) and len(arg) > 2:
                return arg[len(name):]
    return fallback


def _positional(argv: list[str], flags: tuple[str, ...]) -> list[str]:
    """The arguments, minus the flags and minus anything a flag consumed."""
    out, skip = [], False
    for arg in argv:
        if skip:
            skip = False
            continue
        if arg in flags:
            skip = True                    # its value is the next argument
        elif not arg.startswith("-"):
            out.append(arg)
    return out


def cmd_new(argv: list[str]) -> int:
    """File a bug, with an id that is allocated rather than counted by eye."""
    priority = _flag(argv, ("-p", "--priority"), "P2")
    evidence = _flag(argv, ("-e", "--evidence"), "")
    positional = _positional(argv, ("-p", "--priority", "-e", "--evidence"))
    if len(positional) < 2:
        print('usage: bugs.py new <domain> "<title>" [-p P2] [-e Test,Other] [--no-fetch]',
              file=sys.stderr)
        return 2
    domain, title = positional[0], positional[1]
    if domain not in DOMAINS:
        print(f"unknown domain {domain!r} — one of: {' '.join(sorted(DOMAINS))}", file=sys.stderr)
        return 2

    if priority not in ("P1", "P2", "P3", "P4"):
        print(f"unknown priority {priority!r} — P1 to P4", file=sys.stderr)
        return 2

    if "--no-fetch" not in argv:
        print(f"  {fetch_origin()}")
    entry_id = next_id(ledgers()[0])
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


def cmd_check(against: list[str] | None = None) -> int:
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
    # Written against `LEDGERS`, a table that never existed — `ledgers()` is
    # the real source — so the first merge-commit HEAD this ran on raised a
    # NameError instead of a report (B214). A crash here is worse than the
    # missed check it replaces: `check` runs in CI, and a PR build checks out
    # a merge commit, which is exactly the HEAD that takes this branch.
    # Explicit refs win (that is what makes this loop testable at all — HEAD
    # is only sometimes a merge); otherwise the parents of HEAD, same as `ids`.
    lost: list[tuple[str, str, str]] = []
    if os.environ.get(ALLOW_DELETION) != "1":
        for spec in against or merge_parents():
            for ledger in ledgers():
                if not ledger.path.exists() or (before := ledger.at(spec)) is None:
                    continue
                current = ledger.reader(ledger.now())
                lost += [(ledger.tag, i, t)
                         for i, t in lost_ids(ledger.reader(before), current)]

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
        sys.exit(cmd_check(sys.argv[2:]))
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
    elif cmd == "selftest":
        return cmd_selftest()
    elif cmd == "stats":
        cmd_stats()
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    sys.exit(main() or 0)
