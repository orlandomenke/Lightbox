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


class Index:
    """Everything the code index knows that an evidence anchor might name."""

    def __init__(self) -> None:
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
