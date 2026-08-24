#!/usr/bin/env python3
"""Measure what every session pays before it has done anything.

WHY THIS EXISTS. `CLAUDE.md` is loaded into every session and inherited by
every subagent, so a paragraph added to it is not paid once — it is paid once
per agent per session, forever. Nothing measured that, so it only ever grew:
by 2026-08-24 it was 37,909 characters, and *half of it was two sections*.

The thing worth measuring is not the file's size. It is the split between the
**rule** and the **incident that produced the rule**. Almost every section here
is a short imperative followed by a long, well-written account of the day it was
learned — and the account is what makes it persuasive to a reader and what makes
it expensive to a session that was never going to touch that area. A rule has to
be resident to be obeyed. Its history only has to be *reachable*.

So this reports three numbers rather than one:

    resident    loaded on every session, and again inside every subagent
    on-demand   skills and agent definitions, loaded only when invoked
    fan-out     resident x the agents a fanned-out round actually spawns

The estimate is characters / 4, which is what `docs/DESIGN-ai-payload.md`
already uses so the two agree. For prose that is close; for the JSON that doc
measures it is a floor, because tokenizers split digit runs. Bytes are the hard
number and are always shown beside it.

Commands
    measure         the full picture: resident, on-demand, fan-out
    sections        CLAUDE.md broken down by heading, largest first
    check           exit 1 if the resident prelude exceeds its ratchet
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CLAUDE_MD = ROOT / "CLAUDE.md"
SKILLS = ROOT / ".claude" / "skills"
AGENTS = ROOT / ".claude" / "agents"
RATCHET = ROOT / ".claude" / "quality" / "PRELUDE.md"

# What a fanned-out round actually spawns. `improve-loop.js` runs several
# independent lenses per round and refutes each finding, and every one of those
# is a fresh context that inherits CLAUDE.md. Six is the figure LOOP.md records
# for a round's assessment pass; the multiplier is the point, not the exact six.
FANOUT_AGENTS = 6


def tokens(chars: int) -> int:
    return chars // 4


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        return ""


def split_sections(src: str) -> list[tuple[str, int, str]]:
    """(heading, chars, level-prefixed display name), in file order."""
    out: list[tuple[str, int, str]] = []
    cur: list[str] = []
    name, level = "(preamble)", 1
    for line in src.split("\n"):
        m = re.match(r"^(#{1,4})\s+(.*)", line)
        if m:
            if cur:
                out.append((name, len("\n".join(cur)), "  " * (level - 1) + name))
            level, name = len(m.group(1)), m.group(2).strip()
            cur = [line]
        else:
            cur.append(line)
    if cur:
        out.append((name, len("\n".join(cur)), "  " * (level - 1) + name))
    return out


def ratchet_limit() -> int | None:
    """The ceiling, in characters, from the one-file-per-budget ratchet."""
    text = read(RATCHET)
    m = re.search(r"^\s*budget:\s*([0-9_]+)\s*$", text, re.MULTILINE)
    return int(m.group(1).replace("_", "")) if m else None


def cmd_sections() -> int:
    src = read(CLAUDE_MD)
    if not src:
        print("CLAUDE.md not found", file=sys.stderr)
        return 1
    secs = [s for s in split_sections(src) if s[1] >= 40]
    total = len(src)
    print(f"CLAUDE.md  {total:,} chars  ~{tokens(total):,} tokens\n")
    print(f"{'chars':>7} {'~tok':>6} {'%':>5}  section")
    print("-" * 74)
    for name, chars, display in sorted(secs, key=lambda s: -s[1]):
        print(f"{chars:>7,} {tokens(chars):>6,} {100*chars/total:>4.0f}%  {name}")
    return 0


def cmd_measure() -> int:
    claude = len(read(CLAUDE_MD))

    skills = sorted(SKILLS.glob("*/SKILL.md"))
    agents = sorted(AGENTS.glob("*.md"))
    skill_chars = sum(len(read(p)) for p in skills)
    agent_chars = sum(len(read(p)) for p in agents)

    print("RESIDENT — paid on every session, and again inside every subagent")
    print(f"  CLAUDE.md                 {claude:>8,} chars  ~{tokens(claude):>6,} tokens")
    limit = ratchet_limit()
    if limit:
        room = limit - claude
        state = f"{room:,} under" if room >= 0 else f"{-room:,} OVER"
        print(f"  ratchet                   {limit:>8,} chars  ({state})")

    print("\nON DEMAND — loaded only when the work asks for it")
    for p in skills:
        c = len(read(p))
        print(f"  skill/{p.parent.name:<20}{c:>8,} chars  ~{tokens(c):>6,} tokens")
    print(f"  {'-- skills total':<26}{skill_chars:>8,} chars  ~{tokens(skill_chars):>6,} tokens")
    print(f"  {'-- agent definitions':<26}{agent_chars:>8,} chars  ~{tokens(agent_chars):>6,} tokens"
          f"   ({len(agents)} agents)")

    print(f"\nFAN-OUT — one round spawning {FANOUT_AGENTS} subagents")
    inherited = claude * FANOUT_AGENTS
    print(f"  inherited CLAUDE.md       {inherited:>8,} chars  ~{tokens(inherited):>6,} tokens")
    print(f"  + the session's own copy  {claude:>8,} chars  ~{tokens(claude):>6,} tokens")
    grand = inherited + claude
    print(f"  {'= before any work':<26}{grand:>8,} chars  ~{tokens(grand):>6,} tokens")
    return 0


def cmd_check() -> int:
    limit = ratchet_limit()
    if limit is None:
        print(f"prelude: no ratchet at {RATCHET.relative_to(ROOT)}", file=sys.stderr)
        return 1
    claude = len(read(CLAUDE_MD))
    if claude > limit:
        print(
            f"prelude: CLAUDE.md is {claude:,} chars, over its {limit:,} ceiling by "
            f"{claude - limit:,}.\n"
            "  A rule belongs in CLAUDE.md; the incident that produced it belongs in a\n"
            "  skill. If the new text is history, move it. If it is genuinely a rule\n"
            "  every session needs, raise the ratchet and say why in\n"
            f"  {RATCHET.relative_to(ROOT)}.",
            file=sys.stderr,
        )
        return 1
    print(f"prelude: CLAUDE.md {claude:,} chars, {limit - claude:,} under its ceiling")
    return 0


def main(argv: list[str]) -> int:
    cmd = argv[1] if len(argv) > 1 else "measure"
    if cmd == "measure":
        return cmd_measure()
    if cmd == "sections":
        return cmd_sections()
    if cmd == "check":
        return cmd_check()
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
