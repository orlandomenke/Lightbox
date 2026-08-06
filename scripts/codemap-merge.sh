#!/bin/bash
# Git merge driver for the generated codemap index.
#
# WHY THIS EXISTS. `.claude/codemap/INDEX.md` and `FEATURES.md` are derived from
# the whole solution, so every branch that touches code rewrites them end to end
# and two branches always rewrite the same lines. On 2026-08-06 that stopped a
# 24-commit rebase at nearly every commit, and it had already put two open pull
# requests into conflict on files neither author had touched.
#
# CLAUDE.md has always said the resolution is never a hand-merge — take either
# side and run `codemap.py build`, because the file is derived from the merged
# tree rather than from either parent. That sentence was correct and it was
# being carried out by hand, every time. This is the sentence, executed.
#
# THE ORDER MATTERS. Rebuild first, and fall back to "ours" only if the build
# fails. A build can fail legitimately mid-merge: if source files are still
# conflicted, the parser is reading conflict markers, and an index derived from
# those is worse than a stale one. A stale index is self-healing — the staleness
# check at session start rebuilds it — while a wrong one looks authoritative.
#
# NOT A GITHUB FIX. GitHub does not run custom merge drivers, so a pull request
# whose only conflict is here will still say it has conflicts until somebody
# merges the base in locally. What this removes is the hand-resolution at every
# rebase step, which is where the time actually went.
#
# Registered by .claude/hooks/session-start.sh; declared in .gitattributes.
# Arguments are git's: %O ancestor, %A ours (and the file to write), %B theirs.
set -uo pipefail

ours="${2:?merge driver called without the 'ours' temp file}"
path="${4:-}"

root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
built="$root/$path"

# No path, or a path outside the codemap: leave ours in place rather than
# guessing. Exit 0 — the conflict is resolved, just not by rebuilding.
#
# THE SECOND HALF OF THAT SENTENCE IS THE IMPORTANT ONE, and it has to be a
# check rather than a convention, because the whole file reads as "resolve a
# generated file by regenerating it" and that invites being pointed at the next
# generated-looking file. It must not be. `codemap.py build` writes only the
# files under `.claude/codemap/`; for anything else `$built` is whatever the
# working tree happens to hold mid-merge, and copying that over "ours" would
# silently discard the other side while printing "rebuilt". A conflict a person
# has to resolve is far better than a resolution that threw half of it away.
#
# BUGS.md, ROADMAP.md and MANUAL.md are the ones to say no to by name. They look
# like candidates — all three are kept in step by a script, and all three
# collide on parallel branches — but `bugs.py sync`, `roadmap.py sync` and
# `manual.py sync` *parse* their file and rewrite a checkbox, an ordering or a
# marked block inside it. None of them can reconstruct an entry, so there is
# nothing to rebuild from: two branches that each filed a bug have two authored
# entries that both have to survive, and only a person can say so.
case "$path" in
    .claude/codemap/*) ;;
    *)
        echo "codemap: $path is not a generated codemap file — left for a human" >&2
        exit 0
        ;;
esac

if (cd "$root" && python3 scripts/codemap.py build >/dev/null 2>&1) \
    && [ -s "$built" ]; then
    cp "$built" "$ours"
    echo "codemap: rebuilt $path from the merged tree" >&2
else
    echo "codemap: build unavailable, kept ours for $path — rerun codemap.py build after resolving" >&2
fi

exit 0
