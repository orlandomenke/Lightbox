# Setting up to work on Lightbox

From a machine with nothing on it to a clone that builds, tests and runs.

This is the developer's side of the door. `README.md`'s *Build from source* is
the three-line version for somebody who already has a toolchain; this is the
one that starts from zero and says what breaks.

> **Contributions are not being accepted while Lightbox is alpha** — see
> [`CONTRIBUTING.md`](../CONTRIBUTING.md) for why. This file is here because a
> fork, a bug report with a stack trace, and the owner's own second machine all
> need the same setup, and none of them should have to derive it.

---

## What you actually need

Three things, and only three. Everything else is optional and named further
down.

| | Why it is needed | How it fails without it |
| --- | --- | --- |
| **.NET 10 SDK** — and *only* 10 | Every project targets `net10.0` (`Directory.Build.props`), so the SDK that builds the repo carries the runtime that runs it | Nothing builds |
| **`libfontconfig1`** (Linux only) | SkiaSharp's native dependency | **At runtime, not at build time** — an app that compiled perfectly cannot draw a glyph |
| **Python 3** (standard library only) | `scripts/*.py` — the codemap, the bug ledger, the roadmap, the manual's contents list, the branch state | The tooling the project is steered by is simply absent |

There is no `global.json`, no lockfile and no `NuGet.config`. `dotnet restore`
against the public feed is the whole dependency story, which is why the version
of the SDK is the only thing that has to be right.

**Python needs no `pip install`.** Every script imports from the standard
library and nothing else — deliberately, so that a clone is usable the moment it
lands rather than after a virtualenv.

---

## Per platform

### Windows

The only platform Lightbox has ever actually run on. `README.md` says so in its
alpha banner and it is not false modesty — macOS and Linux are *targeted and
untested*, which is a different thing from supported.

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Python.Python.3.12          # if you do not already have one
winget install Git.Git                     # if you do not already have one
```

No fontconfig, no display server, nothing else. The SDK is also available as an
installer from <https://dotnet.microsoft.com/download/dotnet/10.0> if you would
rather not use winget.

### Linux (Ubuntu 24.04)

```sh
sudo apt-get update
sudo apt-get install -y --no-install-recommends \
    dotnet-sdk-10.0 libfontconfig1 python3
```

**Ubuntu 24.04 carries the .NET 10 SDK in its own archive**, so there is no
Microsoft package feed to add and no `dotnet-install.sh` to run. On another
distribution, or an older Ubuntu, use Microsoft's install script or feed — the
requirement is a 10.0.x SDK, however you get one.

`sudo apt-get install` **without `apt-get update` first** fails in a way that
reads like a network or proxy refusal rather than a stale package index. If you
are behind a proxy that blocks third-party PPAs, `update` will complain loudly
about those while still fetching the main archive successfully — read past the
warnings and check whether the *install* worked.

### macOS

Untested. The SDK installer from
<https://dotnet.microsoft.com/download/dotnet/10.0> is the route, and SkiaSharp
uses CoreText rather than fontconfig so there is no native package to add. What
happens after that is not documented here because nobody has run it; if you try
it, the interesting part is what breaks.

### A container instead

[`.devcontainer/devcontainer.json`](../.devcontainer/devcontainer.json) installs
all of the above and is the least effort of any route. Open the repository in
GitHub Codespaces, or locally in VS Code with the Dev Containers extension, and
skip everything in this section.

---

## Getting the repository

```sh
git clone https://github.com/orlandomenke/lightbox.git
cd lightbox
git config core.hooksPath .githooks
```

**That third line is not optional and it is not in the clone.** `core.hooksPath`
is local configuration, so a fresh clone has it unset and no amount of pulling
will set it. Without it:

- [`.githooks/pre-push`](../.githooks/pre-push) never runs, so a push straight to
  the default branch is not refused. That hook exists because it happened — five
  commits went to `main` directly and moved the base underneath two open pull
  requests, both of which went to conflicts in generated files neither author had
  touched.
- `bugs.py ids` never runs before a push, and a duplicate ledger id reaches
  `main` where other branches rebase onto it. A collision **exists only in the
  merged file**, so CI is structurally too late to be the first line of defence.

The agent session hook sets this automatically, but **only when nothing has set
it already** — `core.hooksPath` replaces the hooks directory wholesale, so
overriding somebody's existing choice would silently disable every hook they
had. If you point it elsewhere, add the guard to your own arrangement.

### Keeping `main` current

The session hook also fast-forwards your local `main` onto `origin/main` at the
start of every agent session, because nothing else is going to. Work lands here
as a pull request merged on GitHub, and git has no client-side event for that —
no hook fires, no ref moves — so a clone sits silently behind until somebody
remembers to fetch. What that costs is not a conflict, which at least announces
itself. It is branching off a stale `main`, reimplementing what landed last
week, and review being where anyone finds out.

It is **fast-forward only**, both when `main` is checked out and when it is not,
so it can never discard a local commit or resolve anything on your behalf. Four
situations it declines, each on one line: a dirty working tree, a `main` that
has diverged from the remote, a clone with no local `main`, and no network. By
hand, the same thing is:

```sh
git fetch --prune origin && git merge --ff-only origin/main
```

Measured at 596/651/633 ms against GitHub with nothing to fetch, which is why it
runs synchronously rather than in the background like the codemap build — the
codemap can afford to be late, a branch point cannot.

### The derived files a clone does not carry

`.claude/codemap/` and `.claude/quality/QUESTIONS.md` are **generated and
untracked, on purpose**: a committed derived file conflicts on every pair of
parallel branches, and GitHub runs no merge driver, so every open pull request
went red the moment any other one merged.

Build them once:

```sh
python3 scripts/codemap.py build      # ~35 s; indexes ~1,160 files
python3 scripts/questions.py build    # instant
```

After that they are refreshed by the tooling. Rebuild the codemap by hand after
a large change.

---

## Verify it works

```sh
dotnet build Lightbox.sln              # everything
dotnet test                            # all four suites, fully headless
dotnet run --project src/Lightbox.App  # launch
```

**`dotnet test` needs no display at all.** `Lightbox.App.Tests` drives Avalonia
through `Avalonia.Headless.XUnit`, so there is no Xvfb, no X server and no
`DISPLAY` in the picture.

What a clean run looks like, measured on a 4-core / 16 GB Linux container. Treat
the durations as advisory rather than a target — they are measuring a container,
and `docs/DESIGN-performance.md` explains why you should read slopes rather than
absolute numbers:

| Suite | Tests | Duration |
| --- | --- | --- |
| `Lightbox.Core.Tests` | 1,334 | 3 s |
| `Lightbox.Raster.Tests` | 795 | 1 m 46 s |
| `Lightbox.Ai.Tests` | 129 | < 1 s |
| `Lightbox.App.Tests` | 3,883 | 9 m 28 s |
| **all four** | **6,141** | **~11 min** |

The Ai suite passes with **no API key set**. Nothing in `dotnet test` reaches a
network or a model — see *Working on the AI features* below for what a key is
actually for.

`dotnet build` is clean apart from a dozen Avalonia XAML warnings
(`AVLN5001` deprecations and `AVLN3001` reachability). Those are not promoted to
errors; C# compiler warnings **are** — `TreatWarningsAsErrors` is set repo-wide
in `Directory.Build.props`, so a stray unused variable fails the build.

---

## Four things that will cost you an afternoon

### 1. Do not install the .NET 8 SDK alongside

It is a tempting thing to do, and the repository used to genuinely need both: 10
to build, because Avalonia 12's source generators want newer Roslyn than the 8
SDK ships, and 8 to run, because the assemblies targeted `net8.0` and nothing set
`RollForward`. Installing one of the two produced a machine that either compiled
or ran, never both.

The `net10.0` migration closed that split deliberately. An 8 SDK now buys
nothing. `docs/DESIGN-net10-upgrade.md` records the migration, and the useful
part of it is the evidence: the render is **bit-identical** across the two
runtimes, pinned by `RuntimeDeterminismTests` against a fingerprint recorded on
.NET 8 before the move.

### 2. A missing `libfontconfig1` is invisible until runtime

It is not a build dependency and nothing checks for it. The symptom is an
application that builds, starts, and cannot render text. If that is what you are
looking at, check this first.

### 3. The App suite sometimes runs short and says it passed

This is **B281**, an open P1: `Lightbox.App.Tests` non-deterministically executes
several hundred fewer tests than exist and still prints `Passed!` with zero
failures. It has been measured at 2,779 of 3,587 on one run and the full count on
the next, same commit both times.

**So read the count, not the word.** `dotnet test --list-tests` gives the number
that should be there. A run that comes up short is not evidence of anything being
fixed or broken — it is this bug, and the entry in
[`BUGS.md`](../.claude/quality/BUGS.md) says what the next investigation step is.

Related but distinct: **B93**, the headless harness intermittently failing a test
at **1 ms** with a native exception from Avalonia's session setup. A one
millisecond failure is a body that never ran, so before theorising about the test
that failed, check its duration.

### 4. Do not run the suite alongside anything heavy

Independent of B281, the App suite under concurrent memory pressure can be killed
outright — observed here as `Test process crashed with exit code 137` (SIGKILL)
after 3,361 of 3,883 tests, while an Avalonia application was running under Xvfb
on the same 16 GB box. Run it on its own and it completes.

The distinction is worth keeping: an OOM kill prints a fatal crash line, and
B281's short runs print nothing at all.

---

## Optional, depending on what you are working on

### Working on the AI features

Set `ANTHROPIC_API_KEY`, or configure a provider in **Edit ▸ Configure ▸ AI**.
Ollama honours `LIGHTBOX_OLLAMA_URL` and `LIGHTBOX_OLLAMA_MODEL` for a fully
offline path.

A key is for *running* AI features by hand. The tests need none.

Any diff touching `src/Lightbox.Ai`, the MCP surface, a prompt, or an AI path in
the view model goes through the **ai-engineer / art-director** pair — charter
gate G12 — and they are meant to disagree. The `ai-work` skill carries the
reasoning, of which the load-bearing fact is that images are ~87% of a request's
bytes and ~5% of its tokens, and strokes are the reverse.

### Running the MCP server

```sh
dotnet run --project src/Lightbox.Mcp
```

For Claude Desktop, point it at a *published* `mcp/Lightbox.Mcp.exe` — see
`README.md`. The failure mode worth knowing in advance is that Claude Desktop
reports nothing at all on a bad `command`: the server never starts and the tools
are simply absent.

### Performance work

```sh
dotnet run --project tools/Lightbox.Bench -c Release
```

Minutes rather than seconds, which is why it is not in `dotnet test`. The budget
tests inside the normal suite are the per-commit ratchet, tagged
`[Trait("Category", "Performance")]` and deliberately loose — they catch
order-of-magnitude regressions, not drift. The bench is the periodic map, and it
writes `.claude/quality/PERFORMANCE.md`.

### Looking at the real window on Linux

```sh
scripts/visuals.sh          # contact sheets, into artifacts/visuals
```

For driving the actual application under Xvfb — and for the reasons synthetic
input through it is unreliable — `MANUAL_TESTING.md` has the recipe and the three
things that cost time to find out.

Prefer a headless pixel test over a screenshot. Tests in
`tests/Lightbox.App.Tests/LivePreviewPixelTests.cs` drive the real
begin/move/end pipeline and inspect the published frame; a dropped synthetic
click looks exactly like a bug.

### The GPU

Neither the suite nor a container can tell you whether the drawing path is using
the graphics card, because a container has no GPU context at all. **Help ▸ Write
a render report** moves that measurement to a machine that has one.
`MANUAL_TESTING.md` lists what to read off it, and the line worth looking at
first when painting feels slow is `durable frame on GPU`.

---

## Before you push

```sh
python3 scripts/branchstate.py     # would this merge?
python3 scripts/bugs.py mine <domain>   # what is broken where you are editing
```

A branch is **one objective**, and its name says which:
`<type>/<domain>/<id>-<slug>` for ledger work, `<type>/<slug>` otherwise. If the
sentence describing the branch needs an "and", it is two branches.

Finished work becomes a pull request — that is the standing route and does not
need asking for. Merging to `main` needs an explicit instruction to merge; the
pre-push hook refuses it otherwise, and `LIGHTBOX_PUSH_TO_MAIN=1` is the escape
hatch for when the answer is genuinely yes.

The `branching` skill carries the incidents behind each of those rules, including
why id allocation lives in a script rather than in whoever read the ledger last.

## Where to look next

| Question | Do this |
| --- | --- |
| Where does X live? | `python3 scripts/codemap.py find X` |
| What is the shape of the codebase? | read `.claude/codemap/INDEX.md` |
| What is known broken? | `python3 scripts/bugs.py next` |
| What should I pick up? | `python3 scripts/roadmap.py next` |
| What does the app do, for an artist? | `python3 scripts/manual.py find X` |

Reading the generated index costs a fraction of exploring the source, and it is
rebuilt automatically when it goes stale. Search it before you reach for `grep`.
