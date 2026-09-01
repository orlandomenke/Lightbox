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
    python3 scripts/testcount.py selftest
        Feeds the comparison synthetic shortfalls and asserts they are caught.

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

ROOT = Path(__file__).resolve().parent.parent
TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def test_projects() -> list[Path]:
    """Every test csproj, tests/<name>/<name>.csproj by convention."""
    found = sorted(ROOT.glob("tests/*/*.csproj"))
    if not found:
        sys.exit("no test projects under tests/ — is this the repository root?")
    return found


def newest_trx(project_dir: Path) -> Path | None:
    results = sorted(
        project_dir.glob("TestResults/*.trx"), key=lambda p: p.stat().st_mtime
    )
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


def cmd_verify(configuration: str) -> int:
    failures: list[str] = []
    for csproj in test_projects():
        project = csproj.parent
        trx = newest_trx(project)
        if trx is None:
            # No results file is how a host that died before the logger
            # flushed looks — the exact run this guard must not bless.
            failures.append(f"{project.name}: no .trx under {project.name}/TestResults — the run left no record")
            continue

        reported, rows, total = reported_names(trx)
        discovered = discovered_names(csproj, configuration)
        missing = missing_tests(discovered, reported)

        verdict = "ok"
        if total >= 0 and total != rows:
            verdict = f"TRX inconsistent: Counters says {total}, file holds {rows} results"
            failures.append(f"{project.name}: {verdict}")
        if missing:
            verdict = f"{len(missing)} discovered test(s) never ran"
            shown = "\n    ".join(sorted(missing)[:40])
            more = f"\n    … and {len(missing) - 40} more" if len(missing) > 40 else ""
            failures.append(f"{project.name}: {verdict}:\n    {shown}{more}")

        print(
            f"{project.name}: discovered {len(discovered)}, reported {rows} "
            f"({trx.name}) — {verdict}"
        )

    if failures:
        print(
            "\nFAILED — a run that proves less than discovery promises is a "
            "failed run (B269/B281):\n" + "\n".join(failures)
        )
        return 1
    print("\nevery discovered test ran")
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
    sub.add_parser("selftest", help="feed the comparison synthetic shortfalls")
    args = parser.parse_args()
    if args.command == "verify":
        return cmd_verify(args.configuration)
    return cmd_selftest()


if __name__ == "__main__":
    sys.exit(main())
