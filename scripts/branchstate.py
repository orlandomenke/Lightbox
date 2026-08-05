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

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Regenerated on every branch, so a conflict here means nothing about the work.
DERIVED = {
    ".claude/codemap/INDEX.md",
    ".claude/codemap/FEATURES.md",
    ".claude/codemap/map.json",
}


def git(*args: str) -> str:
    result = subprocess.run(
        ["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    return result.stdout.strip()


def default_branch() -> str:
    """What `origin/HEAD` records, or main."""
    ref = git("symbolic-ref", "--quiet", "refs/remotes/origin/HEAD")
    return ref.rsplit("/", 1)[-1] if ref else "main"


def main() -> int:
    branch = git("rev-parse", "--abbrev-ref", "HEAD")
    base = default_branch()
    if branch in {base, "HEAD"}:
        print(f"branch: on {branch} — nothing to compare")
        return 0

    remote_base = f"origin/{base}"
    if not git("rev-parse", "--verify", "--quiet", remote_base):
        print(f"branch: no {remote_base} to compare against")
        return 0

    counts = git("rev-list", "--left-right", "--count", f"{remote_base}...HEAD")
    behind, ahead = (counts.split() + ["0", "0"])[:2] if counts else ("0", "0")

    # --write-tree does the whole merge in the object database: no checkout, no
    # index, nothing to clean up if it conflicts. That is what makes this safe to
    # run from a hook while somebody is mid-edit.
    merge = subprocess.run(
        ["git", "merge-tree", "--write-tree", "--name-only", remote_base, "HEAD"],
        cwd=ROOT, capture_output=True, text=True, check=False)

    if merge.returncode == 0:
        print(f"branch: {branch} — {ahead} ahead, {behind} behind {base}, merges clean")
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
    return 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except BrokenPipeError:
        import os
        os._exit(0)
