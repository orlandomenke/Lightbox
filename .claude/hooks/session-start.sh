#!/bin/bash
# Put the .NET toolchain in the container before an agent needs it.
#
# WHY THIS EXISTS. The remote container ships without .NET, so the first thing
# that ran `dotnet` used to discover that and install it by hand, mid-task.
# That happened for real on 2026-08-04: a performance agent spent a large part
# of a 132k-token run apt-getting two SDKs before it could measure anything.
# An install is not information — nothing about it belongs in an agent's
# context — and it recurs on every fresh container until something else does
# it. That something is this.
#
# The container image is cached after the hook completes, so the cost is paid
# once per container rather than once per session; every later run hits the
# idempotency check below and exits in milliseconds.
set -euo pipefail

# A local machine has its own toolchain and its own opinions about it.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  command -v sudo >/dev/null 2>&1 && SUDO="sudo"
fi

# TWO SDKs, deliberately — the same split .devcontainer/devcontainer.json and
# docs/DESIGN-net10-upgrade.md explain. 10.0 BUILDS the repo, because Avalonia
# 12's source generators need newer Roslyn than the .NET 8 SDK ships. 8.0 RUNS
# it, because every project except Lightbox.Mcp targets net8.0 and nothing sets
# rollForward, so a net8.0 test assembly will not start on a 10.0 runtime.
# Install only one and you get a build that cannot test, or a test host with
# nothing to build it — both of which read as a broken repo rather than a
# missing runtime.
have_build_sdk() { dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; }
have_run_time()  { dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft\.NETCore\.App 8\.'; }

if command -v dotnet >/dev/null 2>&1 && have_build_sdk && have_run_time; then
  echo "toolchain: .NET 10 SDK and .NET 8 runtime already present"
else
  echo "toolchain: installing .NET SDKs 10.0 and 8.0"
  export DEBIAN_FRONTEND=noninteractive
  $SUDO apt-get update -qq
  # Ubuntu 24.04 carries both in its own archive, so no Microsoft feed and no
  # install script is needed.
  #
  # libfontconfig1 is SkiaSharp's native dependency and is present in the base
  # image today. It is named anyway because without it text rendering fails at
  # RUNTIME rather than at build time, which is an expensive way to discover a
  # base-image change.
  $SUDO apt-get install -y -qq --no-install-recommends \
    dotnet-sdk-10.0 dotnet-sdk-8.0 libfontconfig1
  $SUDO rm -rf /var/lib/apt/lists/*
fi

# Warm the NuGet cache into the cached container layer, so the first real
# build in a session is not also the first restore.
#
# Gated on the restore having actually happened, not on the toolchain being
# present, because this hook runs SYNCHRONOUSLY: an unconditional restore is
# ~14 s added to every session start, paid to re-confirm a warm cache. Measured
# at 15.4 s total before this check and well under a second after it.
# `project.assets.json` is what restore writes, so its presence is the fact
# rather than a marker of our own that could drift from it.
cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
if [ ! -f src/Lightbox.Core/obj/project.assets.json ]; then
  echo "toolchain: warming the NuGet cache"
  # Never fatal — a restore that fails offline must not stop a session starting.
  dotnet restore Lightbox.sln >/dev/null 2>&1 || true
fi

echo "toolchain: ready"
