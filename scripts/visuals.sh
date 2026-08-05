#!/usr/bin/env bash
#
# Render the visual contact sheets and say where they are.
#
# These sit beside the pixel tests rather than replacing them: every visual test also asserts, so a
# regression is red whether or not anybody opens the images. What the sheets add is the half a
# number cannot carry — whether a difference of 127/255 on a two-pixel rim is glaring or invisible,
# and whether a wash that measures faint reads as wrong or merely wet.
#
# Usage:
#   scripts/visuals.sh              # everything, into artifacts/visuals
#   scripts/visuals.sh media        # only sheets whose test name matches "media"
#   OUT=/tmp/sheets scripts/visuals.sh
set -euo pipefail

cd "$(dirname "$0")/.."
OUT="${OUT:-$PWD/artifacts/visuals}"
FILTER="${1:-}"

# Cleared first, so what is in the directory afterwards is what this run produced. A stale sheet
# from a build two fixes ago is worse than no sheet: it looks current and disagrees with the code.
rm -rf "$OUT"
mkdir -p "$OUT"

# Both suites: the engine sheets compare a draft against a commit inside BrushEngine, and the app
# sheets go through BeginStroke/MoveStroke/EndStroke and read what the canvas published. Those two
# levels have failed independently — B54 was the engine, B39 was above it — so a sheet from each is
# what says which half moved.
run() {
  local project="$1" name="$2"
  # By trait, not by class name: LiveMatchesCommittedTests writes sheets and is not called
  # "Visual", and filtering on the name silently skipped it — which is the failure mode this whole
  # script exists to avoid, one level up.
  local filter="Category=Visual"
  [ -n "$FILTER" ] && filter="$filter&FullyQualifiedName~$FILTER"
  echo "── $name"
  LIGHTBOX_VISUALS="$OUT" dotnet test "$project" -c Release \
    --filter "$filter" --logger "console;verbosity=detailed" 2>&1 \
    | grep -E "^\s+(Passed|Failed) |px differ|px change|peak|presets drawn|differ," || true
}

run tests/Lightbox.Raster.Tests/Lightbox.Raster.Tests.csproj "engine sheets"
run tests/Lightbox.App.Tests/Lightbox.App.Tests.csproj "canvas sheets"

echo
echo "$(find "$OUT" -name '*.png' | wc -l | tr -d ' ') sheets in $OUT"
find "$OUT" -name '*.png' | sort | sed 's|^|  |'
