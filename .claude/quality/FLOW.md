# The flow — two tracks, one router

**Owner-only** (see `SENSITIVITY.md`): an agent proposes a change to this file and
does not make one. The reasons and what was left out are in
`docs/DESIGN-sensitive-topics.md`; the decision is Q191.

Every change starts at the same place — **triage** — and then takes one of two
tracks. Small, ordinary work is done inline, as it always was here. Anything
sensitive, large, or in a hotspot takes the full pipeline. The router decides, not
the session that wants the change to be small.

```
                    ┌─ FAST ── locate → test → implement → floor → adversary → commit
 change → TRIAGE ───┤
                    └─ FULL ── story → design → tests first → implement → review* → adversary → real-app → land

  * review is parallel and chosen by what the diff touched, not always all of it.
  A track is raised at any stage and never lowered.
```

## Stage 0 — triage

Run before editing, from the paths you expect to touch:

```bash
python3 scripts/sensitivity.py triage --files <path> [<path> …]
```

Mid-work, drop `--files` and it reads the diff against `main`. The `triage` agent
(`.claude/agents/triage.md`) runs the script and adds the two things a script cannot:
**is this one objective**, and **is anything here a decision rather than a task**. It
may raise the track. It may not lower it.

The script sends a change to **FULL** if *any* of these holds, and says which:

| Signal | Why it is not fast-track work |
| --- | --- |
| It touches a **sensitive path** — AI, MCP, importers, the saved format, projects, the process launchers, CI, hooks | These are the trust boundaries in `SENSITIVITY.md`. A one-line change to where a key is read from is small and is not fast |
| It touches an **owner-only** file | The owner decides, not the session |
| More than 5 files, or more than 150 changed lines — **tests, ledgers and the manual not counted** | Too big to hold the reasoning in one head without writing it down. Tests come with every change and are its guard, not its risk |
| A file whose hotspot risk is over 0.15 | `HOTSPOTS.md`: hot and thinly tested. Being wrong here is expensive |
| A **new source file** | A new type is a decision about where something lives |

The numbers are the owner's, in the JSON block of `SENSITIVITY.md`. They were
**calibrated by replay, on 2026-09-21**: the router was run over the last 40 pull
requests merged to `main`. Counting every line, 31 of 40 came out FULL, which would
have left the fast track nearly empty and taught people to route around it — the
cause was test lines, so tests are no longer counted. Counting source only, **23 came
out FAST and 17 FULL**; of the 29 that touched `src/`, 11 (38%) hit a sensitive path,
most of them the document model, which is where format risk lives. The hotspot
threshold (0.15) is set so that the six riskiest files at that date are FULL. What
this does not show is whether the FULL ones *needed* it — that needs the cycle ledger
that is deferred (Q191). Recalibrate by replay when it lands, and whenever a threshold
starts to feel arbitrary.

Triage's answer is a short block: the track, the reasons (cited by trigger), the
reviewers the diff calls for, and the branch name. Where the router and the agent
disagree, the higher track wins.

**A found bug is a separate objective and is triaged on its own** — the existing rule
(finish the branch, then give the bug its own branch) is unchanged, and it is usually
fast track.

## The fast track

For work that is small, ordinary and touches nothing sensitive. What it skips is
ceremony, and **never a check**:

1. **Locate.** `codemap.py find` — only if you do not already know.
2. **Test first.** A regression test that fails without the fix. This is not new; it
   is what closes a bug in `BUGS.md`.
3. **Implement**, the smallest change that moves it.
4. **The floor.** Build; the suite the change touches; `python3 scripts/sensitivity.py
   scan`; the hooks (they run by themselves). Then the surfaces walk in `CLAUDE.md`
   (*Land the feature, then land the places it shows up*) — the manual section, the
   `bugs.py` evidence line, the roadmap anchor.
5. **Adversary** on the claim "this is fixed" — one command that tries to break it.
6. **Commit** on the branch whose objective it is.

## The full track

For anything sensitive, large, hot or new. Each stage names its owner — most of them
already exist, which is the point (see the design document for why only two agents
were added).

| # | Stage | Who | Produces | Passes when |
| --- | --- | --- | --- | --- |
| 1 | **Story** | `story-analyst` | acceptance criteria; what can be built now and what needs a decision | every criterion is observable. Anything that does not resolve against `CLAUDE.md` goes to `AskUserQuestion` — never a guess |
| 2 | **Design** | the main thread, with the `scope-call` skill | a `docs/DESIGN-*.md`, or the roadmap item and a question file | it answers *what would a hostile file or a hostile agent do here*, names the `SENSITIVITY.md` rules the change engages and how the design meets each, and — for a document change — says what an old file does to it (W2, W5) |
| 3 | **Tests first** | `test-smith` | failing tests that fail for the right reason | a parser has a hostile-input test (S1); a format change has an old-fixture round trip (W8); a brush change draws a curve (O8) |
| 4 | **Implement** | the main thread | the change | tests green, and the registries walked. Deliberately not an agent: it holds the context and is the one the owner is talking to |
| 5 | **Review** | in parallel, by trigger — below | one verdict each | no BLOCKING verdict; every NOTE is answered by a change or a question |
| 6 | **Adversary** | `adversary` | CONFIRMED / REFUTED / PARTIAL per claim | nothing is reported as fixed on the strength of the fix's author |
| 7 | **Real app** | a headless pixel test, or the owner's own capture | evidence the artist's path works | for anything a pointer draws, the owner's capture outranks the suite (it has caught every latency fault the suite missed) |
| 8 | **Land** | `git-handler` | a pull request | ledgers, manual and roadmap are in the same commit; the owner merges |

**Review is chosen by what the diff touched**, and `sensitivity.py triage` prints the
list:

| The diff touches | Reviewer | Gate |
| --- | --- | --- |
| any sensitive path | `sensitivity-guardian` | G13 |
| `src/Lightbox.Ai`, the MCP surface, a prompt, an AI path in a view model | `ai-engineer` **and** `art-director` | G12 |
| XAML, a docker, a dialog, a row template | `ui-critic` | G9 |
| the brush engine, compositing, caches, export | `leak-hunter`, `perf-warden` | G7, G4 |
| several files by refactor | `feature-guard` | G3 |

Where two reviewers disagree and cannot measure, it becomes a question rather than
going to whoever ran last (the existing rule for the AI pair, generalised).

## Returns and limits

A stage may send work back to an earlier one, always with a reason that can be
checked. "Could be better" is not one.

| From | May return to | Typical reason |
| --- | --- | --- |
| tests first | design, story | the criterion is ambiguous or cannot be observed |
| implement | design, tests first | the design is infeasible; a test contradicts it |
| review | implement, design | a defect; or the design cannot satisfy a rule |
| adversary | implement, tests first | the fix is refuted; the test passes without it |
| real app | implement, tests first, design | the artist's path fails where the suite passed |

- **The same return, for substantially the same reason, three times** stops the work
  and goes to the owner as a question. The change is stuck, and a fourth attempt is
  not information.
- **More than six returns in one change** does the same.
- Any stage may **stop and ask** when the work would break a rule in `SENSITIVITY.md`,
  needs a decision the owner holds (a licence, an export of user data, a new egress
  path, a format), or is not one objective. A run that cannot reach the owner
  stops and asks in a pull request titled `[needs a decision] …` — the existing rule.

The limits are starting values, like the size thresholds, and for the same reason.

## What the tracks may never do

- **Skip the floor.** The hooks and `sensitivity.py scan` run on both tracks. The fast
  track being cheap is not a reason for it to be a route around the checks.
- **Lower a track.** Only the owner can say "treat this as fast", and then it is the
  owner's call and is recorded in the pull request.
- **Read a track off the size of a change.** Size only decides between tracks once
  nothing sensitive is touched.
- **Edit the files that define the tracks.** `SENSITIVITY.md`, this file, the
  charter, `CLAUDE.md`, `settings.json` and `guard.py` are owner-only; the hook asks
  before a session changes any of them.

## Not built yet

The pipeline the sibling project runs also has a **coach** (an agent that trains the
others, under tiers and an evidence rule), a **cycle ledger**, a cross-ledger
**recall**, and **token budgets**. They are specified for Lightbox in
`docs/DESIGN-sensitive-topics.md` and are the next branch, not this one — a coach
needs a ledger to read and a ledger needs cycles to have run. Until then the only
learning loop is G10 (*the loop learned*) and the owner's eye.
