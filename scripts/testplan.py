#!/usr/bin/env python3
"""Work out which tests a change can reach, and split the slow ones up.

`dotnet test Lightbox.sln` is one serial run of four assemblies, and measured on
2026-09-12 (Release, 4 cores) it costs 742 s — of which **Lightbox.App.Tests is
557 s, or 75%**. Two facts follow, and together they decide the shape of this
script:

  * Selecting assemblies by what a change touched saves less than it sounds
    like. App references Core, Raster, Ai, Import and Mcp, so a sound rule can
    skip the expensive assembly only for a change that touches none of them.
  * The expensive assembly is one *process*. `HeadlessSessionSerialisation.cs`
    turns collection parallelism off assembly-wide (B93), and most of its
    classes are `[AvaloniaFact]` classes that serialise on the headless
    dispatcher regardless. Splitting it across processes is the only split that
    sidesteps both — and it sidesteps B93's own mechanism too, because two
    headless sessions on two runners cannot race each other.

So: this emits a **plan** — a list of legs, each a test project and a shard of
it — and CI runs the legs concurrently. Q186 records the measurement and the
decision.

WHAT MAKES THIS SAFE TO TRUST, WHICH IS THE WHOLE PROBLEM

A skipped test reads exactly like a passing one. B269 and B281 are that failure
already — a suite that quietly proved less than it claimed — so a *selector* is
the same class of hazard with a friendlier face. Three properties hold it:

  1. **The graph is exact, not inferred.** Reachability comes from
     `ProjectReference` in the csproj files and from `<Compile Include="..\...">`
     links. It is deliberately NOT taken from `.claude/codemap/map.json`, whose
     edges are regex name-matching built for risk scoring: good enough to say
     "this file is thinly tested", not good enough to decide a test may be
     skipped.
  2. **Everything outside the graph selects everything**, unless a rule in
     `REPO_RULES` below says otherwise — and `audit` proves those rules cover
     what the tests actually read, by resolving the literal paths out of the
     test sources themselves rather than trusting the list.
  3. **The shard partition is total by construction.** The last shard is not a
     list of classes, it is the *complement* of every other shard's list. A
     class this script fails to enumerate therefore lands in the last shard and
     runs anyway, instead of falling down the gap between two include-filters.
  4. **A project no test reaches is still compiled.** `dotnet test
     Lightbox.sln` built the whole solution as a side effect, so a project with
     no tests was kept compiling for free; per-project legs lose that. The plan
     emits a compile-only leg for anything outside every test's closure —
     `tools/Lightbox.Bench` today — derived rather than listed, so the next one
     is covered the day it is added.

Usage
    testplan.py plan [--base REF] [--head REF] [--changed-files PATH|-]
                     [--json] [--github]
        The legs to run. With no base, plans for the whole suite.
    testplan.py explain <path>...
        Why those paths select what they select. For arguing with the rules.
    testplan.py shards <project>
        The shard filters for one project, whatever the change was.
    testplan.py filter <project> <shard>
        One leg's filter and nothing else. What CI's legs ask for; the plan
        deliberately does not carry it, because the complement shard's
        expression alone is 25 KB.
    testplan.py run [--base REF] [--jobs N] [--skip-verify]
        Run the plan locally, legs in parallel, then check each leg ran
        everything it was given. What you want instead of `dotnet test` while
        working.
    testplan.py audit
        Every repo path a test reads, and whether a rule covers it. Exit 1 if
        one is unaccounted for.
    testplan.py selftest
        Feed the rules the cases they exist to get right.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Verdicts a rule can carry, beyond a literal set of test projects.
ALL = "ALL"
NONE = "NONE"

# ---------------------------------------------------------------------------
# How many pieces each test project is cut into.
#
# Measured 2026-09-12, Release, 4 cores: Core 6 s, Ai 4 s, Raster 125 s,
# App 557 s — so App alone is 75% of a 742 s serial run and is the only number
# that matters. Shards are balanced by test count, and the cost of that proxy
# was measured rather than assumed, against the per-class durations in the
# run's own TRX:
#
#   App, 4 shards, balanced by test count        worst leg 179 s
#   App, 4 shards, balanced by measured seconds  worst leg 137 s   (the ideal)
#   App, 6 shards, balanced by test count        worst leg 141 s
#
# Which settles a question worth writing down: **a committed file of measured
# per-class timings would buy 42 s, and one more pair of shards buys the same
# 42 s for nothing.** Stored timings are derived state that every branch would
# rewrite and every merge would conflict over — the reason `.claude/codemap/`
# is not committed — for a saving a free runner already gives. So: counts, and
# more pieces.
#
# Raster at 2 keeps its legs near 62 s, below App's 141 s. Cutting either
# finer buys nothing until the other is cut too, and cutting App finer than
# 6 runs into the fixed ~60 s of checkout, restore and build every leg pays.
#
# These are advisory. Getting them wrong makes CI unbalanced, never wrong,
# because the partition is total whatever the split.
# ---------------------------------------------------------------------------
SHARDS = {
    "Lightbox.App.Tests": 6,
    "Lightbox.Raster.Tests": 2,
}

# ---------------------------------------------------------------------------
# Paths the project graph cannot speak for.
#
# A file under `src/` or `tests/` belongs to a project and the graph decides.
# Everything else — the workflows, the scripts, the ledgers, the documents — is
# decided here, in order, first match winning.
#
# THE DEFAULT IS `ALL`, and nothing may change that. A path nobody wrote a rule
# for runs the whole suite, because the alternative is a new kind of file
# silently testing nothing. `audit` is what keeps the list honest in the other
# direction: it resolves the paths the test sources actually open and reports
# any that a rule does not cover, which is `PublishLayoutTests`' assertion
# generalised — that one was written after PR #95, where a README-only change
# skipped the only test reading the README.
# ---------------------------------------------------------------------------
REPO_RULES: list[tuple[str, object, str]] = [
    # -- source a suite outside its own reference graph reads -----------------
    #
    # These are the edges the project graph structurally cannot see: a test
    # that opens another project's *file* rather than calling its code. The
    # graph says Core.Tests does not reach Lightbox.App, and it is right — but
    # WindowsSubsystemTests asserts on the text of App's Program.cs, so an edit
    # there has to run it. Found by `audit`, not by reading.
    ("src/Lightbox.App/Program.cs", {"Lightbox.Core.Tests"},
     "WindowsSubsystemTests asserts the console is opened from Main"),
    ("src/Lightbox.App/Services/DiagnosticsConsole.cs", {"Lightbox.Core.Tests"},
     "WindowsSubsystemTests asserts what that console does on Windows"),
    ("src/Lightbox.App/Lightbox.App.csproj", {"Lightbox.Core.Tests"},
     "CiRuntimeTests derives the runtime CI must offer from its TargetFramework, "
     "and WindowsSubsystemTests reads its OutputType"),

    # -- test inputs that happen to be spelled as configuration ---------------
    (".github/workflows/*.yml", {"Lightbox.Core.Tests"},
     "CiRuntimeTests, CiDraftGateTests, CiTestCountTests and PublishLayoutTests read the workflows"),
    (".githooks/*", {"Lightbox.Core.Tests"},
     "LedgerGateTests reads .githooks/pre-push"),
    (".gitignore", {"Lightbox.Core.Tests"},
     "LedgerGateTests checks what the index is allowed to carry"),
    # The selector cannot be trusted to select its own change. A `scripts/*`
    # rule would run only Core.Tests for an edit to this file — including an
    # edit that narrows selection wrongly, which would then be checked by the
    # narrowed selection it introduced. Same for the guard that proves a leg ran
    # everything it was given. Both are edited rarely; both decide whether every
    # other answer here means anything.
    ("scripts/testplan.py", ALL, "a change to the selector runs everything, including itself"),
    ("scripts/testcount.py", ALL, "a change to the guard that proves a leg ran its slice"),

    ("scripts/*", {"Lightbox.Core.Tests"},
     "LedgerGateTests drives bugs.py and questions.py; CiTestCountTests and "
     "PublishLayoutTests read testcount.py and get-build.ps1"),

    # -- test inputs that happen to be spelled as documentation ---------------
    ("README.md", {"Lightbox.Core.Tests"},
     "PublishLayoutTests asserts it names the folder the MCP server ships in (PR #95)"),
    ("MANUAL_TESTING.md", {"Lightbox.Core.Tests"},
     "PublishLayoutTests asserts it names that folder too"),
    (".claude/quality/ratchets/*", {"Lightbox.App.Tests"},
     "MonolithRatchetTests reads every budget in that directory and checks it "
     "against the file it governs"),
    (".claude/quality/BUGS.md", {"Lightbox.Core.Tests"},
     "LedgerGateTests runs the ledger's own gate over it"),
    (".claude/quality/questions/*", {"Lightbox.Core.Tests"},
     "LedgerGateTests runs questions.py check over the directory"),
    (".claude/quality/DESIGN.md", {"Lightbox.App.Tests"},
     "DensityScaleTests reads its size table and asserts Density.axaml applies it"),
    (".claude/codemap/INDEX.md", {"Lightbox.Core.Tests"},
     "LedgerGateTests resolves evidence anchors against the committed index"),
    (".claude/codemap/FEATURES.md", {"Lightbox.Core.Tests"},
     "LedgerGateTests reads the behaviour inventory out of it"),

    # -- the build itself -----------------------------------------------------
    ("Directory.Build.props", ALL, "it sets the target framework for every project"),
    ("*.sln", ALL, "a project added to or removed from the solution changes every run"),

    # -- genuinely inert ------------------------------------------------------
    ("docs/*", NONE, "no test opens anything under docs/"),
    ("packaging/*", NONE, "desktop entries and icons, read by no test"),
    (".claude/codemap/*", NONE,
     "the rest of the index is generated and not committed; the two committed "
     "files are named above, and order decides"),
    (".claude/*", NONE,
     "the rest of the quality material is prose; the parts a test reads are named above"),
    ("*.md", NONE, "a root-level document, other than the two named above"),
]


# ---------------------------------------------------------------------------
# The project graph
# ---------------------------------------------------------------------------

@dataclass
class Project:
    name: str
    csproj: str                       # repo-relative
    directory: str                    # repo-relative, trailing slash
    references: set[str] = field(default_factory=set)
    linked: set[str] = field(default_factory=set)   # files compiled in from elsewhere
    is_test: bool = False


def _rel(path: Path) -> str:
    return path.resolve().relative_to(ROOT).as_posix()


def load_projects(root: Path = ROOT) -> dict[str, Project]:
    """Every csproj in the tree, with its references and its linked-in files."""
    projects: dict[str, Project] = {}
    for csproj in sorted(root.glob("**/*.csproj")):
        if any(part in ("obj", "bin") for part in csproj.parts):
            continue
        name = csproj.stem
        text = csproj.read_text(encoding="utf-8", errors="ignore")
        project = Project(
            name=name,
            csproj=csproj.resolve().relative_to(root).as_posix(),
            directory=csproj.parent.resolve().relative_to(root).as_posix() + "/",
            is_test=csproj.resolve().relative_to(root).as_posix().startswith("tests/"),
        )
        for include in re.findall(r'<ProjectReference\s+Include="([^"]+)"', text):
            project.references.add(Path(include.replace("\\", "/")).stem)
        # A file compiled in from another directory belongs to this project as
        # well as to the one it lives in — App.Tests links VisualSheet.cs and
        # the PSD fixture builders out of Raster.Tests, so editing those has to
        # rebuild and re-run both.
        for include in re.findall(r'<Compile\s+Include="([^"]+)"', text):
            if ".." not in include:
                continue
            resolved = (csproj.parent / include.replace("\\", "/")).resolve()
            try:
                project.linked.add(resolved.relative_to(root).as_posix())
            except ValueError:
                continue
        projects[name] = project
    if not projects:
        sys.exit("no csproj files found — is this the repository root?")
    return projects


def reach(name: str, projects: dict[str, Project], seen: set[str] | None = None) -> set[str]:
    """`name` plus every project it transitively references."""
    seen = seen if seen is not None else set()
    if name in seen or name not in projects:
        return seen
    seen.add(name)
    for reference in projects[name].references:
        reach(reference, projects, seen)
    return seen


def test_projects(projects: dict[str, Project]) -> list[str]:
    return sorted(p.name for p in projects.values() if p.is_test)


def owners(path: str, projects: dict[str, Project]) -> set[str]:
    """Which projects a repo-relative path belongs to.

    Usually one — the project whose directory contains it. A linked file
    belongs to the linking project too, which is the edge the directory alone
    cannot see.
    """
    found = {p.name for p in projects.values()
             if path.startswith(p.directory) or path == p.directory.rstrip("/")}
    found |= {p.name for p in projects.values() if path in p.linked}
    return found


# ---------------------------------------------------------------------------
# Selection
# ---------------------------------------------------------------------------

def rule_for(path: str) -> tuple[object, str] | None:
    """The first REPO_RULES entry matching a path, or None for no rule.

    A pattern ending `/*` matches the whole subtree, not just one level —
    `docs/*` has to cover `docs/design/ui-reference.png` or the rule is a
    decoration.
    """
    for pattern, verdict, why in REPO_RULES:
        if fnmatch.fnmatchcase(path, pattern):
            return verdict, why
        if pattern.endswith("/*") and (path.startswith(pattern[:-1]) or path == pattern[:-2]):
            return verdict, why
    return None


def changed_projects(changed: list[str], projects: dict[str, Project]) -> set[str]:
    """Every project a change touched, whether or not a test reaches it."""
    touched: set[str] = set()
    for path in changed:
        touched |= owners(path.strip(), projects)
    return touched


def select(changed: list[str], projects: dict[str, Project]) -> tuple[set[str], list[str]]:
    """The test projects a change can reach, and the reasoning for each path."""
    tests = test_projects(projects)
    reachable = {name: reach(name, projects) for name in tests}
    selected: set[str] = set()
    notes: list[str] = []

    for path in changed:
        path = path.strip()
        if not path:
            continue

        # The graph first: a file in a project selects every test project that
        # can reach that project. Exact, and it needs no rule.
        owning = owners(path, projects)
        hit = {t for t in tests if reachable[t] & owning} if owning else set()

        # Then the rules, ADDED to that rather than instead of it. A test may
        # read a file belonging to a project it does not reference at all —
        # WindowsSubsystemTests is in Core.Tests and reads
        # src/Lightbox.App/Program.cs — and the graph cannot see that edge, so
        # the rule has to be able to widen the graph's answer.
        decided = rule_for(path)
        if decided is None:
            if owning:
                selected |= hit
                notes.append(f"{path}: in {', '.join(sorted(owning))} -> "
                             f"{', '.join(sorted(hit)) or 'nothing'}")
            else:
                # Outside every project and covered by no rule. The only safe
                # answer, and the one build.yml has always given a change it
                # could not classify.
                selected |= set(tests)
                notes.append(f"{path}: NO RULE -> everything (add one to REPO_RULES if that is wrong)")
            continue

        verdict, why = decided
        if verdict is ALL:
            selected |= set(tests)
            notes.append(f"{path}: rule says everything — {why}")
        elif verdict is NONE:
            selected |= hit
            where = f"in {', '.join(sorted(owning))} -> {', '.join(sorted(hit))}; " if hit else ""
            notes.append(f"{path}: {where}rule adds nothing — {why}")
        else:
            selected |= hit | set(verdict)
            notes.append(f"{path}: {'graph ' + ', '.join(sorted(hit)) + ' plus ' if hit else ''}"
                         f"rule {', '.join(sorted(verdict))} — {why}")

    return selected, notes


# ---------------------------------------------------------------------------
# Sharding
# ---------------------------------------------------------------------------

TEST_ATTRIBUTE = re.compile(r"\[\s*(?:Avalonia)?(?:Fact|Theory)\s*[\](]")
NAMESPACE = re.compile(r"^\s*namespace\s+([A-Za-z0-9_.]+)", re.MULTILINE)
CLASS = re.compile(
    r"^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*class\s+([A-Za-z0-9_]+)",
    re.MULTILINE,
)


def test_classes(project: Project, root: Path = ROOT) -> dict[str, int]:
    """Fully-qualified test classes in a project, and how many tests each holds.

    Static, because the plan is computed before anything is built. It does not
    have to be complete: the last shard is a complement, so a class missed here
    still runs. It only has to be roughly right, or the shards are uneven.
    """
    found: dict[str, int] = {}
    sources = [p for p in (root / project.directory).glob("**/*.cs")
               if not any(part in ("obj", "bin") for part in p.parts)]
    sources += [root / linked for linked in sorted(project.linked)]
    for source in sources:
        if not source.exists():
            continue
        text = source.read_text(encoding="utf-8", errors="ignore")
        namespace_match = NAMESPACE.search(text)
        if not namespace_match:
            continue
        namespace = namespace_match.group(1)
        # Split on class declarations so each class is counted with its own
        # attributes rather than the file's.
        positions = [(m.start(), m.group(1)) for m in CLASS.finditer(text)]
        for i, (start, name) in enumerate(positions):
            end = positions[i + 1][0] if i + 1 < len(positions) else len(text)
            count = len(TEST_ATTRIBUTE.findall(text[start:end]))
            if count:
                found[f"{namespace}.{name}"] = found.get(f"{namespace}.{name}", 0) + count
    return found


PERFORMANCE_TRAIT = re.compile(r'\[\s*Trait\(\s*"Category"\s*,\s*"Performance"\s*\)\s*\]')


def performance_classes(project: Project, root: Path = ROOT) -> set[str]:
    """Test classes holding at least one performance-tagged test.

    WHY THE PLAN HAS TO KNOW. The budgets in `[Trait("Category",
    "Performance")]` measure wall clock, so they lie under load — and a tool
    that runs several legs at once on one machine *is* load.
    `LargeCanvasPerformanceTests.FourK_WholeStrokeIncludingCommit_HasNoPenLiftStall`
    failed exactly that way on the first parallel local run of this script,
    with nothing wrong with the code. `docs/DEVELOPING.md` has warned since
    B281 not to run the suite alongside anything heavy; this would have made
    the suite the heavy thing.

    On CI it does not arise, because every leg has a runner to itself. It
    arises locally, which is where the script is used most, so these classes are
    gathered into one leg per project and that leg is run on its own.

    A class the scan misses is treated as ordinary and may run in parallel —
    that is a flaky measurement, not a skipped test, and the partition is
    unaffected either way.
    """
    found: set[str] = set()
    sources = [q for q in (root / project.directory).glob("**/*.cs")
               if not any(part in ("obj", "bin") for part in q.parts)]
    sources += [root / linked for linked in sorted(project.linked)]
    for source in sources:
        if not source.exists():
            continue
        text = source.read_text(encoding="utf-8", errors="ignore")
        if not PERFORMANCE_TRAIT.search(text):
            continue
        namespace_match = NAMESPACE.search(text)
        if not namespace_match:
            continue
        positions = [(m.start(), m.group(1)) for m in CLASS.finditer(text)]
        for i, (start, name) in enumerate(positions):
            end = positions[i + 1][0] if i + 1 < len(positions) else len(text)
            # The attribute may sit above the class as well as on a method
            # inside it, so look a little before the declaration too.
            window = text[max(0, start - 400):end]
            if PERFORMANCE_TRAIT.search(window) and TEST_ATTRIBUTE.search(text[start:end]):
                found.add(f"{namespace_match.group(1)}.{name}")
    return found


def balance(weights: dict[str, int], buckets: int) -> list[list[str]]:
    """Longest-processing-time first: the heaviest class into the lightest bucket."""
    if buckets <= 1:
        return [sorted(weights)]
    loads = [0] * buckets
    groups: list[list[str]] = [[] for _ in range(buckets)]
    for name in sorted(weights, key=lambda n: (-weights[n], n)):
        lightest = loads.index(min(loads))
        loads[lightest] += weights[name]
        groups[lightest].append(name)
    # Heaviest bucket first, so the *lightest* becomes the complement shard —
    # it is the one that also picks up anything the enumeration missed.
    order = sorted(range(buckets), key=lambda i: (-loads[i], i))
    return [sorted(groups[i]) for i in order]


def shard_filters(project: Project, count: int) -> list[str]:
    """One VSTest filter per shard. The last is the complement of the rest.

    That is the property that makes this safe rather than merely convenient: a
    class the static enumeration above never saw is excluded by no term, so it
    falls into the complement and runs. The union of the filters is the whole
    assembly whatever the enumeration got wrong.

    Neither form mixes `&` with `|`, because VSTest's filter grammar has no
    parentheses and mixing the two is where that bites.
    """
    if count <= 1:
        return [""]
    classes = test_classes(project)
    timed = {name for name in performance_classes(project) if name in classes}

    if timed and count >= 2:
        # Shard 1 is every timed class, so exactly one leg per project has to
        # be run on its own. The rest are balanced across what is left, and the
        # last is still the complement of all of them — so the partition is
        # total exactly as before, and an unenumerated class still runs.
        rest = {name: weight for name, weight in classes.items() if name not in timed}
        groups = [sorted(timed)] + balance(rest, count - 1)
    else:
        groups = balance(classes, count)

    filters = ["|".join(f"FullyQualifiedName~{name}." for name in group) for group in groups[:-1]]
    named = [name for group in groups[:-1] for name in group]
    complement = "&".join(f"FullyQualifiedName!~{name}." for name in sorted(named))
    return filters + [complement]


def matches_filter(expression: str, name: str) -> bool:
    """Evaluate a VSTest `FullyQualifiedName` filter against one test name.

    THE GUARD NEEDS THIS BECAUSE VSTEST WILL NOT DO IT. `dotnet test
    --list-tests --filter …` reports "No test matches" for a filter that the
    very same command *executes* four tests under — discovery and execution do
    not agree about what a fully-qualified name is. So `testcount.py` discovers
    unfiltered and applies the filter here instead.

    That is better than a workaround. The guard now compares what the runner
    reported against what this script believed it asked for, so the two
    understandings of a filter are checked against each other on every run —
    and a shard whose filter means something different to VSTest than it means
    here fails loudly rather than quietly running less.

    Only the two operators the planner emits are accepted, and `&` is never
    mixed with `|` in what it emits, so there is no precedence to get wrong.
    Anything else raises rather than guessing: a filter this cannot read is a
    filter whose coverage nobody can vouch for.
    """
    expression = expression.strip()
    if not expression:
        return True
    if "|" in expression and "&" in expression:
        raise ValueError(f"filter mixes & and |, which has no defined precedence here: {expression!r}")
    if "|" in expression:
        return any(matches_filter(term, name) for term in expression.split("|"))
    if "&" in expression:
        return all(matches_filter(term, name) for term in expression.split("&"))
    if expression.startswith("FullyQualifiedName!~"):
        return expression[len("FullyQualifiedName!~"):] not in name
    if expression.startswith("FullyQualifiedName~"):
        return expression[len("FullyQualifiedName~"):] in name
    raise ValueError(f"unreadable filter term: {expression!r}")


def uncovered(projects: dict[str, Project]) -> list[str]:
    """Projects no test leg would ever compile.

    `dotnet test Lightbox.sln` built the whole solution as a side effect, so a
    project with no tests was still kept compiling by the suite. `dotnet test
    <csproj>` builds only that project's closure, so the moment the suite
    becomes a set of per-project legs, anything outside every test's closure
    stops being built at all — and a compile error in it reaches the default
    branch with every check green.

    Today that is `tools/Lightbox.Bench`, which references Core, Raster and App
    and is referenced by nothing. Found by asking rather than by noticing, and
    the answer is derived rather than a list, so the next such project is
    covered the day it is added.
    """
    covered: set[str] = set()
    for name in test_projects(projects):
        covered |= reach(name, projects)
    return sorted(name for name in projects if name not in covered)


def build_legs(touched: set[str] | None, projects: dict[str, Project]) -> list[dict]:
    """Compile-only legs, for the projects nothing tests.

    Selected on the same reachability the test legs use: a change to
    `Lightbox.Core` can break `Lightbox.Bench` without touching its directory.

    `None` means "no usable base, plan everything" and `set()` means "this
    change touched no project at all". Collapsing the two gave a
    documentation-only change a compile leg — caught by the selftest, which is
    the entire reason it enumerates the boring cases.
    """
    plan = []
    for name in uncovered(projects):
        if touched is None or touched & reach(name, projects):
            plan.append({
                "project": name,
                "csproj": projects[name].csproj,
                "kind": "build",
                "shard": 1,
                "shards": 1,
                "performance": False,
                "name": f"{name} (compile only)",
            })
    return plan


def legs(selected: set[str], projects: dict[str, Project],
         changed_projects: set[str] | None = None) -> list[dict]:
    """The plan: one entry per shard of each selected project, plus compiles."""
    plan = []
    for name in sorted(selected):
        count = SHARDS.get(name, 1)
        timed = bool(performance_classes(projects[name])) and count >= 2
        for index in range(1, count + 1):
            # Shard 1 carries the timed tests when there is more than one shard.
            # CI gives every leg its own runner so it makes no difference there;
            # `run` uses it to keep those tests off a loaded machine.
            performance = timed and index == 1
            plan.append({
                "project": name,
                "csproj": projects[name].csproj,
                "kind": "test",
                "shard": index,
                "shards": count,
                "performance": performance,
                "name": (name if count == 1 else f"{name} {index}/{count}")
                        + (" [timed]" if performance else ""),
            })
    plan += build_legs(changed_projects, projects)
    return plan


# ---------------------------------------------------------------------------
# Working out what changed
# ---------------------------------------------------------------------------

def changed_files(base: str | None, head: str | None) -> list[str] | None:
    """The paths a change touched, or None when there is no usable base.

    None means "test everything", and it is the same judgement `build.yml` has
    always made for a new branch or a force push: skipping a suite somebody
    needed is a worse failure than running one they did not.

    **With no explicit head, this compares the base against the WORKING TREE**,
    untracked files included — because the local question is "what have I
    changed", and the answer that ignores what is not committed yet is the one
    that runs nothing after an edit and looks broken. CI does not come through
    here: it computes its own diff between two commits and passes the result to
    `--changed-files`, so the two-ref form stays exact where exactness is what
    is wanted.
    """
    if not base:
        return None
    probe = subprocess.run(["git", "cat-file", "-e", f"{base}^{{commit}}"],
                           cwd=ROOT, capture_output=True)
    if probe.returncode != 0:
        return None

    # utf-8 named, not assumed: a text-mode pipe otherwise decodes with the
    # locale codec, and a path with anything past ASCII in it then raises or
    # blocks. The ledger gate checks every such call in scripts/.
    def git(*args: str) -> list[str] | None:
        done = subprocess.run(["git", *args], cwd=ROOT, capture_output=True,
                              text=True, encoding="utf-8", errors="replace")
        if done.returncode != 0:
            return None
        return [line for line in done.stdout.splitlines() if line.strip()]

    if head:
        return git("diff", "--name-only", base, head)

    tracked = git("diff", "--name-only", base)
    untracked = git("ls-files", "--others", "--exclude-standard")
    if tracked is None or untracked is None:
        return None
    return sorted(set(tracked) | set(untracked))


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

def cmd_plan(args) -> int:
    projects = load_projects()
    tests = set(test_projects(projects))

    touched: set[str] | None = None
    if args.changed_files:
        source = sys.stdin if args.changed_files == "-" else open(args.changed_files)
        with source as handle:
            changed = [line.strip() for line in handle if line.strip()]
        selected, notes = select(changed, projects)
        touched = changed_projects(changed, projects)
    elif args.base:
        changed = changed_files(args.base, args.head)
        if changed is None:
            selected, notes = tests, ["no usable base ref — testing everything"]
        else:
            selected, notes = select(changed, projects)
            touched = changed_projects(changed, projects)
    else:
        selected, notes = tests, ["no base given — planning the whole suite"]

    plan = legs(selected, projects, touched)

    if args.github:
        out = os.environ.get("GITHUB_OUTPUT")
        payload = json.dumps(plan, separators=(",", ":"))
        lines = [
            f"plan={payload}",
            f"any={'true' if plan else 'false'}",
            f"projects={','.join(sorted(selected))}",
        ]
        if out:
            with open(out, "a", encoding="utf-8") as handle:
                handle.write("\n".join(lines) + "\n")
        else:
            print("\n".join(lines))

    if args.json:
        print(json.dumps(plan, indent=2))
        return 0

    for note in notes:
        print(f"  {note}")
    skipped = sorted(tests - selected)
    print(f"\n{len(plan)} leg(s) across {len(selected)} of {len(tests)} test projects")
    for leg in plan:
        print(f"  {leg['name']}")
    if skipped:
        print(f"not reachable from this change: {', '.join(skipped)}")
    return 0


def cmd_explain(args) -> int:
    projects = load_projects()
    selected, notes = select(args.paths, projects)
    for note in notes:
        print(f"  {note}")
    print(f"\nselects: {', '.join(sorted(selected)) or 'nothing'}")
    return 0


def cmd_filter(args) -> int:
    """The filter for one leg, and nothing else, for a shell to capture.

    Deliberately NOT carried in the plan JSON: the complement shard's filter is
    25 KB on its own, and six of those would put a quarter of a megabyte
    through a workflow expression context for no reason. A leg recomputes its
    own from the same checkout, which is the same answer by construction.
    """
    projects = load_projects()
    if args.project not in projects:
        sys.exit(f"unknown project {args.project!r}")
    if not projects[args.project].is_test:
        # A compile-only leg has no tests to slice. Empty rather than an error,
        # so the workflow can ask every leg the same question.
        print("")
        return 0
    count = SHARDS.get(args.project, 1)
    if not 1 <= args.shard <= count:
        sys.exit(f"{args.project} has {count} shard(s); asked for {args.shard}")
    print(shard_filters(projects[args.project], count)[args.shard - 1])
    return 0


def cmd_shards(args) -> int:
    projects = load_projects()
    if args.project not in projects:
        sys.exit(f"unknown project {args.project!r} — one of: {', '.join(sorted(projects))}")
    project = projects[args.project]
    count = SHARDS.get(args.project, 1)
    classes = test_classes(project)
    print(f"{args.project}: {len(classes)} test classes, {sum(classes.values())} tests, {count} shard(s)")
    for index, test_filter in enumerate(shard_filters(project, count), start=1):
        kind = "complement" if test_filter.startswith("FullyQualifiedName!~") else "include"
        terms = len(re.findall(r"FullyQualifiedName", test_filter)) if test_filter else 0
        print(f"  shard {index}/{count}: {kind}, {terms} term(s), {len(test_filter)} chars")
    return 0


def cmd_run(args) -> int:
    """Run the plan locally, legs in parallel."""
    projects = load_projects()
    touched: set[str] | None = None
    if args.base:
        changed = changed_files(args.base, args.head)
        if changed is None:
            selected = set(test_projects(projects))
        else:
            selected = select(changed, projects)[0]
            touched = changed_projects(changed, projects)
    else:
        selected = set(test_projects(projects))
    plan = legs(selected, projects, touched)
    if not plan:
        print("nothing this change can reach — no tests to run")
        return 0

    build = subprocess.run(["dotnet", "build", "Lightbox.sln", "-c", args.configuration], cwd=ROOT)
    if build.returncode != 0:
        return build.returncode

    # Timed legs last and alone. A performance budget measures wall clock, so
    # running one beside five other legs on one machine measures the machine —
    # which is how the first parallel run of this script produced a red
    # `FourK_WholeStrokeIncludingCommit_HasNoPenLiftStall` with nothing wrong.
    # CI never hits this (one runner per leg) and does not need the ordering.
    ordinary = [leg for leg in plan if not leg.get("performance")]
    timed = [leg for leg in plan if leg.get("performance")]

    print(f"\nrunning {len(plan)} leg(s): {len(ordinary)} up to {args.jobs} at a time"
          + (f", then {len(timed)} timed leg(s) alone" if timed else "") + "\n")
    running: list[tuple[dict, subprocess.Popen]] = []
    queue = list(ordinary)
    failures: list[str] = []

    def launch(leg: dict) -> subprocess.Popen:
        if leg["kind"] == "build":
            command = ["dotnet", "build", leg["csproj"], "-c", args.configuration]
        else:
            command = ["dotnet", "test", leg["csproj"], "-c", args.configuration,
                       "--no-build", "--logger", "trx"]
            test_filter = shard_filters(projects[leg["project"]], leg["shards"])[leg["shard"] - 1]
            if test_filter:
                command += ["--filter", test_filter]
        # Somewhere already ignored, because a dev-loop tool that leaves eight
        # untracked files in `git status` is one people stop running. A test
        # leg's log goes beside its TRX under `tests/*/TestResults/`; a compile
        # leg has no TRX and its project may not be under `tests/` at all, so
        # that one goes in `obj/`, which is ignored at any depth.
        directory = ROOT / projects[leg["project"]].directory
        results = directory / ("obj" if leg["kind"] == "build" else "TestResults")
        results = results / f"leg-{leg['shard']}-of-{leg['shards']}"
        results.mkdir(parents=True, exist_ok=True)
        if leg["kind"] != "build":
            # A directory per leg, not one shared by all six. Sharing it makes
            # "the newest TRX" mean "whichever leg finished last", and the count
            # guard then checks one leg's slice against another leg's results.
            # CI has one runner per leg and never sees this; locally it turned
            # a green run into a false shortfall report.
            command += ["--results-directory", str(results)]
        log = results / "leg.log"
        return subprocess.Popen(command, cwd=ROOT, stdout=log.open("w"), stderr=subprocess.STDOUT)

    while queue or running:
        while queue and len(running) < args.jobs:
            leg = queue.pop(0)
            print(f"  start  {leg['name']}")
            running.append((leg, launch(leg)))
        leg, process = running.pop(0)
        code = process.wait()
        print(f"  {'ok   ' if code == 0 else 'FAIL '}  {leg['name']}")
        if code != 0:
            failures.append(leg["name"])

    for leg in timed:
        print(f"  start  {leg['name']} (alone — it is measuring wall clock)")
        code = launch(leg).wait()
        print(f"  {'ok   ' if code == 0 else 'FAIL '}  {leg['name']}")
        if code != 0:
            failures.append(leg["name"])

    if failures:
        print(f"\nfailed: {', '.join(failures)} — see */TestResults/leg-*/leg.log "
              f"(a compile leg logs under its obj/)")
        return 1

    # `Passed!` is not evidence that the tests ran (B269/B281) — a run that died
    # mid-suite prints it too, one line after `Catastrophic failure`. CI checks
    # every leg against discovery and so does this, by the same command, because
    # a local green that means less than CI's green is worse than no local run.
    if args.skip_verify:
        print("\nevery leg passed (count guard skipped by request)")
        return 0
    print("\nchecking every leg ran what it was given")
    for leg in plan:
        if leg["kind"] == "build":
            continue  # nothing ran, so there is nothing to count
        test_filter = shard_filters(projects[leg["project"]], leg["shards"])[leg["shard"] - 1]
        results = (ROOT / projects[leg["project"]].directory / "TestResults"
                   / f"leg-{leg['shard']}-of-{leg['shards']}")
        check = subprocess.run(
            ["python3", "scripts/testcount.py", "verify", "-c", args.configuration,
             "--project", leg["project"], "--filter", test_filter,
             "--results", str(results)],
            cwd=ROOT, capture_output=True, text=True,
            encoding="utf-8", errors="replace")
        if check.returncode != 0:
            print(check.stdout + check.stderr)
            print(f"\n{leg['name']} proved less than it claimed — see above (B269/B281)")
            return 1
    print("every leg passed, and ran everything it was given")
    return 0


# ---------------------------------------------------------------------------
# audit — the rules have to cover what the tests actually read
# ---------------------------------------------------------------------------

COMBINE = re.compile(r'(?:RepoRoot|Root|ProjectRoot)\(\)\s*,\s*((?:"[^"]*"\s*,?\s*)+)')
LITERALS = re.compile(r'"([^"\\\n]{2,120})"')


def reads_of(source: Path) -> set[str]:
    """Repo-relative paths a test source names, resolved against the tree.

    Existence-checked rather than pattern-matched: a literal only counts as a
    read if it actually resolves to something in the repository, which is what
    keeps arbitrary strings out without needing to understand the C#.

    The one ambiguity worth handling is a bare filename. `"README.md"` is a
    read of the repository's README in PublishLayoutTests and is the name of a
    file to *skip* in MonolithRatchetTests, which enumerates
    `.claude/quality/ratchets/`. Resolving it against the root in both places
    would demand a rule saying the App suite depends on the README, which is
    false. So a bare literal that also resolves under a directory the same file
    builds with `Path.Combine(RepoRoot(), …)` is attributed there instead.
    """
    text = source.read_text(encoding="utf-8", errors="ignore")
    found: set[str] = set()
    directories: list[str] = []

    # Path.Combine(RepoRoot(), "a", "b") — two segments or more, so the single
    # `"src"` of the root-walk does not read as a dependency on all of src/.
    for group in COMBINE.findall(text):
        segments = re.findall(r'"([^"]*)"', group)
        if len(segments) < 2:
            continue
        candidate = "/".join(s for s in segments if s)
        target = ROOT / candidate
        if not target.exists():
            continue
        found.add(candidate)
        if target.is_dir():
            directories.append(candidate)

    # A plain literal that happens to name a file in the tree.
    for literal in LITERALS.findall(text):
        candidate = literal.replace("\\", "/").strip("/")
        if not candidate or candidate.startswith(("http", " ")):
            continue
        if "/" not in candidate and any((ROOT / d / candidate).exists() for d in directories):
            continue  # it names a file in a directory this file already walks
        if (ROOT / candidate).is_file():
            found.add(candidate)
    return found


def cmd_audit(args) -> int:
    projects = load_projects()
    tests = test_projects(projects)
    reachable = {name: reach(name, projects) for name in tests}
    problems: list[str] = []
    covered = 0

    for name in tests:
        project = projects[name]
        for source in sorted((ROOT / project.directory).glob("**/*.cs")):
            if any(part in ("obj", "bin") for part in source.parts):
                continue
            for path in sorted(reads_of(source)):
                if owners(path, projects) & reachable[name]:
                    covered += 1
                    continue  # the graph already selects this project for it
                where = f"{project.directory}{source.name}"
                # A directory literal is a base to build paths from, not a read
                # of everything under it. It counts as covered when some rule
                # inside it selects this project — the files themselves are
                # checked on their own account.
                if (ROOT / path).is_dir() and any(
                        pattern.startswith(path) and verdict not in (ALL, NONE) and name in verdict
                        for pattern, verdict, _ in REPO_RULES):
                    covered += 1
                    continue
                decided = rule_for(path)
                if decided is None:
                    problems.append(
                        f"{where} reads {path}, which no rule covers — it would "
                        f"select everything (safe, but say so in REPO_RULES)")
                    continue
                verdict, _ = decided
                if verdict is ALL:
                    covered += 1
                elif verdict is NONE:
                    problems.append(
                        f"{where} reads {path}, and REPO_RULES calls it inert — "
                        f"a change to it would skip the test that reads it")
                elif name not in verdict:
                    problems.append(
                        f"{where} reads {path}, but its rule selects only "
                        f"{', '.join(sorted(verdict))} — {name} would not run")
                else:
                    covered += 1

    print(f"{covered} read(s) accounted for")
    if problems:
        print("\nFAILED — a test that reads a file CI calls inert is PR #95 again:")
        for problem in problems:
            print(f"  {problem}")
        return 1
    print("every repo path the tests read is covered by the graph or a rule")
    return 0


# ---------------------------------------------------------------------------
# selftest
# ---------------------------------------------------------------------------

def _raises(thunk) -> bool:
    try:
        thunk()
    except ValueError:
        return True
    return False


def cmd_selftest(args) -> int:
    checks: list[tuple[str, bool]] = []
    projects = load_projects()
    tests = set(test_projects(projects))

    def check(label: str, ok: bool) -> None:
        checks.append((label, ok))

    # -- the graph ----------------------------------------------------------
    check("every test project is known to the plan", tests == {
        "Lightbox.Core.Tests", "Lightbox.Raster.Tests",
        "Lightbox.Ai.Tests", "Lightbox.App.Tests"})
    check("App.Tests reaches Core through App",
          "Lightbox.Core" in reach("Lightbox.App.Tests", projects))
    check("Core.Tests does not reach Raster",
          "Lightbox.Raster" not in reach("Lightbox.Core.Tests", projects))

    # -- selection ----------------------------------------------------------
    core_change, _ = select(["src/Lightbox.Core/Doc.cs"], projects)
    check("a Core change runs everything", core_change == tests)

    app_change, _ = select(["src/Lightbox.App/Views/MainWindow.axaml.cs"], projects)
    check("an App change runs only the App suite", app_change == {"Lightbox.App.Tests"})

    raster_change, _ = select(["src/Lightbox.Raster/BrushEngine.cs"], projects)
    check("a Raster change spares Core.Tests", "Lightbox.Core.Tests" not in raster_change)
    check("a Raster change still runs App.Tests", "Lightbox.App.Tests" in raster_change)
    check("a Raster change still runs Ai.Tests (it references Raster on purpose)",
          "Lightbox.Ai.Tests" in raster_change)

    linked, _ = select(["tests/Lightbox.Raster.Tests/VisualSheet.cs"], projects)
    check("a file linked into two suites runs both",
          {"Lightbox.Raster.Tests", "Lightbox.App.Tests"} <= linked)

    docs, _ = select(["docs/DESIGN-ai-payload.md"], projects)
    check("a design document runs nothing", docs == set())

    deep_docs, _ = select(["docs/design/ui-reference.png"], projects)
    check("a rule ending /* covers the whole subtree", deep_docs == set())

    readme, _ = select(["README.md"], projects)
    check("README.md is a test input, not documentation (PR #95)",
          readme == {"Lightbox.Core.Tests"})

    ratchet, _ = select([".claude/quality/ratchets/MainViewModel.cs.md"], projects)
    check("a ratchet budget runs the suite that reads it",
          ratchet == {"Lightbox.App.Tests"})

    prose, _ = select([".claude/quality/CHARTER.md"], projects)
    check("the rest of .claude/ stays inert", prose == set())

    unknown, _ = select(["some/new/thing.toml"], projects)
    check("a path with no rule runs EVERYTHING", unknown == tests)

    check("a change to the selector runs everything",
          select(["scripts/testplan.py"], projects)[0] == tests)
    check("a change to the count guard runs everything",
          select(["scripts/testcount.py"], projects)[0] == tests)
    check("an ordinary script still runs only what reads it",
          select(["scripts/codemap.py"], projects)[0] == {"Lightbox.Core.Tests"})

    props, _ = select(["Directory.Build.props"], projects)
    check("the shared build props run everything", props == tests)

    mixed, _ = select(["docs/x.md", "src/Lightbox.App/App.axaml.cs"], projects)
    check("selection is the union, not the last word", mixed == {"Lightbox.App.Tests"})

    check("no changed files at all selects nothing", select([], projects)[0] == set())

    # The edge the graph structurally cannot see, and the reason rules add to
    # the graph instead of replacing it. Found by `audit` rather than by
    # reading, which is the whole argument for `audit` existing.
    program, _ = select(["src/Lightbox.App/Program.cs"], projects)
    check("a rule widens the graph rather than replacing it",
          program == {"Lightbox.App.Tests", "Lightbox.Core.Tests"})
    check("an inert rule never narrows the graph",
          select(["src/Lightbox.App/Views/Foo.axaml.cs"], projects) [0] == {"Lightbox.App.Tests"})

    design, _ = select([".claude/quality/DESIGN.md"], projects)
    check("the design rules run the suite that reads them", design == {"Lightbox.App.Tests"})

    # The bare-filename ambiguity `reads_of` exists to resolve.
    ratchets = ROOT / "tests/Lightbox.App.Tests/MonolithRatchetTests.cs"
    if ratchets.exists():
        check("a filename skipped inside a walked directory is not a root read",
              "README.md" not in reads_of(ratchets))
    publish = ROOT / "tests/Lightbox.Core.Tests/PublishLayoutTests.cs"
    if publish.exists():
        check("a genuine root read is still seen", "README.md" in reads_of(publish))

    # -- sharding, which is where a mistake is silent -----------------------
    weights = {"A": 10, "B": 9, "C": 2, "D": 1}
    groups = balance(weights, 2)
    check("balancing splits the load", sorted(sum(weights[n] for n in g) for g in groups) == [11, 11])
    check("balancing loses no class", sorted(n for g in groups for n in g) == ["A", "B", "C", "D"])
    lopsided = {"A": 10, "B": 1}
    check("the lightest bucket is last, so it carries the unknowns",
          sum(lopsided[n] for n in balance(lopsided, 2)[-1]) == 1)

    app = projects["Lightbox.App.Tests"]
    filters = shard_filters(app, 4)
    check("a sharded project gets one filter per shard", len(filters) == 4)
    check("the last shard is a complement, not a list",
          filters[-1].startswith("FullyQualifiedName!~") and "~FullyQualifiedName" not in filters[-1])
    check("no filter mixes & with |",
          all(not ("&" in f and "|" in f) for f in filters))

    # The property the whole design rests on: a class nobody enumerated is
    # still run, because it is excluded by no term of the complement.
    excluded = set(re.findall(r"FullyQualifiedName!~([A-Za-z0-9_.]+)\.", filters[-1]))
    included = set(re.findall(r"FullyQualifiedName~([A-Za-z0-9_.]+)\.", "|".join(filters[:-1])))
    check("every named class is in exactly one include shard and excluded once",
          excluded == included)
    check("an unknown class is excluded by nothing, so the complement runs it",
          "Lightbox.App.Tests.AClassNobodyEnumerated" not in excluded)

    check("an unsharded project gets one empty filter", shard_filters(projects["Lightbox.Core.Tests"], 1) == [""])

    # -- the partition, evaluated rather than argued ------------------------
    #
    # Every real class name, plus one this script could never have enumerated,
    # run through the filters the plan emits. Each must land in exactly one
    # shard: twice is wasted runner time, none is a test that silently stopped
    # running, which is B269 with a new cause.
    names = [f"{cls}.ATest" for cls in test_classes(app)]
    names.append("Lightbox.App.Tests.AClassAddedAfterThePlanWasMade.ATest")
    names.append("Lightbox.App.Tests.Outer+Nested.ATest")
    landings = [sum(1 for f in filters if matches_filter(f, n)) for n in names]
    check("every test lands in exactly one shard", set(landings) == {1})
    unknown_shard = [i for i, f in enumerate(filters)
                     if matches_filter(f, "Lightbox.App.Tests.AClassAddedAfterThePlanWasMade.ATest")]
    check("a class added after the plan was made lands in the complement shard",
          unknown_shard == [len(filters) - 1])

    check("an empty filter matches everything", matches_filter("", "Anything.At.All"))
    check("a filter that mixes operators is refused rather than guessed",
          _raises(lambda: matches_filter("FullyQualifiedName~A|FullyQualifiedName!~B&FullyQualifiedName~C", "A")))
    check("an unreadable filter term is refused",
          _raises(lambda: matches_filter("Category=Performance", "A")))

    # -- the plan -----------------------------------------------------------
    plan = legs({"Lightbox.App.Tests", "Lightbox.Core.Tests"}, projects)
    check("the plan shards App and leaves Core whole",
          len([l for l in plan if l["project"] == "Lightbox.App.Tests"]) == SHARDS["Lightbox.App.Tests"]
          and len([l for l in plan if l["project"] == "Lightbox.Core.Tests"]) == 1)
    check("every leg names a csproj that exists",
          all((ROOT / l["csproj"]).exists() for l in plan))
    check("the plan is JSON a workflow can read", json.loads(json.dumps(plan)) == plan)
    check("the plan carries no filter, so the matrix stays small",
          all("filter" not in leg for leg in plan))
    check("the plan is small enough for a workflow expression",
          len(json.dumps(legs(tests, projects), separators=(",", ":"))) < 8000)

    # -- projects nothing tests still have to compile -----------------------
    #
    # `dotnet test Lightbox.sln` built the whole solution as a side effect and
    # this does not, so a project outside every test's closure would silently
    # stop being built. Today that is tools/Lightbox.Bench.
    orphans = uncovered(projects)
    check("a project no test reaches is found rather than listed",
          "Lightbox.Bench" in orphans)
    check("nothing a test already builds is duplicated as a compile leg",
          not ({"Lightbox.Core", "Lightbox.App", "Lightbox.Raster"} & set(orphans)))

    full = legs(tests, projects)
    check("a full plan compiles the projects nothing tests",
          any(leg["kind"] == "build" and leg["project"] == "Lightbox.Bench" for leg in full))

    # A change to what an untested project depends on must compile it, even
    # though the change is nowhere near its directory.
    core_touched = changed_projects(["src/Lightbox.Core/Doc.cs"], projects)
    check("a change to something an untested project uses still compiles it",
          any(leg["kind"] == "build" for leg in
              legs(*select(["src/Lightbox.Core/Doc.cs"], projects)[:1], projects, core_touched)))

    docs_touched = changed_projects(["docs/x.md"], projects)
    check("a documentation change compiles nothing",
          legs(set(), projects, docs_touched) == [])

    # Bench reaches Ai through App, so an Ai change DOES compile it. A test
    # project is the thing it genuinely cannot see.
    check("Bench sees Lightbox.Ai through Lightbox.App",
          "Lightbox.Ai" in reach("Lightbox.Bench", projects))
    only_tests = ["tests/Lightbox.Core.Tests/AnchorTests.cs"]
    tests_touched = changed_projects(only_tests, projects)
    check("a change to a test suite does not compile the benchmark",
          not any(leg["kind"] == "build" for leg in
                  legs(select(only_tests, projects)[0], projects, tests_touched)))

    check("every leg says which kind it is", all("kind" in leg for leg in full))

    # -- timed tests are kept off a loaded machine --------------------------
    timed_classes = performance_classes(app)
    check("the performance classes are found", len(timed_classes) > 5)
    check("a performance class is one that exists",
          timed_classes <= set(test_classes(app)))

    app_legs = [leg for leg in full if leg["project"] == "Lightbox.App.Tests"]
    check("exactly one leg per sharded project is the timed one",
          sum(1 for leg in app_legs if leg["performance"]) == 1)
    check("the timed leg is the first shard",
          next(leg["shard"] for leg in app_legs if leg["performance"]) == 1)
    check("an unsharded project has no timed leg",
          not any(leg["performance"] for leg in full if leg["shards"] == 1))

    # Every timed class must actually be in the timed leg, or the point is lost.
    timed_filter = shard_filters(app, SHARDS["Lightbox.App.Tests"])[0]
    check("every performance class lands in the timed leg",
          all(matches_filter(timed_filter, f"{c}.ATest") for c in timed_classes))
    check("nothing else lands in the timed leg",
          not any(matches_filter(timed_filter, f"{c}.ATest")
                  for c in set(test_classes(app)) - timed_classes))
    check("a compile leg has no filter to ask for",
          shard_filters(projects["Lightbox.Bench"], 1) == [""])

    failed = [label for label, ok in checks if not ok]
    for label, ok in checks:
        print(f"  {'ok  ' if ok else 'FAIL'} {label}")
    if failed:
        print(f"\nselftest: {len(failed)} of {len(checks)} checks failed")
        return 1
    print(f"\nselftest: all {len(checks)} checks pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    plan = sub.add_parser("plan", help="the legs to run for a change")
    plan.add_argument("--base", help="the ref to diff against")
    plan.add_argument("--head", default=None,
                      help="compare two commits instead of the base against your working tree")
    plan.add_argument("--changed-files", help="read paths from a file, or - for stdin")
    plan.add_argument("--json", action="store_true")
    plan.add_argument("--github", action="store_true", help="write GITHUB_OUTPUT entries")

    explain = sub.add_parser("explain", help="why these paths select what they select")
    explain.add_argument("paths", nargs="+")

    shards = sub.add_parser("shards", help="the shard filters for one project")
    shards.add_argument("project")

    one = sub.add_parser("filter", help="the filter for one leg, for a shell to capture")
    one.add_argument("project")
    one.add_argument("shard", type=int)

    run = sub.add_parser("run", help="run the plan locally, legs in parallel")
    run.add_argument("--base")
    run.add_argument("--head", default=None,
                     help="compare two commits instead of the base against your working tree")
    run.add_argument("-c", "--configuration", default="Release")
    run.add_argument("-j", "--jobs", type=int, default=max(1, (os.cpu_count() or 4) // 2))
    run.add_argument("--skip-verify", action="store_true",
                     help="do not check each leg against discovery afterwards")

    sub.add_parser("audit", help="prove the rules cover what the tests read")
    sub.add_parser("selftest", help="feed the rules the cases they exist to get right")

    args = parser.parse_args()
    return {
        "plan": cmd_plan, "explain": cmd_explain, "shards": cmd_shards,
        "filter": cmd_filter, "run": cmd_run, "audit": cmd_audit,
        "selftest": cmd_selftest,
    }[args.command](args)


if __name__ == "__main__":
    # `filter` exists to be captured and `plan --json` to be piped, so closing
    # the pipe early — `| head`, a shell that stopped reading — is ordinary use
    # rather than an error. Without this it ends in a BrokenPipeError traceback
    # that reads like a fault in the plan.
    try:
        sys.exit(main())
    except BrokenPipeError:
        try:
            sys.stdout.close()
        finally:
            os._exit(0)
