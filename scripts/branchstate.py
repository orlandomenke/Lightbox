#!/usr/bin/env python3
"""Where this branch stands against the default branch — divergence and conflicts.

Answers the question nobody asks until a pull request is already red: *would this
merge?* `git` can say so without touching the working tree, and the answer is worth
having while the work is still in your hands rather than in a review.

    python3 scripts/branchstate.py

Exits 1 when the branch would conflict, 0 otherwise, so a hook or a person can
branch on it.

WHY THIS EXISTS. On 2026-08-05 four pull requests were open at once and two went to
conflicts without anybody noticing, because the base moved underneath them. Both
conflicts were in the same two generated files, which is not luck — every branch
regenerates the code index, so **any** two branches that both touched code collide
there whatever they were about. That is the case this reports specially, because it
looks alarming and is a thirty-second fix: regenerate, never hand-merge.
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

# DOMAINS and the entry format come from `bugs.py` rather than being copied, so
# the branch, the ledger entry and `bugs.py mine <domain>` cannot drift apart —
# which is the entire argument for putting the domain in the name. Importing it
# is cheap: `bugs.py` only touches the code index inside `Index.__init__`, and
# nothing here constructs one.
from bugs import BUGS, DOMAINS, ENTRY

ROOT = Path(__file__).resolve().parent.parent

# Regenerated on every branch, so a conflict here means nothing about the work.
DERIVED = {
    ".claude/codemap/INDEX.md",
    ".claude/codemap/FEATURES.md",
    ".claude/codemap/map.json",
}

# The Conventional Commits set, which is what `git-handler.md` already names —
# copied deliberately rather than invented, and `ci` is in it because that
# document says so. A checker that quietly disagrees with the convention it
# enforces is worse than no checker.
TYPES = {"feat", "fix", "perf", "refactor", "docs", "test", "chore", "ci"}

BUG_ID = re.compile(r"\bB\d+\b")

# `claude/codespaces-agentic-setup-fjq295` and its siblings. Provenance rather
# than scope, and the reason the convention exists at all.
#
# The suffix rule wants both a letter and a digit, which is what separates a
# session hash from an ordinary six-letter word. Written as plain length first,
# it fired on `chore/ledger-by-domain`, `…-two-things` and `…-the-ledger` —
# three false positives out of twelve cases, and each one shadowed the message
# that should have been printed instead.
SESSION_PREFIX = re.compile(r"^claude/")
SESSION_SUFFIX = re.compile(r"-(?=[a-z0-9]{6}$)(?=[a-z0-9]*\d)(?=[a-z0-9]*[a-z])[a-z0-9]{6}$")


def git(*args: str) -> str:
    result = subprocess.run(
        ["git", *args], cwd=ROOT, capture_output=True,
        text=True, encoding="utf-8", errors="replace", check=False)
    return result.stdout.strip()


def default_branch() -> str:
    """What `origin/HEAD` records, or main."""
    ref = git("symbolic-ref", "--quiet", "refs/remotes/origin/HEAD")
    return ref.rsplit("/", 1)[-1] if ref else "main"


def ledger_domains() -> dict[str, str]:
    """Bug id to the domain the ledger files it under."""
    if not BUGS.exists():
        return {}
    found = {}
    for line in BUGS.read_text(encoding="utf-8").splitlines():
        if (m := ENTRY.match(line)) is not None:
            found[m.group("id")] = m.group("domain")
    return found


def name_advice(branch: str) -> list[str]:
    """
    What is wrong with this branch's name, or nothing.

    The convention is `<type>/<domain>/<id>-<slug>` for work with a ledger id and
    `<type>/<slug>` for work without one. Both halves earn their place: the type
    says what kind of change it is, and the domain says which part of the
    application is in flight — so four open branches are legible at a glance
    instead of reading `fix/B67-…`, `fix/B62-…`, `fix/B58-…` and saying nothing.

    **Advice, never a failure.** This script's exit code means "would conflict",
    and a name is not a merge hazard. Renaming mid-work also costs a force-push
    and a stale pull request, so the honest weight for a branch already underway
    is a line that says what to do next time.
    """
    if SESSION_PREFIX.search(branch) or SESSION_SUFFIX.search(branch):
        return [f"'{branch}' names the chat that made it, not the objective.",
                "A name that states no objective cannot be departed from, and every one",
                "of these has drifted. Use <type>/<domain>/<id>-<slug>."]

    parts = branch.split("/")
    if len(parts) == 1:
        return [f"'{branch}' has no type. Expected <type>/<slug>, "
                f"one of {'/'.join(sorted(TYPES))}."]

    kind = parts[0]
    if kind not in TYPES:
        return [f"'{kind}/' is not a known type. Expected one of {', '.join(sorted(TYPES))}."]

    ids = BUG_ID.findall(branch)
    if not ids:
        # `<type>/<slug>` is complete on its own. Demanding a domain from work
        # that has none is a rule that gets argued with every time.
        return [] if len(parts) <= 3 else [f"'{branch}' is deeper than <type>/<domain>/<slug>."]

    if len(ids) > 1:
        return [f"'{branch}' names {len(ids)} bugs ({', '.join(ids)}). "
                "A branch is one objective — if the sentence needs an 'and', it is two branches."]

    bug = ids[0]
    if len(parts) < 3:
        filed = ledger_domains().get(bug)
        suggestion = f"{kind}/{filed}/{bug}-<slug>" if filed else f"{kind}/<domain>/{bug}-<slug>"
        return [f"'{branch}' carries {bug} but no domain. Expected {suggestion}."]

    domain = parts[1]
    if domain not in DOMAINS:
        return [f"'{domain}' is not a domain. Use one bugs.py knows: {', '.join(sorted(DOMAINS))}."]

    filed = ledger_domains().get(bug)
    if filed is None:
        return [f"'{branch}' names {bug}, which is not in the ledger. "
                "A branch with an id should have an entry to close."]
    if filed != domain:
        # The check worth having: a plausible domain that is the wrong one sends
        # `bugs.py mine <domain>` looking in the wrong place.
        return [f"{bug} is filed under `{filed}`, not `{domain}`. "
                f"Rename to {kind}/{filed}/{bug}-<slug>, or fix the ledger entry."]
    return []


# Every rule above, with the names that produced it. `selftest` is here rather
# than in a test project because there is no Python suite to put it in, and the
# suffix heuristic already needed it once: written as a bare six-character
# match it fired on `chore/ledger-by-domain` and two others, each false positive
# hiding the message that should have been printed instead.
NAME_CASES: list[tuple[str, str | None]] = [
    ("fix/brush/B39-effect-brush-scratch", None),
    ("perf/canvas/B29-composite-cache", None),
    ("chore/ledger-by-domain", None),            # six-letter last word, not a hash
    ("docs/manual-split-and-wiki", None),
    ("fix/brush/B999-not-in-the-ledger", "not in the ledger"),
    ("claude/codespaces-agentic-setup-fjq295", "names the chat"),
    ("feat/some-work-fjq295", "names the chat"),  # the suffix alone is enough
    ("fix/B58-reach-the-rig-overlay", "no domain"),
    ("fix/ui/B39-effect-brush-scratch", "filed under"),
    ("fix/widgets/B39-something", "is not a domain"),
    ("fix/brush/B39-and-B50-two-things", "names 2 bugs"),
    ("net10-upgrade", "has no type"),
    ("wip/brush/B39-x", "not a known type"),
]


def selftest() -> int:
    """Check `name_advice` against every case, and say which one broke."""
    failures = 0
    for branch, expected in NAME_CASES:
        advice = " ".join(name_advice(branch))
        if expected is None and advice:
            print(f"  FALSE POSITIVE  {branch}\n                  {advice}")
            failures += 1
        elif expected is not None and expected not in advice:
            got = advice or "<silent>"
            print(f"  WRONG           {branch}\n                  wanted '{expected}', got: {got}")
            failures += 1
    print(f"branch names: {len(NAME_CASES) - failures}/{len(NAME_CASES)} cases correct")
    return 1 if failures else 0


def main() -> int:
    if len(sys.argv) > 1 and sys.argv[1] == "selftest":
        return selftest()

    branch = git("rev-parse", "--abbrev-ref", "HEAD")
    base = default_branch()
    if branch in {base, "HEAD"}:
        print(f"branch: on {branch} — nothing to compare")
        return 0

    advice = name_advice(branch)

    remote_base = f"origin/{base}"
    if not git("rev-parse", "--verify", "--quiet", remote_base):
        print(f"branch: no {remote_base} to compare against")
        report_name(advice)
        return 0

    counts = git("rev-list", "--left-right", "--count", f"{remote_base}...HEAD")
    behind, ahead = (counts.split() + ["0", "0"])[:2] if counts else ("0", "0")

    # --write-tree does the whole merge in the object database: no checkout, no
    # index, nothing to clean up if it conflicts. That is what makes this safe to
    # run from a hook while somebody is mid-edit.
    merge = subprocess.run(
        ["git", "merge-tree", "--write-tree", "--name-only", remote_base, "HEAD"],
        cwd=ROOT, capture_output=True,
        text=True, encoding="utf-8", errors="replace", check=False)

    if merge.returncode == 0:
        print(f"branch: {branch} — {ahead} ahead, {behind} behind {base}, merges clean")
        report_name(advice)
        return 0

    # Line one is the tree oid; the conflicted paths follow until a blank line.
    lines = merge.stdout.splitlines()
    paths = []
    for line in lines[1:]:
        if not line.strip():
            break
        paths.append(line.strip())

    derived = [p for p in paths if p in DERIVED]
    real = [p for p in paths if p not in DERIVED]

    print(f"branch: {branch} — {ahead} ahead, {behind} behind {base}, WOULD CONFLICT")
    if real:
        print("  authored files, which need a real decision:")
        for p in real:
            print(f"    {p}")
    if derived:
        print("  generated index, which is never hand-merged — take either side and rebuild:")
        for p in derived:
            print(f"    {p}")
        print("    git checkout --theirs " + " ".join(derived))
        print("    git add .claude/codemap && python3 scripts/codemap.py build")
    print(f"  fix by merging the base in: git merge origin/{base}")
    report_name(advice)
    return 1


def report_name(advice: list[str]) -> None:
    """Silent when the name says what the work is — this runs after every build."""
    if not advice:
        return
    print("  branch name, which is advice rather than a failure:")
    for line in advice:
        print(f"    {line}")


if __name__ == "__main__":
    try:
        sys.exit(main())
    except BrokenPipeError:
        import os
        os._exit(0)
