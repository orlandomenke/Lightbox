#!/usr/bin/env bash
#
# Publish the manual and the design docs to the GitHub wiki.
#
# The repository stays the source of truth. `CLAUDE.md` makes the manual part
# of the definition of done — a change that alters what an artist sees updates
# the relevant section IN THE SAME COMMIT — and a wiki is a separate git
# repository, so it can never carry that rule. Writing the manual there would
# trade a document that is provably current for one that merely looks tidy.
# This publishes a VIEW instead: readers get the wiki's navigation, and the
# thing being read is generated from files that went through a PR.
#
# Configuration, all through the environment:
#   WIKI_URL      the wiki remote to push to (authenticated).
#   DRY_RUN       "true" reports what would change and pushes nothing.
#
# THE OWNERSHIP RULE, which is the whole safety design.
#
# This script owns exactly three kinds of page: `Manual-*`, `Design-*` and
# `_Sidebar`. It deletes and rewrites those and touches nothing else, so a
# hand-written `Home` — or any other page somebody makes in the wiki UI —
# survives every run untouched. Deleting *our own* pages first is what stops a
# renamed or removed section leaving a ghost behind; deleting anything else
# would make the wiki unusable as a place to write.
#
# It fails loudly on purpose. A sync that breaks quietly produces exactly the
# drift this design exists to avoid, only now with nobody watching for it.
set -euo pipefail

: "${WIKI_URL:?WIKI_URL is required}"
DRY_RUN="${DRY_RUN:-false}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# A wiki repository does not exist until the wiki has one page. Cloning a wiki
# that was never initialised fails with a bare "repository not found", which
# reads as a permissions problem and is not one — so say which it is.
if ! git clone --depth 1 "$WIKI_URL" "$WORK/wiki" 2>"$WORK/clone.err"; then
  echo "Could not clone the wiki." >&2
  sed 's/^/  /' "$WORK/clone.err" >&2
  echo >&2
  echo "If this says 'repository not found', the wiki most likely has no pages" >&2
  echo "yet: a GitHub wiki's git repository is created by saving its first page" >&2
  echo "in the web UI. Create a Home page, then re-run." >&2
  exit 1
fi

cd "$WORK/wiki"

# ---- slugs -----------------------------------------------------------------

# A wiki namespace is FLAT — there are no directories — so the prefix is what
# groups the pages, and it is also what the ownership rule matches on.
slug() {
  printf '%s' "$1" \
    | tr '[:upper:]' '[:lower:]' \
    | sed -e 's/[^a-z0-9]\+/-/g' -e 's/^-//' -e 's/-$//'
}

declare -A CLAIMED=()

claim() {  # claim <page-name> <source-path>
  if [[ -n "${CLAIMED[$1]:-}" ]]; then
    echo "Page name collision: '$1' wanted by both" >&2
    echo "  ${CLAIMED[$1]}" >&2
    echo "  $2" >&2
    echo "Two sources cannot share a wiki page; rename one." >&2
    exit 1
  fi
  CLAIMED[$1]="$2"
}

# ---- clear only what we own ------------------------------------------------

find . -maxdepth 1 -type f \( -name 'Manual-*.md' -o -name 'Design-*.md' -o -name '_Sidebar.md' \) \
  -delete

# ---- the manual ------------------------------------------------------------

MANUAL_NAV=()
for src in "$REPO_ROOT"/docs/manual/*.md; do
  base="$(basename "$src" .md)"                       # 01-first-run
  num="${base%%-*}"                                   # 01
  title="$(sed -n 's/^# \(.*\)$/\1/p' "$src" | head -1)"
  [[ -n "$title" ]] || { echo "No H1 in $src" >&2; exit 1; }
  page="Manual-${num}-$(slug "$title")"
  claim "$page" "$src"
  {
    echo "<!-- Generated from docs/manual/$(basename "$src") — edits here are overwritten. -->"
    echo
    cat "$src"
  } > "${page}.md"
  MANUAL_NAV+=("  - [[${title}|${page}]]")
done

# ---- the design docs -------------------------------------------------------

DESIGN_NAV=()
while IFS= read -r src; do
  base="$(basename "$src" .md)"
  name="${base#DESIGN-}"                              # DESIGN-ai-payload -> ai-payload
  page="Design-$(slug "$name")"
  claim "$page" "$src"
  title="$(sed -n 's/^# \(.*\)$/\1/p' "$src" | head -1)"
  [[ -n "$title" ]] || title="$name"
  {
    echo "<!-- Generated from ${src#$REPO_ROOT/} — edits here are overwritten. -->"
    echo
    cat "$src"
  } > "${page}.md"
  DESIGN_NAV+=("  - [[${title}|${page}]]")
done < <(find "$REPO_ROOT/docs" -maxdepth 2 \( -name 'DESIGN-*.md' -o -path '*/docs/design/*.md' \) | sort)

# ---- the sidebar -----------------------------------------------------------

{
  echo "### Manual"
  printf '%s\n' "${MANUAL_NAV[@]}"
  echo
  echo "### Design notes"
  printf '%s\n' "${DESIGN_NAV[@]}"
  echo
  echo "---"
  echo
  echo "_Generated from the repository. Edit the files under \`docs/\`, not these pages._"
} > _Sidebar.md

# ---- commit only if something moved ---------------------------------------

if [[ -z "$(git status --porcelain)" ]]; then
  echo "Wiki already current — nothing to push."
  exit 0
fi

git status --short

if [[ "$DRY_RUN" == "true" ]]; then
  echo "DRY_RUN — not pushing."
  exit 0
fi

git config user.name  "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git add -A
git commit -m "Publish docs from ${GITHUB_SHA:-local}"
git push
echo "Wiki updated: ${#MANUAL_NAV[@]} manual pages, ${#DESIGN_NAV[@]} design pages."
