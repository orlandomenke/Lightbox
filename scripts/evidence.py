#!/usr/bin/env python3
"""Resolving `evidence:` anchors against the generated code index.

Shared by `roadmap.py` and `bugs.py`. Both files make the same promise — that
a checkbox is *derived* from the code rather than asserted — and they must
agree on what "resolves" means, or a project would have two conventions that
drift apart. One resolver, imported twice.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MAP = ROOT / ".claude" / "codemap" / "map.json"


def _ensure_current() -> None:
    """Rebuild the index if the tree has moved under it.

    WHY THIS EXISTS, and it is the whole point of the module's promise. Both
    ledgers claim a checkbox is *derived* from the code. What they actually derive
    it from is `map.json` — which is **gitignored**, so `git switch` never touches
    it. Resolve against a leftover map and the derivation is real, current and
    about a tree nobody is looking at.

    It happened, and in both directions at once. On 2026-08-05 a `bugs.py check`
    on a freshly pulled `main` reported `DRIFTED B58 marked ' ' but the code says
    fixed` and `DRIFTED B77 marked 'x' but the code says open`. Neither was true:
    the map on disk had been built on a feature branch that contained B58's fix
    and predated B77's tests. One `codemap.py build` cleared both and changed no
    committed file — which is the tell, because a wrong answer that leaves no
    trace is the kind nobody catches.

    Rebuilding rather than refusing, because refusing turns `bugs.py check` in CI
    into a failure about tooling instead of a report about bugs — and because the
    session-start hook already rebuilds on the same staleness signal, so this is
    the existing rule applied at the point of use rather than a new one. It costs
    a second or two; a stale answer costs an investigation.
    """
    try:
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import codemap
    except Exception:
        # A resolver that cannot import the builder still resolves — against
        # whatever map exists. Better a possibly-stale answer than no answer.
        return

    reason = codemap.is_stale()
    if not reason:
        return
    print(f"evidence: the code index is stale ({reason}) — rebuilding", file=sys.stderr)
    try:
        codemap.build()
    except Exception as error:  # noqa: BLE001 - never block a report on the builder
        print(f"evidence: could not rebuild the index ({error})", file=sys.stderr)


class Index:
    """Everything the code index knows that an evidence anchor might name."""

    def __init__(self) -> None:
        _ensure_current()
        if not MAP.exists():
            sys.exit("No code index — run: python3 scripts/codemap.py build")
        data = json.loads(MAP.read_text(encoding="utf-8"))
        self.names: set[str] = set()
        self.paths: set[str] = set()
        for path, info in data["files"].items():
            self.paths.add(path)
            self.paths.add(Path(path).name)
            # The stem too: a test file is routinely named after the class it
            # holds, and an anchor naming one should not care which it meant.
            self.paths.add(Path(path).stem)
            for t in info.get("types", []):
                self.names.add(t["name"] if isinstance(t, dict) else str(t))
            for m in info.get("members", []):
                self.names.add(m["name"] if isinstance(m, dict) else str(m))
        for test in data.get("tests", []):
            self.names.add(test["test"])
            self.names.add(test["class"])

    def has(self, anchor: str) -> bool:
        anchor = anchor.strip()
        if not anchor:
            return False
        # `Type.Member` resolves if either half is known; a member alone is
        # ambiguous enough that requiring both would produce false negatives.
        if "." in anchor and "/" not in anchor:
            return any(part in self.names for part in anchor.split("."))
        if "/" in anchor:
            return anchor in self.paths or (ROOT / anchor).exists()
        return anchor in self.names or anchor in self.paths
