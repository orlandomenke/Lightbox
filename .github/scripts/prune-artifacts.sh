#!/usr/bin/env bash
#
# Delete old build artifacts. Shared by the build workflow (which runs it
# before uploading) and the cleanup button (which runs it on demand), so the
# policy is written once and the two cannot drift apart.
#
# Configuration, all through the environment:
#   KEEP          newest bundles to keep per branch. 0 keeps none.
#   STALE_DAYS    also delete feature-branch bundles older than this. 0 skips.
#   BUDGET_MB     ceiling for total artifact storage. 0 disables the rule.
#   HEADROOM_MB   free this much *below* the budget, for an upload about to happen.
#   DRY_RUN       "true" lists without deleting.
#   NAME_PREFIX   only ever touch artifacts whose name starts with this.
#
# Requires gh, and a token with actions:write.
#
# Why there are three rules and not one. KEEP and STALE_DAYS are *policy* —
# they say how much history is worth having, and they keep storage flat once it
# is already sane. Neither can rescue a quota that is already full, which is
# what the first version of this got wrong: it pruned only the branch it was
# running on and only bundles over a week old, so faced with 10.6 GB across 99
# recent bundles from many branches it ran, reported honestly, and deleted
# nothing. BUDGET_MB is the rule that does not care about names, branches or
# ages — it is the one that actually unblocks a build.

set -uo pipefail

: "${KEEP:=3}"
: "${STALE_DAYS:=7}"
: "${BUDGET_MB:=0}"
: "${HEADROOM_MB:=0}"
: "${DRY_RUN:=false}"
: "${NAME_PREFIX:=Lightbox-win-x64-}"

# The inputs may be free text from a workflow_dispatch form. A non-numeric one
# would make awk compare a count against a string and delete the wrong things.
number_or() {
  case "$1" in
    ''|*[!0-9]*) echo "$2" ;;
    *)           echo "$1" ;;
  esac
}
KEEP=$(number_or "$KEEP" 3)
STALE_DAYS=$(number_or "$STALE_DAYS" 7)
BUDGET_MB=$(number_or "$BUDGET_MB" 0)
HEADROOM_MB=$(number_or "$HEADROOM_MB" 0)

say() {
  echo "$1"
  [ -n "${GITHUB_STEP_SUMMARY:-}" ] && printf '%s\n' "$1" >> "$GITHUB_STEP_SUMMARY"
  return 0
}

ALL=$(gh api --paginate "repos/$GITHUB_REPOSITORY/actions/artifacts?per_page=100" \
        --jq '.artifacts[] | select(.expired == false)
              | [.id, .name, .created_at, .size_in_bytes] | @tsv') || {
  echo "could not list artifacts — skipping the prune"
  exit 0
}

# Only ever our own build bundles, so anything a future workflow uploads is
# none of this script's business.
OURS=$(printf '%s\n' "$ALL" | awk -F'\t' -v p="$NAME_PREFIX" 'index($2, p) == 1')

count()  { printf '%s' "$1" | grep -c . || true; }
megs()   { printf '%s\n' "$1" | awk -F'\t' '{s+=$4} END {printf "%.0f", s/1048576}'; }

BEFORE_N=$(count "$OURS")
BEFORE_MB=$(megs "$OURS")
say "Artifact storage in use: **${BEFORE_MB:-0} MB** across ${BEFORE_N} bundles."

if [ "$BEFORE_N" -eq 0 ]; then
  say ""
  say "Nothing to do."
  exit 0
fi

# ---- rule 1 · keep the newest KEEP per branch ---------------------------------
#
# Every branch, not just the one this is running on. Grouping is the artifact
# name minus its trailing SHA, so `…-14vd6m-773e4d5` and `…-14vd6m-4a6a279` are
# one group and a branch name containing dashes still groups correctly.
RULE1=$(printf '%s\n' "$OURS" \
  | awk -F'\t' 'BEGIN{OFS="\t"} { g=$2; sub(/-[^-]*$/, "", g); print g,$1,$2,$3,$4 }' \
  | sort -t$'\t' -k1,1 -k4,4r \
  | awk -F'\t' -v keep="$KEEP" 'BEGIN{OFS="\t"}
      { if ($1 != prev) { n = 0; prev = $1 } n++; if (n > keep) print $2,$3,$4,$5 }')

# ---- rule 2 · stale throwaway bundles ----------------------------------------
#
# Feature branches only. A release or bugfix bundle is one somebody kept on
# purpose, and age alone is not a reason to take it.
RULE2=""
if [ "$STALE_DAYS" -gt 0 ]; then
  CUTOFF=$(date -u -d "-${STALE_DAYS} days" +%Y-%m-%dT%H:%M:%SZ)
  RULE2=$(printf '%s\n' "$OURS" \
    | awk -F'\t' -v c="$CUTOFF" '$2 ~ /-branch-/ && $3 < c')
fi

DOOMED=$(printf '%s\n%s\n' "$RULE1" "$RULE2" | grep . | sort -u -t$'\t' -k1,1n)

# ---- rule 3 · the budget, oldest first ---------------------------------------
#
# The safety valve, and the only rule that can unblock a full quota. It ignores
# names, branches and ages: if what would survive rules 1 and 2 is still over
# the ceiling, keep taking the oldest until it is not.
if [ "$BUDGET_MB" -gt 0 ]; then
  TARGET=$((BUDGET_MB - HEADROOM_MB))
  [ "$TARGET" -lt 0 ] && TARGET=0
  SURVIVORS=$(printf '%s\n' "$OURS" \
    | awk -F'\t' -v d="$DOOMED" 'BEGIN { n=split(d, a, "\n"); for (i=1; i<=n; i++) { split(a[i], f, "\t"); gone[f[1]]=1 } }
        !($1 in gone)')
  SURVIVING_MB=$(megs "$SURVIVORS")
  if [ "${SURVIVING_MB:-0}" -gt "$TARGET" ]; then
    say ""
    say "Rules 1 and 2 leave ${SURVIVING_MB} MB, over the ${TARGET} MB target — taking the oldest until it fits."
    EXTRA=$(printf '%s\n' "$SURVIVORS" | grep . \
      | sort -t$'\t' -k3,3 \
      | awk -F'\t' -v over="$((SURVIVING_MB - TARGET))" 'BEGIN{OFS="\t"}
          { freed += $4 / 1048576; print; if (freed >= over) exit }')
    DOOMED=$(printf '%s\n%s\n' "$DOOMED" "$EXTRA" | grep . | sort -u -t$'\t' -k1,1n)
  fi
fi

if [ -z "$DOOMED" ]; then
  say ""
  say "Nothing matched — already inside the policy."
  exit 0
fi

# ---- delete -------------------------------------------------------------------

freed=0
say ""
if [ "$DRY_RUN" = 'true' ]; then say "### Would delete"; else say "### Deleted"; fi
say ""
say "| Bundle | Created | MB |"
say "| --- | --- | --- |"
while IFS=$'\t' read -r id name created size; do
  [ -n "${id:-}" ] || continue
  mb=$((size / 1048576))
  if [ "$DRY_RUN" != 'true' ]; then
    gh api -X DELETE "repos/$GITHUB_REPOSITORY/actions/artifacts/$id" >/dev/null || {
      echo "  could not delete $name — skipping"
      continue
    }
  fi
  freed=$((freed + mb))
  say "| \`$name\` | $created | $mb |"
done <<< "$DOOMED"

say ""
if [ "$DRY_RUN" = 'true' ]; then
  say "**${freed} MB** would be freed. Nothing was deleted."
else
  say "**${freed} MB** freed, leaving about **$((BEFORE_MB - freed)) MB**."
  say ""
  say "> GitHub recalculates quota usage every 6–12 hours, so a build started right now may still be refused even though the space is genuinely free."
fi
