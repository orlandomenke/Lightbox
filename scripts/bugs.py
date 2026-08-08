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
    ids             ids only: unique, and none lost in a merge. No index, instant
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


def cmd_ids(argv: list[str]) -> int:
    """Ids only: unique within the file, and none dropped by a merge.

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

    # Explicit refs win; otherwise the parents of HEAD, and only when the ledgers
    # are the real ones. An overridden ledger compared against this repository's
    # history would report every id in `main` as lost, which is true of the
    # fixture and says nothing about the tree.
    against = [a for a in argv if not a.startswith("-")]
    if not against and not any(overrides.values()):
        against = merge_parents()

    problems = 0
    for name, (path, reader) in ledgers.items():
        source = Path(overrides[name] or path)
        if not source.exists():
            continue
        now = reader(source.read_text(encoding="utf-8"))
        print(f"  {len(now)} {'bug' if name == 'ID' else 'question'} ids in {source.name}")

        for entry_id, titles in duplicates_in(now).items():
            problems += 1
            print(f"  DUPLICATE {name} {entry_id}  used {len(titles)} times: "
                  + " / ".join(t[:40] for t in titles))
            print("               renumber all but one — an id is cited from tests and docs")

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
    print("  ids unique, none lost" + (f" (against {len(against)} ref(s))" if against else ""))
    return 0


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
