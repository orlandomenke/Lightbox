#!/usr/bin/env python3
"""Index the codebase so agents can find things without reading it.

Grepping a 20k-line solution to answer "where does X live" costs thousands of
tokens per question and gets re-paid every session. This builds a compact,
queryable index instead:

    map.json      every file: symbols with line numbers, dependencies, git
                  churn, which tests exercise it, and a risk score
    INDEX.md      the shortlist an agent reads at the start of a task
    HOTSPOTS.md   where change is risky: heavily edited, widely depended on,
                  thinly tested

INDEX.md and FEATURES.md are committed; map.json and HOTSPOTS.md are not, and
the line between them is whether the artefact depends on git history. Anything
that does is wrong the moment it is committed, because committing it is
another commit. See .gitignore.

Usage
    codemap.py build              rebuild all artefacts
    codemap.py find <term>        symbols/files matching a term, with line
                                  numbers and the tests that cover them
    codemap.py file <path>        one file's symbols, dependents and tests
    codemap.py promises <term>    the behaviour inventory, filtered — the
                                  queryable form of FEATURES.md
    codemap.py stale              exit 0 if the map is current, 1 if not
    codemap.py refresh            rebuild only if the map is out of date
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from collections import defaultdict
from dataclasses import dataclass, field, asdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / ".claude" / "codemap"
SOURCE_DIRS = ["src", "tests", "tools"]
CODE_SUFFIXES = {".cs", ".axaml"}

# Commit subjects that mean "this file needed fixing", which is the strongest
# single predictor of where the next defect will be.
FIX_WORDS = re.compile(
    r"\b(fix|fixes|fixed|bug|regression|crash|freeze|hang|lag|stutter|leak|"
    r"wrong|broken|revert|hotfix)\b",
    re.IGNORECASE,
)

TYPE_DECL = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?P<mods>(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file)\s+)*"
    r"(?P<kind>class|record|struct|interface|enum)\s+"
    r"(?P<name>[A-Z]\w*)",
)

MEMBER_DECL = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?P<mods>(?:public|internal|protected)\s+)"
    r"(?:(?:static|virtual|override|sealed|abstract|async|partial|readonly|extern|new|unsafe)\s+)*"
    # The tail may be end-of-line: Allman braces put the `{` of a property or
    # method on the next line, and without this every block-bodied property in
    # the codebase — most of the view model's API — is invisible to the index.
    r"(?P<sig>[\w<>\[\],\?\.\(\) ]+?)\s*(?P<tail>[\{=;\(]|$)",
)

# CommunityToolkit.Mvvm source generators. The attribute sits on its own line
# above the declaration, and the member it produces exists nowhere in source —
# so without these the index cannot see most of the view model's public API.
GENERATOR_ATTR = re.compile(r"^\s*\[(?P<attr>ObservableProperty|RelayCommand)\b")
BACKING_FIELD = re.compile(r"^\s*private\s+[\w<>\[\],\?\.]+\s+_(?P<field>\w+)\s*[;=]")
COMMAND_METHOD = re.compile(r"^\s*private\s+(?:async\s+)?[\w<>\[\],\?\.]+\s+(?P<name>\w+)\s*\(")

AXAML_NAME = re.compile(r'x:Name="(?P<name>\w+)"')
AXAML_HANDLER = re.compile(r'(?:Click|Tapped|Changed|Pressed|Released|Entered|Exited|Drop|DragOver)="(?P<name>\w+)"')
USING_INTERNAL = re.compile(r"^\s*using\s+(Lightbox\.[\w\.]+)\s*;", re.MULTILINE)


def run(args: list[str]) -> str:
    try:
        return subprocess.run(
            args, cwd=ROOT, capture_output=True, text=True, check=False, timeout=120
        ).stdout
    except (OSError, subprocess.SubprocessError):
        return ""


@dataclass
class FileInfo:
    path: str
    project: str
    kind: str  # "source" | "test" | "ui"
    loc: int = 0
    types: list[dict] = field(default_factory=list)
    members: list[dict] = field(default_factory=list)
    uses_namespaces: list[str] = field(default_factory=list)
    depends_on: list[str] = field(default_factory=list)
    dependents: list[str] = field(default_factory=list)
    tests: list[str] = field(default_factory=list)
    tests_indirect: list[str] = field(default_factory=list)
    commits: int = 0
    fix_commits: int = 0
    last_touched: str = ""
    heat: float = 0.0
    risk: float = 0.0


def project_of(path: Path) -> str:
    parts = path.parts
    for i, part in enumerate(parts):
        if part in ("src", "tests", "tools") and i + 1 < len(parts):
            return parts[i + 1]
    return "?"


def classify(path: Path) -> str:
    if path.suffix == ".axaml":
        return "ui"
    if "tests" in path.parts:
        return "test"
    # Development tooling — indexed so the roadmap can anchor to it, but not
    # counted as application source, which would put a benchmark harness into
    # the hotspot and coverage figures for the app.
    if "tools" in path.parts:
        return "tool"
    return "source"


def collect_files() -> list[Path]:
    found: list[Path] = []
    for directory in SOURCE_DIRS:
        base = ROOT / directory
        if not base.exists():
            continue
        for path in base.rglob("*"):
            if path.suffix not in CODE_SUFFIXES:
                continue
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            found.append(path)
    return sorted(found)


def Generated(attribute: str, line: str) -> str | None:
    """The public name a CommunityToolkit attribute produces, if any.

    `[ObservableProperty] private int _fooBar;` becomes `FooBar`;
    `[RelayCommand] private void DoThing()` becomes `DoThingCommand`.
    """
    if attribute == "ObservableProperty":
        if (m := BACKING_FIELD.match(line)) is None:
            return None
        field = m.group("field")
        return field[:1].upper() + field[1:]
    if attribute == "RelayCommand":
        if (m := COMMAND_METHOD.match(line)) is None:
            return None
        return m.group("name") + "Command"
    return None


def parse_source(path: Path, text: str, info: FileInfo) -> None:
    lines = text.splitlines()
    info.loc = len(lines)
    info.uses_namespaces = sorted(set(USING_INTERNAL.findall(text)))

    brace_depth = 0
    current_type = ""
    pending_generator = ""
    for number, line in enumerate(lines, start=1):
        stripped = line.strip()
        if stripped.startswith("//") or stripped.startswith("///"):
            brace_depth += line.count("{") - line.count("}")
            continue

        if (attr := GENERATOR_ATTR.match(line)) is not None:
            pending_generator = attr.group("attr")
            continue

        type_match = TYPE_DECL.match(line)
        if type_match:
            current_type = type_match.group("name")
            info.types.append(
                {
                    "name": current_type,
                    "kind": type_match.group("kind"),
                    "line": number,
                }
            )
        elif current_type:
            # A generated member: the attribute was on the line above and the
            # member it produces exists nowhere in source. Record it under the
            # name callers actually use.
            if pending_generator and (generated := Generated(pending_generator, line)) is not None:
                info.members.append(
                    {"name": generated, "type": current_type, "line": number,
                     "sig": f"[{pending_generator}] {line.strip()[:80]}"}
                )
                pending_generator = ""
                brace_depth += line.count("{") - line.count("}")
                continue

            member = MEMBER_DECL.match(line)
            if member:
                sig = " ".join(member.group("sig").split())
                # The last identifier in the signature is the member name.
                name_match = re.search(r"(\w+)\s*$", sig)
                if name_match and name_match.group(1) not in {"get", "set", "return"}:
                    info.members.append(
                        {
                            "name": name_match.group(1),
                            "type": current_type,
                            "line": number,
                            "sig": sig[-90:],
                        }
                    )
        brace_depth += line.count("{") - line.count("}")


def parse_axaml(path: Path, text: str, info: FileInfo) -> None:
    info.loc = len(text.splitlines())
    for name in sorted(set(AXAML_NAME.findall(text))):
        info.types.append({"name": name, "kind": "element", "line": 0})
    for handler in sorted(set(AXAML_HANDLER.findall(text))):
        info.members.append({"name": handler, "type": path.stem, "line": 0, "sig": "handler"})


def git_history(files: dict[str, FileInfo]) -> None:
    """Per-file commit counts, fix-commit counts and last-touched date."""
    log = run(["git", "log", "--no-merges", "--date=short",
               "--format=%x01%H%x02%ad%x02%s", "--name-only"])
    if not log:
        return
    for block in log.split("\x01"):
        if not block.strip():
            continue
        header, _, body = block.partition("\n")
        parts = header.split("\x02")
        if len(parts) < 3:
            continue
        _, date, subject = parts[0], parts[1], parts[2]
        is_fix = bool(FIX_WORDS.search(subject))
        for name in body.splitlines():
            name = name.strip()
            info = files.get(name)
            if info is None:
                continue
            info.commits += 1
            if is_fix:
                info.fix_commits += 1
            if not info.last_touched:
                info.last_touched = date


def link_dependencies(files: dict[str, FileInfo]) -> None:
    """Wire file→file edges from type references, and tests→source coverage."""
    owner: dict[str, str] = {}
    for path, info in files.items():
        for declared in info.types:
            if declared["kind"] == "element":
                continue
            owner.setdefault(declared["name"], path)

    # Longer names first so "MainViewModel" wins over "Main" style prefixes.
    # The lookbehind rejects namespace segments — `Lightbox.App` must not read
    # as a reference to the `App` class, which otherwise makes half the
    # solution look like it depends on the other half.
    names = sorted(owner, key=len, reverse=True)
    pattern = (
        re.compile(r"(?<![\w.])(" + "|".join(re.escape(n) for n in names) + r")\b")
        if names else None
    )

    for path, info in files.items():
        text = (ROOT / path).read_text(encoding="utf-8", errors="ignore")
        if pattern is None:
            continue
        referenced = set()
        for match in pattern.finditer(text):
            target = owner.get(match.group(1))
            if target and target != path:
                referenced.add(target)
        info.depends_on = sorted(referenced)

    for path, info in files.items():
        for target in info.depends_on:
            files[target].dependents.append(path)
        # A test file referencing a source file counts as coverage evidence.
        if info.kind == "test":
            for target in info.depends_on:
                if files[target].kind != "test":
                    files[target].tests.append(path)

    for info in files.values():
        info.dependents = sorted(set(info.dependents))
        info.tests = sorted(set(info.tests))

    # A file a test never names can still be driven through one that it does —
    # ComposeRing has no test of its own but every painting test runs through
    # it. Counting that one hop keeps the risk score from crying wolf, while
    # keeping it weaker evidence than a test that targets the file directly.
    for path, info in files.items():
        if info.kind == "test" or info.tests:
            continue
        indirect: set[str] = set()
        for dependent in info.dependents:
            neighbour = files[dependent]
            if neighbour.kind == "test":
                indirect.add(dependent)
            else:
                indirect.update(neighbour.tests)
        info.tests_indirect = sorted(indirect)


def score(files: dict[str, FileInfo]) -> None:
    """Heat = how much this file moves and matters. Risk = heat without tests."""
    def norm(values: list[float]) -> list[float]:
        top = max(values) if values else 0
        return [v / top if top else 0.0 for v in values]

    sources = [f for f in files.values() if f.kind != "test"]
    if not sources:
        return
    churn = norm([f.commits for f in sources])
    fixes = norm([f.fix_commits for f in sources])
    central = norm([len(f.dependents) for f in sources])
    size = norm([f.loc for f in sources])

    for info, c, x, d, s in zip(sources, churn, fixes, central, size):
        info.heat = round(0.30 * c + 0.30 * x + 0.25 * d + 0.15 * s, 4)
        # Tests are the mitigation: the same heat with no test is the risk.
        # Direct tests count fully; being reachable from a test counts for a
        # third, because such a test rarely fails for the right reason.
        direct = min(1.0, len(info.tests) / 3.0)
        indirect = min(1.0, len(info.tests_indirect) / 3.0)
        cover = min(1.0, direct + indirect / 3.0)
        info.risk = round(info.heat * (1.0 - 0.75 * cover), 4)


def test_inventory(files: dict[str, FileInfo]) -> list[dict]:
    """Every test method, grouped by class — the feature-guard's ground truth."""
    fact = re.compile(r"^\s*\[(?:AvaloniaFact|Fact|Theory|AvaloniaTheory)")
    method = re.compile(r"^\s*(?:public|private|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\]\?]+\s+(\w+)\s*\(")
    trait = re.compile(r'\[Trait\("(?P<k>[^"]+)",\s*"(?P<v>[^"]+)"\)\]')
    cls = re.compile(r"^\s*public\s+(?:sealed\s+)?class\s+(\w+)")

    entries: list[dict] = []
    for path, info in sorted(files.items()):
        if info.kind != "test":
            continue
        lines = (ROOT / path).read_text(encoding="utf-8", errors="ignore").splitlines()
        current_class = ""
        class_traits: list[str] = []
        pending = False
        pending_traits: list[str] = []
        for number, line in enumerate(lines, start=1):
            class_match = cls.match(line)
            if class_match:
                current_class = class_match.group(1)
                class_traits = pending_traits[:]
                pending_traits = []
            for t in trait.finditer(line):
                pending_traits.append(f"{t.group('k')}={t.group('v')}")
            if fact.match(line):
                pending = True
                continue
            if pending:
                m = method.match(line)
                if m:
                    entries.append(
                        {
                            "test": m.group(1),
                            "class": current_class,
                            "file": path,
                            "line": number,
                            "traits": sorted(set(class_traits + pending_traits)),
                        }
                    )
                    pending = False
                    pending_traits = []
    return entries


def human(name: str) -> str:
    """Turn a PascalCase_test_name into the sentence it was written as."""
    text = name.replace("_", " ")
    text = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text[0].upper() + text[1:] if text else text


def build() -> dict:
    paths = collect_files()
    files: dict[str, FileInfo] = {}
    for path in paths:
        rel = path.relative_to(ROOT).as_posix()
        info = FileInfo(path=rel, project=project_of(path), kind=classify(path))
        text = path.read_text(encoding="utf-8", errors="ignore")
        if path.suffix == ".axaml":
            parse_axaml(path, text, info)
        else:
            parse_source(path, text, info)
        files[rel] = info

    git_history(files)
    link_dependencies(files)
    score(files)
    tests = test_inventory(files)

    data = {
        "generated_from_commit": run(["git", "rev-parse", "--short", "HEAD"]).strip(),
        "file_count": len(files),
        "total_loc": sum(f.loc for f in files.values()),
        "test_count": len(tests),
        "files": {path: asdict(info) for path, info in sorted(files.items())},
        "tests": tests,
    }

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    (OUT_DIR / "map.json").write_text(json.dumps(data, indent=1), encoding="utf-8")
    write_index(data)
    write_hotspots(data)
    write_features(data)
    (OUT_DIR / ".stamp").write_text(fingerprint(), encoding="utf-8")
    return data


def write_index(data: dict) -> None:
    by_project: dict[str, list[dict]] = defaultdict(list)
    for path, info in data["files"].items():
        if info["kind"] == "test":
            continue
        by_project[info["project"]].append(info)

    out = [
        "# Code index",
        "",
        # Deliberately no commit stamp. This file is committed, so writing the
        # HEAD it was built from into it is self-defeating: committing the file
        # changes HEAD, the stamp is immediately one behind, and rebuilding to
        # fix it produces another commit that invalidates it again. Everything
        # below is derived from file contents, so it stays true across commits
        # and a rebuild after an unrelated commit is a no-op.
        f"{data['file_count']} files · {data['total_loc']} lines · "
        f"{data['test_count']} tests.",
        "",
        "Read this before searching. Each entry lists the types a file declares and",
        "the line they start on, so you can open the exact region instead of the",
        "whole file. `codemap.py find <term>` answers targeted questions.",
        "",
    ]
    for project in sorted(by_project):
        entries = sorted(by_project[project], key=lambda f: -f["loc"])
        out.append(f"## {project}")
        out.append("")
        for info in entries:
            if not info["types"] and info["loc"] < 20:
                continue
            names = ", ".join(
                f"{t['name']}:{t['line']}" if t["line"] else t["name"]
                for t in info["types"][:8]
            )
            suffix = " …" if len(info["types"]) > 8 else ""
            if info["tests"]:
                cover = f" · {len(info['tests'])} test files"
            elif info["tests_indirect"]:
                cover = f" · {len(info['tests_indirect'])} indirect only"
            else:
                cover = " · **no tests**"
            out.append(f"- `{info['path']}` ({info['loc']} ln){cover}")
            if names:
                out.append(f"  - {names}{suffix}")
        out.append("")
    (OUT_DIR / "INDEX.md").write_text("\n".join(out), encoding="utf-8")


def write_hotspots(data: dict) -> None:
    sources = [f for f in data["files"].values() if f["kind"] != "test"]
    ranked = sorted(sources, key=lambda f: -f["risk"])[:20]
    hottest = sorted(sources, key=lambda f: -f["heat"])[:10]

    out = [
        "# Hotspots",
        "",
        "Where change has been concentrated and where it is risky. **Heat** combines",
        "commit churn, fix-commit churn, how many files depend on this one, and size.",
        "**Risk** is heat discounted by how many test files exercise the file — a hot",
        "file with no tests is the top of this list for a reason.",
        "",
        "## Riskiest to change",
        "",
        "| File | Risk | Heat | Commits | Fixes | Dependents | Test files |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for info in ranked:
        out.append(
            f"| `{info['path']}` | {info['risk']:.2f} | {info['heat']:.2f} | "
            f"{info['commits']} | {info['fix_commits']} | {len(info['dependents'])} | "
            f"{len(info['tests'])} |"
        )
    out += ["", "## Most active regardless of coverage", ""]
    for info in hottest:
        out.append(
            f"- `{info['path']}` — heat {info['heat']:.2f}, "
            f"{info['commits']} commits ({info['fix_commits']} fixes), "
            f"{len(info['dependents'])} dependents"
        )
    untested = [f for f in sources
                if not f["tests"] and not f["tests_indirect"] and f["loc"] > 80]
    if untested:
        out += ["", "## Substantial files with no test reference", ""]
        for info in sorted(untested, key=lambda f: -f["loc"])[:15]:
            out.append(f"- `{info['path']}` ({info['loc']} ln)")
    (OUT_DIR / "HOTSPOTS.md").write_text("\n".join(out) + "\n", encoding="utf-8")


def write_features(data: dict) -> None:
    """The behaviour inventory: every test, phrased as the promise it guards."""
    by_class: dict[str, list[dict]] = defaultdict(list)
    for entry in data["tests"]:
        by_class[f"{entry['file']}::{entry['class']}"].append(entry)

    out = [
        "# Behaviour inventory",
        "",
        f"{data['test_count']} tests, derived from the suite itself. Each line is a",
        "promise the application currently keeps. Treat this as the regression",
        "contract: if a change makes one of these statements false, it is a",
        "regression even when every test still compiles.",
        "",
    ]
    for key in sorted(by_class):
        file_path, class_name = key.split("::")
        entries = by_class[key]
        traits = sorted({t for e in entries for t in e["traits"]})
        label = f" _{', '.join(traits)}_" if traits else ""
        out.append(f"## {class_name}{label}")
        out.append(f"`{file_path}`")
        out.append("")
        for entry in sorted(entries, key=lambda e: e["line"]):
            out.append(f"- {human(entry['test'])} — `:{entry['line']}`")
        out.append("")
    (OUT_DIR / "FEATURES.md").write_text("\n".join(out), encoding="utf-8")


def fingerprint() -> str:
    """
    Cheap staleness key: how many files there are and the newest mtime among
    them.

    HEAD used to be part of this, which meant every commit marked the index
    stale even when not one indexed file had changed — so the session hook
    rebuilt, the rebuild differed only in its own commit stamp, and that
    became a commit of its own. Staleness is about the source having moved,
    not about a commit having happened.
    """
    newest = 0.0
    count = 0
    for path in collect_files():
        newest = max(newest, path.stat().st_mtime)
        count += 1
    return f"{count}:{newest:.0f}"


def load() -> dict:
    target = OUT_DIR / "map.json"
    if not target.exists():
        print("No map yet — run: python3 scripts/codemap.py build", file=sys.stderr)
        sys.exit(2)
    return json.loads(target.read_text(encoding="utf-8"))


def cmd_find(term: str) -> None:
    data = load()
    needle = term.lower()
    type_hits, member_hits, test_hits = [], [], []

    for path, info in data["files"].items():
        for declared in info["types"]:
            if needle in declared["name"].lower():
                type_hits.append((path, declared, info))
        for member in info["members"]:
            if needle in member["name"].lower():
                member_hits.append((path, member))
    for entry in data["tests"]:
        if needle in entry["test"].lower() or needle in entry["class"].lower():
            test_hits.append(entry)

    if not (type_hits or member_hits or test_hits):
        print(f"No symbol matches '{term}'. Try a shorter term, or grep for prose.")
        return

    if type_hits:
        print(f"TYPES matching '{term}'")
        for path, declared, info in sorted(type_hits, key=lambda h: -h[2]["heat"])[:15]:
            cover = f"{len(info['tests'])} test files" if info["tests"] else "NO TESTS"
            print(f"  {declared['kind']:9} {declared['name']:32} {path}:{declared['line']}  [{cover}]")
            if info["dependents"]:
                shown = ", ".join(Path(d).name for d in info["dependents"][:5])
                print(f"            used by: {shown}{' …' if len(info['dependents']) > 5 else ''}")
    if member_hits:
        print(f"\nMEMBERS matching '{term}'")
        for path, member in member_hits[:25]:
            where = f"{path}:{member['line']}" if member["line"] else path
            print(f"  {member['type']}.{member['name']:28} {where}")
    if test_hits:
        print(f"\nTESTS matching '{term}'")
        for entry in test_hits[:20]:
            print(f"  {human(entry['test'])}\n      {entry['file']}:{entry['line']}")


PROMISE_CAP = 200


def cmd_promises(term: str) -> None:
    """
    The behaviour inventory, filtered — what FEATURES.md says about one area.

    FEATURES.md is 150 KB, which is ~38k tokens to read for a question about
    one brush or one docker, and `write_features` shows it is a pure rendering
    of `data["tests"]`. So this is the same inventory queried rather than
    dumped, and the file stays exactly as it is for anything that genuinely
    wants all of it.

    Three things it does that `cmd_find` does not, each because an agent
    checking promises needs it:

    - **Matches the file path too**, so "what covers the raster engine" is
      askable. `cmd_find` matches only the test and class name.
    - **Groups by class**, so the output reads like the inventory section it
      replaces rather than a flat list.
    - **Never truncates silently.** `cmd_find` caps test hits at 20 and says
      nothing, and a silent cap is precisely how a reader concludes a promise
      is missing when it was only cut off — the failure this command exists to
      avoid. The true match count and the inventory total are always printed.
    """
    data = load()
    needle = term.lower()
    hits = [
        entry for entry in data["tests"]
        if needle in entry["test"].lower()
        or needle in entry["class"].lower()
        or needle in entry["file"].lower()
    ]

    total = data["test_count"]
    if not hits:
        print(f"No promise matches '{term}' — {total} in the inventory.")
        print("Try a shorter term, a class name, or part of a path.")
        return

    by_class: dict[str, list[dict]] = defaultdict(list)
    for entry in hits:
        by_class[f"{entry['file']}::{entry['class']}"].append(entry)

    shown = hits[:PROMISE_CAP]
    print(f"PROMISES matching '{term}' — {len(hits)} across "
          f"{len(by_class)} test class(es), of {total} in the inventory")
    if len(hits) > PROMISE_CAP:
        print(f"  showing {PROMISE_CAP} of {len(hits)} — narrow the term to see the rest")

    budget = {id(e) for e in shown}
    for key in sorted(by_class):
        entries = [e for e in by_class[key] if id(e) in budget]
        if not entries:
            continue
        file_path, class_name = key.split("::")
        traits = sorted({t for e in entries for t in e["traits"]})
        label = f" _{', '.join(traits)}_" if traits else ""
        print(f"\n## {class_name}{label}\n`{file_path}`")
        for entry in sorted(entries, key=lambda e: e["line"]):
            print(f"  - {human(entry['test'])} — `:{entry['line']}`")


def cmd_file(target: str) -> None:
    data = load()
    info = data["files"].get(target)
    if info is None:
        matches = [p for p in data["files"] if target in p]
        if len(matches) != 1:
            print("Not indexed. Candidates:" if matches else "Not indexed.")
            for m in matches[:10]:
                print(f"  {m}")
            return
        info = data["files"][matches[0]]

    print(f"{info['path']}  ({info['loc']} lines, {info['project']}, {info['kind']})")
    print(f"heat {info['heat']:.2f} · risk {info['risk']:.2f} · {info['commits']} commits "
          f"({info['fix_commits']} fixes) · last touched {info['last_touched'] or 'unknown'}")
    if info["types"]:
        print("\ntypes:")
        for declared in info["types"]:
            print(f"  {declared['kind']:9} {declared['name']}  :{declared['line']}")
    if info["members"]:
        print("\npublic surface:")
        for member in info["members"][:40]:
            print(f"  :{member['line']:<5} {member['sig']}")
        if len(info["members"]) > 40:
            print(f"  … {len(info['members']) - 40} more")
    if info["tests"]:
        print("\nexercised by:")
        for test in info["tests"]:
            print(f"  {test}")
    elif info.get("tests_indirect"):
        print("\nno test names this file; reached indirectly by:")
        for test in info["tests_indirect"][:10]:
            print(f"  {test}")
        print("  (indirect cover fails for the wrong reason — a direct test is worth writing)")
    else:
        print("\nexercised by: NOTHING — changes here are unguarded")
    if info["dependents"]:
        print(f"\ndependents ({len(info['dependents'])}):")
        for dep in info["dependents"][:20]:
            print(f"  {dep}")


def is_stale() -> str:
    """Empty string when the map is current, otherwise why it is not."""
    stamp = OUT_DIR / ".stamp"
    if not stamp.exists():
        return "never built"
    if stamp.read_text(encoding="utf-8").strip() != fingerprint():
        return "sources changed since the last build"
    return ""


def cmd_stale() -> None:
    reason = is_stale()
    if reason:
        print(f"stale: {reason}")
        sys.exit(1)
    print("current")


def main() -> None:
    args = sys.argv[1:]
    if not args or args[0] in {"-h", "--help", "help"}:
        print(__doc__)
        return
    command = args[0]
    if command == "build":
        data = build()
        print(f"Indexed {data['file_count']} files, {data['total_loc']} lines, "
              f"{data['test_count']} tests → {OUT_DIR.relative_to(ROOT)}/")
    elif command == "find" and len(args) > 1:
        cmd_find(" ".join(args[1:]))
    elif command == "file" and len(args) > 1:
        cmd_file(args[1])
    elif command == "promises" and len(args) > 1:
        cmd_promises(" ".join(args[1:]))
    elif command == "stale":
        cmd_stale()
    elif command == "refresh":
        # Rebuild only when the sources have moved on. Cheap enough to run
        # after every turn: a no-op costs a few milliseconds of stat calls,
        # and a real rebuild is under a second.
        if is_stale():
            build()
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    try:
        main()
    except BrokenPipeError:
        # `codemap.py promises brush | head` closes the pipe early, which is
        # ordinary use of a query tool and not a failure. Without this, Python
        # prints a traceback that lands in an agent's transcript and reads as a
        # broken command — costing tokens and inviting a pointless
        # investigation. os._exit skips interpreter shutdown, which would
        # otherwise try to flush to the same closed pipe and raise again.
        os._exit(0)
