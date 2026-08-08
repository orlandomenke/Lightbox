#!/bin/bash
# Put the .NET toolchain in the container before an agent needs it.
#
# WHY THIS EXISTS. The remote container ships without .NET, so the first thing
# that ran `dotnet` used to discover that and install it by hand, mid-task.
# That happened for real on 2026-08-04: a performance agent spent a large part
# of a 132k-token run apt-getting SDKs before it could measure anything. An
# apt transcript is not information — nothing about it belongs in an agent's
# context — and it recurs on every fresh container until something else does
# it. That something is this.
#
# The container image is cached after the hook completes, so the cost is paid
# once per container rather than once per session; every later run hits the
# idempotency check below and exits in milliseconds.
set -euo pipefail

# Point git at the committed hooks, so .githooks/pre-push is actually consulted.
#
# ABOVE the remote gate below on purpose: the toolchain install is a container
# concern, but the rule that finished work becomes a pull request is not — a local
# clone can push to main just as easily. `.githooks/pre-push` says what that cost.
#
# Only when nothing has set it, because core.hooksPath replaces the hooks
# directory wholesale: setting it over somebody's existing choice would silently
# disable every hook they had. Someone who has already pointed it elsewhere keeps
# their own arrangement, and can add the guard to it themselves.
(
  cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
  if git rev-parse --git-dir >/dev/null 2>&1; then
    existing=$(git config --get core.hooksPath 2>/dev/null || true)
    if [ -z "$existing" ]; then
      git config core.hooksPath .githooks
      echo "git: hooks path set to .githooks (pre-push guards the default branch)"
    fi

    # The codemap merge driver used to be registered here. Retired on
    # 2026-08-08 (Q55) along with the committed INDEX.md/FEATURES.md it
    # resolved: the files are untracked now, so there is nothing to merge.
    # Clean up the config keys it left behind, or git dies on any clone that
    # still has .gitattributes naming a driver whose command is half-unset.
    git config --unset merge.codemap.name 2>/dev/null || true
    git config --unset merge.codemap.driver 2>/dev/null || true
  fi
) || true

# A local machine has its own toolchain and its own opinions about it.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  command -v sudo >/dev/null 2>&1 && SUDO="sudo"
fi

# ONE SDK, and it is 10 — the rule CLAUDE.md states and
# docs/DESIGN-net10-upgrade.md records. Every project targets net10.0, and CI
# installs a single 10.0.x for the same reason.
#
# Do not "helpfully" add dotnet-sdk-8.0 back. The solution used to need both —
# 10 to build, because Avalonia 12's source generators want newer Roslyn than
# the 8 SDK ships, and 8 to run, because the assemblies targeted net8.0 and
# nothing set rollForward. The net10 upgrade closed that split deliberately.
# An 8.0 install now costs an SDK's worth of download for nothing, and a
# runtime check that demands it would never be satisfied — so this hook would
# reinstall on every single session, which is precisely the cost it exists to
# remove.
have_sdk() { dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; }

if command -v dotnet >/dev/null 2>&1 && have_sdk; then
  echo "toolchain: .NET 10 SDK already present"
else
  echo "toolchain: installing the .NET 10 SDK"
  export DEBIAN_FRONTEND=noninteractive
  $SUDO apt-get update -qq
  # Ubuntu 24.04 carries it in its own archive, so no Microsoft feed and no
  # install script.
  #
  # libfontconfig1 is SkiaSharp's native dependency and is present in the base
  # image today. It is named anyway because without it text rendering fails at
  # RUNTIME rather than at build time, which is an expensive way to discover a
  # base-image change.
  $SUDO apt-get install -y -qq --no-install-recommends \
    dotnet-sdk-10.0 libfontconfig1
  $SUDO rm -rf /var/lib/apt/lists/*
fi

# Warm the NuGet cache into the cached container layer, so the first real
# build in a session is not also the first restore.
#
# Gated on the restore having actually happened, not on the toolchain being
# present, because this hook runs SYNCHRONOUSLY: an unconditional restore is
# ~14 s added to every session start, paid to re-confirm a warm cache. Measured
# at 15.4 s total before this check and 13 ms after it. `project.assets.json`
# is what restore writes, so its presence is the fact rather than a marker of
# our own that could drift from it.
cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
if [ ! -f src/Lightbox.Core/obj/project.assets.json ]; then
  echo "toolchain: warming the NuGet cache"
  # Never fatal — a restore that fails offline must not stop a session starting.
  dotnet restore Lightbox.sln >/dev/null 2>&1 || true
fi

echo "toolchain: ready"

# Surface unanswered questions, because a question nobody sees is a guess.
#
# WHY THIS EXISTS. `CLAUDE.md` says to ask a blocking question in the
# conversation, with a recommendation, and write it to the file afterwards —
# "asking first makes the file record an answer; asking after makes it record a
# guess waiting to be corrected." On 2026-08-07 that failed in the way the rule
# was written to prevent: four questions were asked in prose, went unanswered
# twice, and were written to the file anyway. The owner had to say "prompt me
# the questions instead of letting me navigate to the file."
#
# Prose in a long message is skippable. A list at the top of a session is not,
# and it costs one line when the file is clean.
(
  cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
  q=".claude/quality/QUESTIONS.md"
  if [ -f "$q" ]; then
    # A heading is answered when it says so; everything else is still open.
    open=$(grep -E "^## Q[0-9]+ " "$q" | grep -v "answered" || true)
    if [ -n "$open" ]; then
      echo "questions: $(echo "$open" | wc -l | tr -d " ") unanswered — ASK these, do not expect them to be read:"
      echo "$open" | sed "s/^## /  /"
    fi
  fi
)
