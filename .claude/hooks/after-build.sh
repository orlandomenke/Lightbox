#!/bin/bash
# After a build or a test run that passed: re-derive the ledgers, and say whether
# this branch would still merge.
#
# WHY THIS EXISTS. Two failures on 2026-08-05, both of the same shape — a fact that
# was knowable, cheap to check, and checked by nobody until it had cost something.
#
#   1. `bugs.py` resolves evidence against `.claude/codemap/map.json`, which is
#      gitignored, so `git switch` never updates it. A `bugs.py check` on a freshly
#      pulled `main` reported one bug fixed that was not and one open that was. The
#      root cause is fixed in `evidence.py`; this makes the re-derivation happen at
#      the moment there is something new to derive from.
#   2. Four pull requests were open at once and two went to conflicts, because the
#      base had moved. Nobody looked, because looking meant remembering to look.
#
# So this runs where the information is freshest: a build or a test just finished,
# which is exactly when the code has changed and when a person is about to decide
# whether the work is done.
#
# NEVER FAILS THE TOOL CALL. A reporting hook that can break a build is a hook
# somebody disables, and then it reports nothing at all. Every path exits 0.
set -uo pipefail

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}" || exit 0

# The hook is handed the tool call as JSON on stdin. Read it once — a second read
# gets nothing, and a hook that blocks on an empty stdin hangs the session.
payload=$(cat 2>/dev/null || true)

# Only builds and test runs. Anything else — a git command, a grep — has not
# changed the code, so there is nothing new to derive and no reason to spend a
# second on it.
python3 - "$payload" <<'PY'
import json, os, re, subprocess, sys

payload = sys.argv[1] if len(sys.argv) > 1 else ""
try:
    data = json.loads(payload) if payload.strip() else {}
except json.JSONDecodeError:
    data = {}

command = ""
response = ""
if isinstance(data, dict):
    command = str((data.get("tool_input") or {}).get("command", ""))
    raw = data.get("tool_response")
    response = raw if isinstance(raw, str) else json.dumps(raw) if raw else ""

# `dotnet build` and `dotnet test`, however they were written — with a project
# path, with -c Release, piped into grep. Substring rather than a strict pattern,
# because the point is to catch every spelling and a missed one is silent.
if not re.search(r"\bdotnet\s+(build|test)\b", command):
    sys.exit(0)

# Never rewrite tracked files in the middle of a merge, rebase or cherry-pick.
# `bugs.py sync` rewrites BUGS.md, and doing that while somebody is resolving a
# conflict in BUGS.md would destroy the resolution — or worse, resolve it silently
# and wrongly. A build during a merge is completely normal (that is how you check
# the merged tree compiles), so this is a real path and not a corner.
git_dir = subprocess.run(
    ["git", "rev-parse", "--git-dir"], capture_output=True, text=True, check=False).stdout.strip()
if git_dir:
    mid_operation = [
        name for name in ("MERGE_HEAD", "REBASE_HEAD", "CHERRY_PICK_HEAD", "rebase-merge", "rebase-apply")
        if os.path.exists(os.path.join(git_dir, name))
    ]
    if mid_operation:
        print(f"after-build: {', '.join(mid_operation)} present — ledgers left alone mid-merge")
        sys.exit(0)

# Success, on the evidence the run itself printed. `Failed!` is xunit's summary
# line and `error CS` is the compiler's; either means there is nothing worth
# re-deriving yet, because the tree does not build or does not pass.
if response and (re.search(r"\bFailed!", response) or re.search(r"\berror CS\d+", response)):
    print("after-build: build or tests are red — ledgers left alone until they pass")
    sys.exit(0)

def run(*args: str) -> tuple[int, str]:
    result = subprocess.run(
        args, capture_output=True, text=True, check=False, timeout=180)
    return result.returncode, (result.stdout + result.stderr).strip()

lines: list[str] = []

# Re-derive, in the order the scripts depend on each other: the index first,
# because both ledgers resolve against it.
_, out = run("python3", "scripts/codemap.py", "build")
_, bugs = run("python3", "scripts/bugs.py", "sync")
_, road = run("python3", "scripts/roadmap.py", "sync")

# Report only what moved. A hook that says "nothing changed" after every test run
# is a hook that gets skimmed, and then the one time it says something real it gets
# skimmed too.
for label, text in (("bugs", bugs), ("roadmap", road)):
    for line in text.splitlines():
        line = line.strip()
        if line.startswith(("CLOSED", "REOPENED")) or " -> " in line:
            lines.append(f"  {label}: {line}")
        elif line.startswith("Updated") and "0 of" not in line:
            lines.append(f"  {label}: {line}")

code, state = run("python3", "scripts/branchstate.py")
if code != 0:
    lines.append(state)

if lines:
    print("after-build:")
    print("\n".join(lines))
PY
exit 0
