#!/usr/bin/env python3
"""Assert that every discovered test actually ran (B269, B281).

A full-solution `dotnet test` has been seen executing hundreds fewer App tests
than exist and still printing `Passed!` with exit 0 — twice locally (B269: 2889
of a discovered 3498; B281: 2779 of 3587) and, in the killed-host variant,
after xUnit's own `Catastrophic failure` line. A suite that quietly proves less
than it claims is worse than a red one, because every gate in this repository
rests on "all four suites green" meaning what it says.

This script is the guard those entries prescribe: after a test run that wrote
TRX logs, compare the NAMES the run reported against the names discovery finds,
per assembly, and fail on any test that went missing. Names rather than counts,
which is B281's own first step — a count says "600 short", a name diff says
which 600, and a name match survives theory pre-enumeration quirks that a bare
count comparison would trip over.

Usage:
    python3 scripts/testcount.py verify [-c Release]
        After `dotnet test ... --logger trx`. Reads the newest .trx per test
        project, runs `--list-tests` discovery against the same build, and
        exits 1 if any discovered test was never reported.
    python3 scripts/testcount.py verify --project <name> [--filter <expr>]
        The same check for ONE assembly, and for one shard of it. Used by CI,
        where each leg of the matrix runs a slice of a suite and must prove it
        ran all of that slice.
    python3 scripts/testcount.py selftest
        Feeds the comparison synthetic shortfalls and asserts they are caught.

SHARDING AND WHY IT DOES NOT WEAKEN THIS

`build.yml` now runs the expensive suites in slices, one leg per slice, so no
single run reports every test any more and a bare "did everything discovered
report?" would fail on every leg. The check is therefore made against the leg's
own filter: discovery finds the whole assembly, the filter says which of those
names this leg was supposed to run, and a name in that set with no result is
the same failure it always was.

Two properties keep that from being weaker than the whole-assembly check it
replaces. The filters partition the assembly — `testplan.py` builds the last
shard as the *complement* of the others, so their union is everything whatever
the planner failed to enumerate — and the filter is evaluated HERE, by
`testplan.matches_filter`, rather than trusted to mean the same thing to
VSTest. A leg that ran a different set from the one it was asked for fails,
which is a check the single-assembly version never had.

`matches_filter` is imported rather than reimplemented. Two readings of a
filter expression would drift, and a drift here is a guard that blesses a gap.

What `verify` cannot see, said plainly: a wedged host that produced no TRX at
all fails loudly here (no results file is a failure, not a pass) — but the run
that died mid-suite *after* the summary printed writes a complete-looking TRX
for the tests it reached, and only the discovery diff tells the difference.
That diff is exactly what this exists to take.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from testplan import matches_filter  # noqa: E402  (one reading of a filter, not two)

ROOT = Path(__file__).resolve().parent.parent
TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def test_projects(only: str | None = None) -> list[Path]:
    """Every test csproj, tests/<name>/<name>.csproj by convention."""
    found = sorted(ROOT.glob("tests/*/*.csproj"))
    if not found:
        sys.exit("no test projects under tests/ — is this the repository root?")
    if only:
        found = [p for p in found if p.stem == only]
        if not found:
            sys.exit(f"no test project named {only!r} under tests/")
    return found


def newest_trx(project_dir: Path, results_dir: Path | None = None) -> Path | None:
    """The newest TRX for a run, in `results_dir` when one is named.

    **Naming it matters the moment legs share a machine.** Six shards of one
    assembly all write into `tests/<project>/TestResults/`, so "newest" is
    whichever leg finished last rather than the leg being checked — and the
    guard then compares one leg's slice against another leg's results and
    reports a shortfall that is not there. Found exactly that way, by this
    guard, on the first local run that got far enough to reach it.

    CI never hits it — one runner per leg, one TRX — which is precisely why it
    had to be found locally rather than trusted.
    """
    directory = results_dir if results_dir is not None else project_dir / "TestResults"
    results = sorted(directory.glob("*.trx"), key=lambda p: p.stat().st_mtime)
    return results[-1] if results else None


def reported_names(trx: Path) -> tuple[set[str], int, int]:
    """The test names a run reported, plus the TRX's own two counts.

    Both counts come back so `verify` can cross-check the logger against
    itself: `Counters@total` disagreeing with the number of result rows is
    B281's "run and never reported" split showing inside one file.
    """
    root = ET.parse(trx).getroot()
    rows = root.findall(f".//{TRX_NS}Results/{TRX_NS}UnitTestResult")
    names = {r.get("testName") or "" for r in rows} - {""}
    counters = root.find(f".//{TRX_NS}ResultSummary/{TRX_NS}Counters")
    total = int(counters.get("total", "-1")) if counters is not None else -1
    return names, len(rows), total


def discovered_names(csproj: Path, configuration: str) -> set[str]:
    """What `--list-tests` finds against the already-built assembly.

    `--no-build`, deliberately: discovery must describe the binaries the run
    under scrutiny actually executed, and a rebuild here could describe a
    different tree.
    """
    proc = subprocess.run(
        [
            "dotnet", "test", str(csproj),
            "-c", configuration, "--no-build", "--list-tests",
        ],
        # utf-8 named, not assumed: the ledger gate requires it, because a
        # Windows console defaults text mode to the ANSI codepage and a test
        # display name with anything past ASCII would decode to mojibake — and
        # then read as "missing" here, which is a false alarm from the guard
        # whose whole job is not to produce them.
        capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=ROOT,
    )
    if proc.returncode != 0:
        sys.exit(
            f"discovery failed for {csproj.name} (exit {proc.returncode}) — "
            f"was the {configuration} build present?\n{proc.stdout[-2000:]}{proc.stderr[-2000:]}"
        )
    lines = proc.stdout.splitlines()
    try:
        start = next(i for i, l in enumerate(lines) if "are available" in l)
    except StopIteration:
        sys.exit(f"discovery for {csproj.name} printed no test list:\n{proc.stdout[-2000:]}")
    return {l.strip() for l in lines[start + 1:] if l.startswith("    ")}


def missing_tests(discovered: set[str], reported: set[str]) -> set[str]:
    """Discovered names with no reported run.

    A discovered name also counts as reported when a parameterised case ran
    under it — `M` is covered by `M(x: 1)` — because a theory whose data
    cannot be pre-enumerated is listed once by discovery and expanded at run
    time. Zero such theories exist today (measured: 1401/1401 and 133/133
    name-exact on 2026-09-01), but the guard must not become its own flake
    the day somebody writes one.
    """
    covered = set()
    for name in discovered:
        if name in reported:
            covered.add(name)
            continue
        prefix = name + "("
        if any(r.startswith(prefix) for r in reported):
            covered.add(name)
    return discovered - covered


def expected_names(discovered: set[str], test_filter: str) -> set[str]:
    """Of everything discovery found, the names this leg was asked to run.

    An empty filter means the whole assembly, which is what a single-leg run
    and every local run pass. `matches_filter` raises on an expression it
    cannot read rather than returning a smaller set — a filter nobody can
    evaluate is a coverage claim nobody can check.
    """
    if not test_filter:
        return discovered
    return {name for name in discovered if matches_filter(test_filter, name)}


def cmd_verify(configuration: str, only: str | None = None, test_filter: str = "",
               results: str | None = None) -> int:
    failures: list[str] = []
    if test_filter and not only:
        sys.exit("--filter needs --project: a filter belongs to one assembly's leg")
    if results and not only:
        sys.exit("--results needs --project: a results directory belongs to one leg")

    results_dir = Path(results).resolve() if results else None
    for csproj in test_projects(only):
        project = csproj.parent
        trx = newest_trx(project, results_dir)
        if trx is None:
            # No results file is how a host that died before the logger
            # flushed looks — the exact run this guard must not bless.
            where = results_dir if results_dir is not None else project / "TestResults"
            failures.append(f"{project.name}: no .trx under {where} — the run left no record")
            continue

        reported, rows, total = reported_names(trx)
        discovered = discovered_names(csproj, configuration)
        expected = expected_names(discovered, test_filter)
        missing = missing_tests(expected, reported)

        # The other direction, and it only exists once there are shards: a leg
        # that ran tests OUTSIDE its filter means VSTest read the expression
        # differently from `testplan.py`, and then the shards are not a
        # partition and some other leg's slice may be going unrun. Reported as
        # loudly as a shortfall, because it is the same hazard one step back.
        stray: set[str] = set()
        if test_filter:
            stray = {name for name in reported
                     if name in discovered and name not in expected}

        verdict = "ok"
        if total >= 0 and total != rows:
            verdict = f"TRX inconsistent: Counters says {total}, file holds {rows} results"
            failures.append(f"{project.name}: {verdict}")
        if missing:
            verdict = f"{len(missing)} expected test(s) never ran"
            shown = "\n    ".join(sorted(missing)[:40])
            more = f"\n    … and {len(missing) - 40} more" if len(missing) > 40 else ""
            failures.append(f"{project.name}: {verdict}:\n    {shown}{more}")
        if stray:
            verdict = f"{len(stray)} test(s) ran that this leg's filter excludes"
            shown = "\n    ".join(sorted(stray)[:20])
            failures.append(
                f"{project.name}: {verdict} — VSTest and testplan.py disagree about "
                f"the filter, so the shards are not a partition:\n    {shown}")

        scope = f", {len(expected)} in this leg" if test_filter else ""
        print(
            f"{project.name}: discovered {len(discovered)}{scope}, reported {rows} "
            f"({trx.name}) — {verdict}"
        )

    if failures:
        print(
            "\nFAILED — a run that proves less than discovery promises is a "
            "failed run (B269/B281):\n" + "\n".join(failures)
        )
        return 1
    print("\nevery expected test ran")
    return 0


def cmd_selftest() -> int:
    """The comparison, fed the failures it exists to catch."""
    checks: list[tuple[str, bool]] = []

    # The B269 shape: hundreds discovered, fewer reported.
    short = missing_tests({"A.T1", "A.T2", "A.T3"}, {"A.T1", "A.T3"})
    checks.append(("a dropped test is named", short == {"A.T2"}))

    # A complete run passes.
    checks.append(("a complete run passes", not missing_tests({"A.T1"}, {"A.T1"})))

    # A theory that could not pre-enumerate: listed once, expanded at run time.
    checks.append((
        "a run-time-expanded theory is not a false alarm",
        not missing_tests({"A.Theory"}, {"A.Theory(x: 1)", "A.Theory(x: 2)"}),
    ))

    # And the expansion rule must not cover a genuinely different test.
    checks.append((
        "a lookalike name does not cover a missing test",
        missing_tests({"A.T"}, {"A.T2"}) == {"A.T"},
    ))

    # A TRX written and read back, so the parser is exercised end to end.
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        trx = Path(tmp) / "sample.trx"
        ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
        trx.write_text(
            f'<?xml version="1.0"?><TestRun xmlns="{ns}">'
            "<Results>"
            '<UnitTestResult testName="A.T1" outcome="Passed"/>'
            '<UnitTestResult testName="A.T2" outcome="NotExecuted"/>'
            "</Results>"
            '<ResultSummary><Counters total="2" executed="1"/></ResultSummary>'
            "</TestRun>",
            encoding="utf-8",
        )
        names, rows, total = reported_names(trx)
        checks.append(("the TRX parser reads names", names == {"A.T1", "A.T2"}))
        checks.append(("a skipped test still counts as reported", "A.T2" in names))
        checks.append(("the TRX counters are read", (rows, total) == (2, 2)))

    # -- the shard slice ----------------------------------------------------
    #
    # Everything below is new with the matrix, and the first two are the ones
    # that would otherwise let a shard prove nothing: an empty filter must
    # still mean "all of it", and a leg's slice must be exactly its filter's.
    everything = {"NS.A.T1", "NS.A.T2", "NS.B.T1", "NS.C.T1"}
    checks.append((
        "no filter still expects the whole assembly",
        expected_names(everything, "") == everything,
    ))
    checks.append((
        "an include filter expects only its own classes",
        expected_names(everything, "FullyQualifiedName~NS.A.|FullyQualifiedName~NS.B.")
        == {"NS.A.T1", "NS.A.T2", "NS.B.T1"},
    ))
    checks.append((
        "a complement filter expects everything the others did not",
        expected_names(everything, "FullyQualifiedName!~NS.A.&FullyQualifiedName!~NS.B.")
        == {"NS.C.T1"},
    ))
    # The property the whole scheme rests on, asserted here as well as in
    # testplan: two shards' expectations must cover the assembly exactly once.
    left = expected_names(everything, "FullyQualifiedName~NS.A.|FullyQualifiedName~NS.B.")
    right = expected_names(everything, "FullyQualifiedName!~NS.A.&FullyQualifiedName!~NS.B.")
    checks.append(("two shards partition the assembly",
                   left | right == everything and not (left & right)))
    checks.append((
        "a shard that drops one of its own tests is caught",
        missing_tests(left, {"NS.A.T1", "NS.B.T1"}) == {"NS.A.T2"},
    ))
    checks.append((
        "a shard that skips every test it was given is caught",
        missing_tests(left, set()) == left,
    ))

    # Discovery output parsing, from a captured shape of the real thing.
    listing = (
        "Determining projects to restore...\n"
        "The following Tests are available:\n"
        "    A.T1\n"
        "    A.Theory(x: 1)\n"
    )
    lines = listing.splitlines()
    start = next(i for i, l in enumerate(lines) if "are available" in l)
    parsed = {l.strip() for l in lines[start + 1:] if l.startswith("    ")}
    checks.append(("the listing parser reads names", parsed == {"A.T1", "A.Theory(x: 1)"}))

    failed = [name for name, ok in checks if not ok]
    for name, ok in checks:
        print(f"  {'ok  ' if ok else 'FAIL'} {name}")
    if failed:
        print(f"selftest: {len(failed)} of {len(checks)} checks failed")
        return 1
    print(f"selftest: all {len(checks)} checks pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    verify = sub.add_parser("verify", help="compare reported names against discovery")
    verify.add_argument("-c", "--configuration", default="Release")
    verify.add_argument("--project", help="check one test project rather than all of them")
    verify.add_argument("--filter", default="", dest="test_filter",
                        help="the VSTest filter this leg ran with, so its slice is what is expected")
    verify.add_argument("--results",
                        help="the directory this leg's TRX was written to, when legs share a "
                             "machine and the project's TestResults holds more than one")
    sub.add_parser("selftest", help="feed the comparison synthetic shortfalls")
    args = parser.parse_args()
    if args.command == "verify":
        return cmd_verify(args.configuration, args.project, args.test_filter, args.results)
    return cmd_selftest()


if __name__ == "__main__":
    sys.exit(main())
