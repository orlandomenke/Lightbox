# The flow — three tracks over one stage list, and a router that forces stages on

**Owner-only** (see `SENSITIVITY.md`): an agent proposes a change to this file and
does not make one. The reasons, what was considered and rejected, and what is not
built are in `docs/DESIGN-sensitive-topics.md`; the decisions are Q191 and its
addendum.

There is one master list of stages. Every change runs some ordered subset of it,
chosen by what kind of change it is — a tweak, a bug, or a new capability — and
`SENSITIVITY.md`'s router can only ever **add** a stage or a reviewer back in,
never take one away. Nothing here is a pipeline you either take whole or skip
whole: **fixing is still the default** (`CLAUDE.md`), and the point of naming three
tracks instead of two is that a small fix and a small tweak are not the same
shape of small, so they should not be forced through the same shortcuts.

```
                    ┌─ MAINTENANCE ─ implement → floor → adversary → land
 change → TRIAGE ───┼─ BUGHUNT ───── test-first → implement → floor → adversary → (real app) → land
                    └─ FEATURE ───── story → design → test-first → implement → review → adversary → real app → land

  Escalation (SENSITIVITY.md) can force a stage or a reviewer back into ANY track.
  It never removes one. A track, once raised, is never lowered except by the owner.
```

## Stage 0 — triage

Run before editing, from the paths you expect to touch:

```bash
python3 scripts/sensitivity.py triage --files <path> [<path> …]
```

Mid-work, drop `--files` and it reads the diff against `main`. The `triage` agent
(`.claude/agents/triage.md`) does two things the script cannot:

1. **Names the track** — `MAINTENANCE`, `BUGHUNT` or `FEATURE`, from what kind of
   change this is (below). The script has no opinion on this; it cannot tell a
   tuning pass from a new capability from a file path.
2. **Decides whether this is one objective**, and whether any part of it is a
   decision rather than a task.

The script answers a narrower question — **is anything here non-negotiable** —
and prints `FULL` (something is) or `FAST` (nothing is) as a **floor**, not a
track name: `FULL` means specific stages and reviewers are forced on regardless
of which track the agent names; `FAST` means the track name is chosen purely by
the nature of the change, with nothing forcing it up.

### Which track

| The change is… | Track | Because |
| --- | --- | --- |
| A tuning pass, a rename, a ratchet raise, a config default, a dependency bump, a refactor with no behaviour change | **MAINTENANCE** | Nothing is broken and nothing new exists yet to design around |
| Fixing a reported defect — a bug in `BUGS.md`, a captured failure, a symptom an artist described | **BUGHUNT** | The defect *is* the story; what is missing is proof it is fixed, which only a regression test gives |
| A capability that does not exist today | **FEATURE** | Needs acceptance criteria and a design before a test can be written against either |

**A found bug is a separate objective from the one in hand** — the existing rule
(finish the branch, then give the bug its own branch) is unchanged. It goes
through its own triage, usually as `BUGHUNT`.

### What the script forces on, whatever the track

| Signal | Forces on | Why |
| --- | --- | --- |
| A **sensitive path** (`SENSITIVITY.md`'s A/L/W/S areas) | Design's threat-modelling paragraph, and `sensitivity-guardian` review (G13) | A one-line change to where a key is read from is small and is not exempt — Q190 |
| An **AI-pair path** (`ai_pair_paths`) | `ai-engineer` **and** `art-director` review (G12) | Found missing here by an adversarial review: the App-layer files that actually dispatch a request matched no glob until this was added |
| A **performance-critical path** (`performance_critical_paths`) | `leak-hunter` **and** `perf-warden` review (G4, G7) | **Added at the owner's request, 2026-09-21**, after the brush-performance pattern in `BUGS.md`: "the per-piece budgets all pass... it is in the sum... between the pieces" and "what closes this is a capture, not a green test", repeated across a dozen entries with a green suite the whole time. More stages do not catch that class of regression — a human capture does — but making the two reviewers *built* for the leak shapes a green suite cannot see non-optional on these paths is a real gap this closes: before, they ran only via `/improve` or a session's own choice, never automatically on a diff made directly |
| An **owner-only file** | The whole change escalates to `FEATURE` | The owner decides, not the session |
| More than 5 files or 150 changed lines (tests, ledgers, the manual excluded), or a **hotspot** over 0.15 risk, or a **new source file** | Story and Design are added back in, whatever track was named | Too big, too risky, or too undecided to hold in one head without writing it down |

The numbers were **calibrated by replay, on 2026-09-21**, over the last 40 pull
requests merged to `main`: counting every line 31 of 40 came out escalated
(tests dominate line counts, so they are excluded); counting source only, 23 of
40 stayed at the floor and 17 escalated, 11 of those (38%) on a sensitive path.
The performance-critical trigger separately escalates 9 of the same 40 (brush
engine, canvas rendering, the painting/rendering view-model partials) — files
that are also disproportionately on `HOTSPOTS.md`. **This shows the
distribution, not whether the escalated ones needed it** — that needs the cycle
ledger deferred in Q191. Recalibrate by replay when it lands, and whenever a
number starts to feel arbitrary.

## MAINTENANCE

For a tuning pass, a rename, a ratchet, a config default, a refactor with no
behaviour change. What it skips is ceremony, and **never a check**:

1. **Implement.** No dedicated test-first stage — nothing is being proven broken
   or newly true. Where the change touches behaviour, the implementer adds or
   updates the test that covers it as part of the same edit. **A maintenance
   change to a path with zero test coverage is itself a signal to raise the
   track** (O1 in the charter: an untested hotspot is the highest-value test to
   write, not a reason to skip writing one).
2. **The floor.** Build; the suite the change touches; `python3 scripts/sensitivity.py
   scan`; the hooks (they run by themselves). Then the surfaces walk in `CLAUDE.md`
   (*Land the feature, then land the places it shows up*).
3. **Review**, only the reviewers the trigger table above names — most maintenance
   changes name none.
4. **Adversary** on the claim "this is correct" — one command that tries to break it.
5. **Land** on the branch whose objective it is.

## BUGHUNT

For fixing a reported defect. The regression test is not optional here — it is
what `BUGS.md` already requires to close an entry, named as the evidence anchor.

1. **Test first.** A test that fails for the reason the bug was reported, before
   the fix exists. This is not new; it is the ledger's own standing rule.
2. **Implement**, the smallest change that makes the test pass.
3. **The floor**, as above.
4. **Review**, by the trigger table.
5. **Adversary** on the claim "this is fixed".
6. **Real app**, when the bug is about drawing, latency or anything a pointer
   touches — a headless pixel test, or the owner's own capture. **The owner's
   capture outranks the suite here**: every drawing-latency fault found so far
   was found in one, and the suite stayed green through all of them. A bug fix
   that only the suite has checked is not yet closed if the bug was about feel.
7. **Land**, with `BUGS.md`'s checkbox and evidence anchor in the same commit.

## FEATURE

For a capability that does not exist today. Each stage names its owner — most
already exist (see the design document for why only two agents were added).

| # | Stage | Who | Produces | Passes when |
| --- | --- | --- | --- | --- |
| 1 | **Story** | `story-analyst` | acceptance criteria; what can be built now and what needs a decision | every criterion is observable. Anything that does not resolve against `CLAUDE.md` goes to `AskUserQuestion` — never a guess |
| 2 | **Design** | the main thread, with the `scope-call` skill | a `docs/DESIGN-*.md`, or the roadmap item and a question file | it answers *what would a hostile file or a hostile agent do here*, names the `SENSITIVITY.md` rules the change engages and how the design meets each, and — for a document change — says what an old file does to it (W2, W5) |
| 3 | **Tests first** | `test-smith` | failing tests that fail for the right reason | a parser has a hostile-input test (S1); a format change has an old-fixture round trip (W8); a brush change draws a curve (O8) |
| 4 | **Implement** | the main thread | the change | tests green, and the registries walked. Deliberately not an agent: it holds the context and is the one the owner is talking to |
| 5 | **Review** | in parallel, by trigger — below | one verdict each | no BLOCKING verdict; every NOTE is answered by a change or a question |
| 6 | **Adversary** | `adversary` | CONFIRMED / REFUTED / PARTIAL per claim | nothing is reported as fixed on the strength of the fix's author |
| 7 | **Real app** | a headless pixel test, or the owner's own capture | evidence the artist's path works | as in BUGHUNT — the capture outranks the suite for anything a pointer draws |
| 8 | **Land** | `git-handler` | a pull request | ledgers, manual and roadmap are in the same commit; the owner merges |

**Review is chosen by what the diff touched**, and `sensitivity.py triage` prints
the list. This table is the same across all three tracks — a `MAINTENANCE` change
that happens to touch the AI layer still gets `ai-engineer`/`art-director`:

| The diff touches | Reviewer | Gate |
| --- | --- | --- |
| any sensitive path | `sensitivity-guardian` | G13 |
| any AI-pair path | `ai-engineer` **and** `art-director` | G12 |
| any performance-critical path | `leak-hunter` **and** `perf-warden` | G7, G4 |
| XAML, a docker, a dialog, a row template | `ui-critic` | G9 |
| several files by refactor | `feature-guard` | G3 |

Where two reviewers disagree and cannot measure, it becomes a question rather than
going to whoever ran last (the existing rule for the AI pair, generalised).

## Returns and limits

A stage may send work back to an earlier one, always with a reason that can be
checked. "Could be better" is not one.

| From | May return to | Typical reason |
| --- | --- | --- |
| tests first | design, story (`FEATURE`); nothing earlier on `BUGHUNT` | the criterion is ambiguous or cannot be observed; on `BUGHUNT` an unclear repro is a return to triage, not to a story stage that does not exist |
| implement | design, tests first | the design is infeasible; a test contradicts it |
| review | implement, design | a defect; or the design cannot satisfy a rule |
| adversary | implement, tests first | the fix is refuted; the test passes without it |
| real app | implement, tests first, design | the artist's path fails where the suite passed |

- **The same return, for substantially the same reason, three times** stops the
  work and goes to the owner as a question. The change is stuck, and a fourth
  attempt is not information. On `MAINTENANCE`/`BUGHUNT`, three returns from
  *any* stage is also the trigger to raise the track to `FEATURE` — a tweak that
  keeps bouncing was not a tweak.
- **More than six returns in one change** does the same.
- Any stage may **stop and ask** when the work would break a rule in `SENSITIVITY.md`,
  needs a decision the owner holds (a licence, an export of user data, a new egress
  path, a format), or is not one objective. A run that cannot reach the owner
  stops and asks in a pull request titled `[needs a decision] …` — the existing rule.

The limits are starting values, like the size thresholds, and for the same reason.

## What no track may ever do

- **Skip the floor.** The hooks and `sensitivity.py scan` run on all three tracks.
  Being cheap is not a reason to route around a check.
- **Lower a track, or drop a stage the escalation table added.** Only the owner
  can say "treat this as smaller than the router says", and then it is recorded
  in the pull request.
- **Choose the track by size alone.** Size is one of several things that can
  escalate; it never by itself decides `MAINTENANCE` versus `BUGHUNT` versus
  `FEATURE` — the *nature* of the change does.
- **Edit the files that define the tracks.** `SENSITIVITY.md`, this file, the
  charter, `CLAUDE.md`, `settings.json` and `guard.py` are owner-only; the hook
  asks before a session changes any of them.

## Not built yet

The pipeline the sibling project runs also has a **coach** (an agent that trains
the others, under tiers and an evidence rule), a **cycle ledger**, a cross-ledger
**recall**, and **token budgets**. They are specified for Lightbox in
`docs/DESIGN-sensitive-topics.md` and are the next branch, not this one — a coach
needs a ledger to read and a ledger needs cycles to have run. Until then the only
learning loop is G10 (*the loop learned*) and the owner's eye. The same is true of
whether `MAINTENANCE`/`BUGHUNT`/`FEATURE` are the right three names, or the right
stage lists within them: this is a first cut, stated as a first cut, and the
ledger is what would turn "this feels right" into a measurement.
